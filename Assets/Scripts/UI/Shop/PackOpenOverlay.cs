using System;
using UnityEngine;

// 카드팩 개봉 화면의 수명 창구. 로비 위에 겹쳐 뜨는 개봉 오버레이를 여닫는 단일 지점이다.
//
// 왜 있나: 개봉이 별도 씬이던 시절엔 씬 로드/언로드가 네 가지 일을 암묵적으로 해줬다 —
//   튜토리얼 스텝 재적용 · 로비 획득 연출 재생 · 구매 잠금 해제 · 개봉 뷰 상태 초기화.
//   오버레이는 씬을 바꾸지 않으므로 아무도 해주지 않는다. 그래서 OnOpened/OnClosed 두 신호로 명시한다.
//   구독자가 씬 이름을 보지 않게 하는 것이 목적이다(PackRevealView.OnAnyPackOpened와 같은 방향).
//
// 여닫는 대상은 이 오브젝트의 **root** 하나다(로비의 다른 저작 오버레이 — CardDetailOverlayView ·
//   RewardClaimPopup · MatchDeckShell — 와 같은 축). 그래서 씬에는 비활성으로 배치되고,
//   비활성 오브젝트는 Awake가 돌지 않으므로 인스턴스는 Awake 선점이 아니라 첫 호출 때
//   비활성 포함으로 찾아 캐시한다(CardDetailOverlayView.Resolve와 같은 관용구).
[DisallowMultipleComponent]
public class PackOpenOverlay : MonoBehaviour
{
    static PackOpenOverlay s_instance;

    /// <summary>씬에 배치된 개봉 오버레이. 꺼져 있어도 찾아낸다(없으면 null).</summary>
    public static PackOpenOverlay Instance => Resolve();

    /// <summary>개봉 화면이 떠 있는가. 로비 쪽 안내·입력을 억제할 때 본다.</summary>
    public static bool IsOpen { get; private set; }

    // 세션을 뷰에 태운 직후. 이 시점엔 IsOpen이 이미 true다.
    public static event Action OnOpened;
    // 닫고 뷰를 되돌린 직후. 이 시점엔 IsOpen이 이미 false다(구독자가 "닫힌 뒤"를 전제로 움직인다).
    public static event Action OnClosed;

    [Tooltip("개봉 세션을 태울 브레인. 미배선이면 자식에서 찾는다.")]
    [SerializeField] PackAcquireController controller;
    [Tooltip("개봉 연출 뷰. 닫을 때 세션을 되돌린다. 미배선이면 자식에서 찾는다.")]
    [SerializeField] PackRevealView view;

    /// <summary>PackHandoff에 세션이 실려 있으면 개봉 화면을 연다. 열지 못하면 false(호출부가 판단).</summary>
    public static bool TryOpen()
    {
        PackOpenOverlay t_overlay = Resolve();
        if (t_overlay == null)
        {
            Debug.LogWarning("[PackOpenOverlay] 인스턴스 없음 — 개봉 화면을 열 수 없다(로비 씬 배치 확인).");
            return false;
        }

        return t_overlay.Open();
    }

    /// <summary>개봉 화면을 닫고 뷰 세션을 되돌린다(다음 개봉을 받을 수 있는 상태로).</summary>
    public void Close()
    {
        if (!IsOpen) return;

        // 플래그를 먼저 내린다 — 아래 SetActive(false)가 OnDisable 안전판을 부르는데,
        // 거기서 같은 뒷정리가 한 번 더 돌지 않게 한다.
        IsOpen = false;

        // 먼저 되돌린 뒤 끈다 — 반대로 하면 OnDisable이 요약 상태를 일부러 남겨(중복 발화 방지)
        // 다음 BeginOpen이 재진입 가드에 막힌다.
        if (this.view != null) this.view.ResetSession();
        gameObject.SetActive(false);

        OnClosed?.Invoke();
    }

    // 씬에 배치된 인스턴스를 비활성 포함으로 찾아 캐시한다. 씬이 바뀌면 참조가 죽으므로 자연히 재탐색된다.
    static PackOpenOverlay Resolve()
    {
        if (s_instance != null) return s_instance;

        s_instance = FindFirstObjectByType<PackOpenOverlay>(FindObjectsInactive.Include);

        return s_instance;
    }

    void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Debug.LogWarning("[PackOpenOverlay] 중복 인스턴스 — 나중 것을 버린다.");
            Destroy(gameObject);
            return;
        }

        s_instance = this;
        ResolveWiring();

        // 저작이 켜진 채로 남아 있어도 시작은 닫힌 상태다. Open이 켜는 중이면(IsOpen) 건드리지 않는다 —
        // Awake는 그 SetActive(true) 호출 안에서 돌기 때문에 여기서 끄면 열리다 만 화면이 된다.
        if (!IsOpen) gameObject.SetActive(false);
    }

    // 바깥이 Close를 거치지 않고 root를 껐을 때의 안전판. Close는 IsOpen을 먼저 내리므로 여기 걸리지 않는다.
    void OnDisable()
    {
        if (!IsOpen) return;

        // 씬이 내려가는 중이면 신호를 쏘지 않는다 — 구독자도 함께 파괴되는 중이다(뒷정리는 OnDestroy가 맡는다).
        if (!gameObject.scene.isLoaded) return;

        IsOpen = false;
        if (this.view != null) this.view.ResetSession();

        OnClosed?.Invoke();
    }

    void OnDestroy()
    {
        if (s_instance != this) return;

        s_instance = null;
        // 열린 채 씬이 바뀌면(첫실행 전투 진입) 플래그가 남아 다음 씬의 안내가 영영 억제된다.
        IsOpen = false;
    }

    bool Open()
    {
        if (IsOpen) return false;   // 이미 열려 있으면 진행 중인 세션을 덮지 않는다.
        if (!PackHandoff.HasPending) return false;

        // IsOpen을 켜는 것보다 먼저 세운다 — SetActive(true)가 그 안에서 Awake를 돌리고,
        // Awake는 "열리는 중이 아니면 닫는다"로 저작 실수를 자가교정하기 때문이다.
        // 연출 신호 순서로도 이쪽이 맞다: 구독자가 "안 열린 개봉"을 보지 않는다(튜토리얼이 이 플래그로 안내를 억제한다).
        IsOpen = true;

        // 켜는 것이 먼저다 — 비활성으로 시작한 오브젝트는 이 시점에 Awake가 돌아 배선이 성립하고,
        // 브레인·뷰의 OnEnable 구독이 붙기 전에 세션을 태우면 연출 신호가 유실된다.
        gameObject.SetActive(true);

        if (this.controller == null || this.view == null)
        {
            Debug.LogWarning("[PackOpenOverlay] controller/view 미배선 — 개봉 화면을 열 수 없다.");
            IsOpen = false;
            gameObject.SetActive(false);
            return false;
        }

        if (!this.controller.BeginSession())
        {
            IsOpen = false;
            gameObject.SetActive(false);
            return false;
        }

        OnOpened?.Invoke();
        return true;
    }

    // 배선을 비워둬도 동작하게 한다(프리팹 구조가 고정이라 자식 탐색으로 충분하다).
    void ResolveWiring()
    {
        if (this.controller == null) this.controller = GetComponentInChildren<PackAcquireController>(true);
        if (this.view == null) this.view = GetComponentInChildren<PackRevealView>(true);
    }
}
