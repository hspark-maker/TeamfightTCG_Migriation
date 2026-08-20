using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

// 튜토리얼 진행으로 아직 열리지 않은 UI에 잠김 룩을 얹는 표시 컴포넌트.
// 판정은 갖지 않는다 — OutgameFeatureLock이 단일 진실원이고 여기는 그 결과를 그리기만 한다.
//
// 차단은 이 컴포넌트의 몫이 아니다. interactable을 세우는 주체는 각 화면의 계산식이어야 한다 —
// 여기서 함께 만지면 매 갱신마다 서로 덮어써 어느 쪽이 이겼는지가 호출 순서에 달리게 된다.
//
// 룩은 두 겹이다: 대상 전체 탈채도 + 그 위에 얹는 자물쇠 배지. 탈채도만으로도 "지금은 못 쓴다"가 성립하고
// 배지는 "나중에 열린다"를 덧붙이는 것이라, 배지 프리팹이 없으면 경고 한 번 뒤 탈채도만으로 간다.
public class FeatureLockView : MonoBehaviour
{
    [Tooltip("이 UI를 여는 기능 키. None이면 항상 열려 있다")]
    [SerializeField] EOutgameFeature feature;

    [Tooltip("자물쇠 배지를 놓을 자리. 비우면 자기 RectTransform")]
    [SerializeField] RectTransform badgeParent;

    [Tooltip("해제되는 순간 자물쇠가 터지는 길이(초). 0이면 예전처럼 즉시 사라진다")]
    [SerializeField] float unlockFxDuration = 0.25f;

    GameObject m_badge;
    bool       m_badgeMissing;   // 프리팹 미배치 경고·재시도는 1회로 끝낸다
    Vector3    m_badgeScale0;    // 연출이 잘려도 저작 배율로 돌아갈 자리

    Sequence m_unlockFx;
    bool     m_wasLocked;        // 직전 적용 결과. 잠김→활성 뒤집힘을 이 컴포넌트가 스스로 잡는다
    bool     m_synced;           // 첫 적용은 상태 맞추기일 뿐이라 연출을 태우지 않는다

    List<UiGrayscale.Toned> m_toned;

    public EOutgameFeature Feature => feature;

    /// <summary>지금 이 UI가 잠겨 있는가. 튜토리얼 게이트가 "왜 타깃이 안 눌리는지"를 진단할 때 읽는다.</summary>
    public bool IsLocked => feature != EOutgameFeature.None && !OutgameFeatureLock.IsUnlocked(feature);

    /// <summary>런타임 부착 창구. 프리팹에 컴포넌트를 못 붙이는 대상(Layer Lab 인스턴스 안의 stripped Button 등)이나
    /// 잠금 대상이 코드로만 정해지는 자리에서 쓴다. 대상마다 거대 프리팹을 열지 않아도 되는 것이 요점이다.
    ///
    /// 한 번만 부르면 된다 — 이후 해금 반영은 이 컴포넌트가 OnChanged를 구독해 스스로 한다.</summary>
    public static void Attach(GameObject _target, EOutgameFeature _feature)
    {
        if (_target == null || _feature == EOutgameFeature.None) return;

        var t_view = _target.GetComponent<FeatureLockView>();
        if (t_view == null) t_view = _target.AddComponent<FeatureLockView>();

        // 저작 시절 꺼둔 채 남은 인스턴스가 있다 — GetComponent는 그것도 찾아오므로 켜 주지 않으면
        // OnEnable이 돌지 않아 구독도 룩도 없이 조용히 넘어간다.
        t_view.enabled = true;
        t_view.Bind(_feature);
    }

    /// <summary>AddComponent 직후엔 OnEnable이 이미 지나갔으므로(구독만 걸린 채 feature가 비어 있다) 여기서 다시 적용한다.</summary>
    public void Bind(EOutgameFeature _feature)
    {
        this.feature = _feature;

        if (isActiveAndEnabled) Apply(_silent: true);
    }

    void OnEnable()
    {
        OutgameFeatureLock.OnChanged += OnLockChanged;

        // 켜질 때는 상태만 맞춘다. 꺼져 있는 동안 열린 기능까지 여기서 터뜨리면
        // 사건이 일어난 시점과 보이는 시점이 갈려 "방금 열렸다"로 안 읽힌다.
        Apply(_silent: true);
    }

    void OnDisable()
    {
        OutgameFeatureLock.OnChanged -= OnLockChanged;
        KillUnlockFx();
        HideBadge();
    }

    void OnLockChanged() => Apply(_silent: false);

