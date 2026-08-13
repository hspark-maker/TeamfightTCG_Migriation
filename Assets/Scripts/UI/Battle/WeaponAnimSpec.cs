using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>카드 프레임에 얹힌 **무기 장식의 애니메이션 구간 재생**을 소유한다(원거리 카드의 활 등).
/// 그 장식 오브젝트(CardView의 Frame 아래) 루트에 붙인다.
///
/// 이 장식은 CardData.weaponPrefab으로 만들어지는 무기(<see cref="CardWeaponView"/>)와 다른 것이다 —
/// 저쪽은 런타임에 Instantiate하는 별도 프리팹이고, 이쪽은 카드 프레임 아트의 일부라 이미 카드 안에 있다.
/// 켜고 끄는 주인은 여전히 CardView.keywordFrames다(원거리 키워드일 때만 보인다).
/// 여기가 아는 것은 "언제 어느 구간을 트는가" 하나뿐이다.
///
/// 왜 구간을 나누는가: 활 클립 하나에 당김 → 최대 당김 유지 → 릴리즈 → 복귀가 이어져 있다.
/// 통째로 틀면 무장하는 순간 쏘는 동작까지 다 지나가 버린다. 무장에서 당김 끝에 세우고,
/// 발사 명령이 오면 유지 구간의 끝에서 이어 재생한다.</summary>
public class WeaponAnimSpec : MonoBehaviour
{
    [Tooltip("제어할 Animator. 비우면 이 오브젝트에서 찾는다.")]
    [SerializeField] Animator animator;

    [Tooltip("Animator 상태 이름.")]
    [SerializeField] string stateName = "Attack";

    [Tooltip("무장(조준)에서 여기까지 재생하고 멈춘다. 초 단위 — 활이 최대로 당겨지는 시각.")]
    [Min(0f)] [SerializeField] float drawSeconds = 0.55f;

    [Tooltip("발사할 때 이 정규화 시간(0~1)부터 이어 재생한다. 당김을 유지하는 구간의 끝.")]
    [Range(0f, 1f)] [SerializeField] float fireStart01 = 0.465f;

    [Tooltip("클립 전체 길이(초). 발사 구간의 원본 길이를 재는 데 쓴다.")]
    [Min(0f)] [SerializeField] float clipSeconds = 1.65f;

    [Tooltip("발사 구간(시위를 놓고 되돌아오기까지)을 이 시간에 재생한다. 0 이하면 클립 원본 속도.")]
    [Min(0f)] [SerializeField] float fireSeconds = 0.1f;

    [Tooltip("임시 진단. 켜면 호출과 Animator 상태를 콘솔에 찍는다.")]
    [SerializeField] bool logDiagnostics;

    // 예약된 "당김 정지"를 무효화하는 세대 번호. 조준을 풀거나 곧바로 쏘면 그 예약이 뒤늦게
    // speed=0을 걸어 날아가던 발사 동작이 그대로 얼어붙는다.
    int generation;

    // 발사 재생 중인가. 무장 해제(ResetToIdle)가 이걸 밟고 지나가면 안 된다 —
    // AttackSequence.ResolveHits가 접촉 직후 무장을 푸는데, 그 시점엔 활이 아직 쏘는 중이다.
    bool firing;

    void Awake()
    {
        if (this.animator == null) this.animator = GetComponent<Animator>();
    }

    // 오브젝트가 켜지는 순간 Animator는 기본 상태를 저절로 처음부터 재생한다 —
    // 그대로 두면 카드가 화면에 뜨자마자 활이 혼자 한 번 당겼다 놓는다. 첫 프레임에 세워 대기 자세로 둔다.
    void OnEnable() => ResetToIdle();

    /// <summary>대기 자세(시위를 놓은 상태)로 세운다.
    /// 쏘는 중이면 무시한다 — 무장 해제는 발사보다 먼저 도착한다(ResolveHits가 접촉 직후 푼다).
    /// 여기서 되감으면 발사 동작이 시작하자마자 사라진다.</summary>
    public void ResetToIdle()
    {
        if (this.firing) { Log("ResetToIdle (무시 — 발사 중)"); return; }

        this.generation++;
        Log("ResetToIdle");
        if (!Ready) return;

        this.animator.speed = 1f;
        this.animator.Play(this.stateName, 0, 0f);
        this.animator.Update(0f);   // 이 프레임에 0초 포즈를 실제로 적용한 뒤 세운다
        this.animator.speed = 0f;
    }

    /// <summary>무장: 시위를 당기고 그 자세로 멈춘다.</summary>
    public void Draw()
    {
        if (this.firing) { Log("Draw (무시 — 발사 중)"); return; }

        this.generation++;
        Log("Draw");
        if (!Ready) return;

        this.animator.speed = 1f;
        this.animator.Play(this.stateName, 0, 0f);

        HoldAtDrawEnd(this.generation).Forget();
    }

    /// <summary>발사: 당김을 유지하던 지점부터 이어 쏜다.
    /// 처음부터 다시 틀면 이미 당겨져 있던 활이 풀렸다가 다시 당겨지는 그림이 된다.</summary>
    public void Fire()
    {
        this.generation++;
        Log("Fire");
        if (!Ready) return;

        // 쏘는 동작은 당기는 동작보다 빨라야 "튕겨나갔다"로 읽힌다 — 클립 원본 속도로 두면
        // 되돌아오는 구간까지 늘어져 발사가 느릿하게 보인다. 남은 구간을 fireSeconds에 눌러 담는다.
        this.animator.speed = FireSpeed;
        this.animator.Play(this.stateName, 0, this.fireStart01);

        this.firing = true;
        ClearFiringWhenDone(this.generation).Forget();
    }

    // 발사 구간의 원본 길이와 요청 시간의 비. fireSeconds가 0 이하면 원본 속도(1배).
    float FireRemainSeconds => Mathf.Max(0f, this.clipSeconds * (1f - this.fireStart01));

    float FireSpeed => this.fireSeconds > 0f && FireRemainSeconds > 0f
        ? FireRemainSeconds / this.fireSeconds
        : 1f;

    // 발사 구간이 다 돌면 잠금을 푼다. 그동안 밀린 무장 해제를 여기서 한 번 반영한다 —
    // 안 그러면 활이 쏜 자세(클립 마지막 프레임)로 굳은 채 다음 무장을 맞는다.
    async UniTaskVoid ClearFiringWhenDone(int _generation)
    {
        // 기다리는 시간은 배속을 적용한 실제 재생 시간이다(원본 길이가 아니라).
        float t_remain = this.fireSeconds > 0f ? this.fireSeconds : FireRemainSeconds;
        await UniTask.Delay((int)(t_remain * 1000));

        if (_generation != this.generation) return;   // 그 사이 다시 쐈거나 상태가 바뀌었다

        this.firing = false;
        ResetToIdle();
    }

    // 꺼져 있는 동안에는 Animator가 평가되지 않으므로 Play/speed가 모두 무의미하다.
    // 원거리가 아닌 카드는 이 장식 자체가 꺼져 있어 여기서 전부 걸러진다.
    bool Ready => this.animator != null && this.animator.isActiveAndEnabled;

    void Log(string _where)
    {
        if (!this.logDiagnostics) return;

        if (this.animator == null)
        {
            Debug.Log($"[WeaponAnimSpec] {_where} on '{name}': animator=null", this);
            return;
        }

        var t_info = this.animator.isActiveAndEnabled && this.animator.runtimeAnimatorController != null
            ? this.animator.GetCurrentAnimatorStateInfo(0)
            : default;

        Debug.Log($"[WeaponAnimSpec] {_where} on '{name}': " +
                  $"activeInHierarchy={this.animator.gameObject.activeInHierarchy} " +
                  $"animEnabled={this.animator.enabled} ready={Ready} " +
                  $"controller={(this.animator.runtimeAnimatorController != null ? this.animator.runtimeAnimatorController.name : "null")} " +
                  $"hasState('{this.stateName}')={this.animator.HasState(0, Animator.StringToHash(this.stateName))} " +
                  $"speed={this.animator.speed} normTime={t_info.normalizedTime:F3} len={t_info.length:F2}", this);
    }

    async UniTaskVoid HoldAtDrawEnd(int _generation)
    {
        await UniTask.Delay((int)(Mathf.Max(0f, this.drawSeconds) * 1000));

        if (_generation != this.generation) return;   // 그 사이 조준이 풀렸거나 이미 쐈다
        if (!Ready) return;

        this.animator.speed = 0f;
    }
}