    void Apply(bool _silent)
    {
        // 판정을 먼저 받는다 — 조회가 내부에서 OnChanged를 동기 발화해 이 메서드가 스스로 재진입할 수 있다.
        // 상태를 그 뒤에만 만지면 중첩 호출이 먼저 끝나도 바깥이 같은 값으로 다시 세워 결과가 어긋나지 않는다.
        bool t_unlocked = OutgameFeatureLock.IsUnlocked(this.feature);

        // 잠김→활성 뒤집힘은 이 위젯이 스스로 잡는다. OnChanged는 "무엇이 열렸는지"를 실어 주지 않지만
        // 각자 자기 직전 상태를 알고 있어 그것만으로 "방금 내가 열렸다"가 성립한다.
        // 판정을 IsUnlocked 뒤에 두는 것이 재진입 방어이기도 하다 — 중첩 호출이 이미 상태를 갱신했으면
        // 바깥은 여기서 false를 받아 같은 해제로 두 번 터지지 않는다.
        bool t_justUnlocked = !_silent && this.m_synced && this.m_wasLocked && t_unlocked;

        this.m_wasLocked = !t_unlocked;
        this.m_synced    = true;

        // 다시 칠하기 전에 항상 저작값으로 되돌린다 — 잠긴 채로 자식이 늘어났을 수 있어 목록을 매번 새로 뜬다.
        UiGrayscale.Restore(this.m_toned);

        if (t_unlocked)
        {
            if (t_justUnlocked) PlayUnlockFx();
            else              { KillUnlockFx(); HideBadge(); }
            return;
        }

        KillUnlockFx();
        EnsureBadge();

        if (this.m_badge != null)
        {
            RestoreBadge();
            this.m_badge.SetActive(true);
        }

        // 배지는 탈채도에서 제외한다. 자물쇠가 원래 무채색이라 지금은 티가 안 나지만, 생성 순서에 따라
        // 걸리기도 안 걸리기도 하는 상태를 남기면 나중에 색 있는 배지로 갈았을 때 조용히 회색이 된다.
        this.m_toned = UiGrayscale.Apply(gameObject, this.m_badge != null ? this.m_badge.transform : null);
    }

    /// <summary>잠김이 걷히는 한 박 — 자물쇠가 부풀며 사라지고 같은 박자에 대상이 한 번 튄다.
    /// 색은 이 메서드에 오기 전에 이미 돌아와 있다. 원색이 자물쇠와 함께 걷혀서는 안 되고
    /// 자물쇠가 터지는 프레임에 이미 들어와 있어야 "열렸다"가 사건으로 읽힌다.</summary>
    void PlayUnlockFx()
    {
        KillUnlockFx();

        if (this.m_badge == null || this.unlockFxDuration <= 0f)
        {
            HideBadge();
            return;
        }

        var t_tr = this.m_badge.transform;
        var t_cg = this.m_badge.GetComponent<CanvasGroup>();
        if (t_cg == null) t_cg = this.m_badge.AddComponent<CanvasGroup>();

        this.m_badge.SetActive(true);
        t_tr.localScale = this.m_badgeScale0;
        t_cg.alpha      = 1f;

        float t_len = this.unlockFxDuration;

        this.m_unlockFx = DOTween.Sequence().SetLink(gameObject)
                                 .Append(t_tr.DOScale(this.m_badgeScale0 * 1.6f, t_len).SetEase(Ease.OutQuad))
                                 .Join(t_cg.DOFade(0f, t_len).SetEase(Ease.InQuad))
                                 .OnComplete(HideBadge);

        UiPunch.Play(transform);
    }

    void KillUnlockFx()
    {
        if (this.m_unlockFx == null) return;

        // 먼저 비우고 죽인다 — Kill이 부르는 콜백이 다시 이 메서드로 들어와도 한 번만 돈다.
        Sequence t_fx = this.m_unlockFx;
        this.m_unlockFx = null;
        t_fx.Kill();
    }

    void HideBadge()
    {
        if (this.m_badge == null) return;

        RestoreBadge();
        this.m_badge.SetActive(false);
    }

    /// <summary>연출이 정상 종료했든 잘렸든 배지를 저작 상태로 되돌린다 — 되감기나 재잠금으로
    /// 다시 켜질 때 투명하거나 부푼 자물쇠가 뜨지 않게(멱등).</summary>
    void RestoreBadge()
    {
        if (this.m_badge == null) return;

        this.m_badge.transform.localScale = this.m_badgeScale0;

        var t_cg = this.m_badge.GetComponent<CanvasGroup>();
        if (t_cg != null) t_cg.alpha = 1f;
    }

    void EnsureBadge()
    {
        if (this.m_badge != null || this.m_badgeMissing) return;

        var t_parent = this.badgeParent != null ? this.badgeParent : transform as RectTransform;
        if (t_parent == null)
        {
            this.m_badgeMissing = true;
            Debug.LogWarning($"[FeatureLockView] '{name}'이 RectTransform이 아니라 자물쇠를 얹을 자리가 없습니다.");
            return;
        }

        var t_prefab = SyncUiPrefabs.Get(ESyncUiPrefab.LockBadge);
        if (t_prefab == null)
        {
            this.m_badgeMissing = true;
            Debug.LogWarning($"[FeatureLockView] 동기 UI 카탈로그 자물쇠 미배선 — '{name}'의 자물쇠를 그리지 못합니다(잠김은 흑백으로만 보입니다).");
            return;
        }

        // 저작된 앵커·비율을 그대로 살린다. 여기서 크기를 다시 잡으면 대상마다 어긋나던 옛 방식으로 돌아간다.
        this.m_badge      = Instantiate(t_prefab, t_parent, false);
        this.m_badge.name = "LockBadge";
        this.m_badge.transform.SetAsLastSibling();

        this.m_badgeScale0 = this.m_badge.transform.localScale;
    }
}
