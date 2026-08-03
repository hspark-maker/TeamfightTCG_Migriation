using System;
using UnityEngine;

// 카드팩 개봉 화면의 수명 창구. 로비 위에 겹쳐 뜨는 개봉 오버레이를 여닫는 단일 지점이다.
//
// 왜 있나: 개봉이 별도 씬이던 시절엔 씬 로드/언로드가 네 가지 일을 암묵적으로 해줬다 —
//   튜토리얼 스텝 재적용 · 로비 획득 연출 재생 · 구매 잠금 해제 · 개봉 뷰 상태 초기화.
//   오버레이는 씬을 바꾸지 않으므로 아무도 해주지 않는다. 그래서 OnOpened/OnClosed 두 신호로 명시한다.
//   구독자가 씬 이름을 보지 않게 하는 것이 목적이다(PackRevealView.OnAnyPackOpened와 같은 방향).
//
// ⚠ 이 오브젝트 자신은 항상 활성이어야 한다(Awake로 Instance를 선점해야 TryOpen이 성립한다).
//   켜고 끄는 대상은 content 하나뿐이다.
[DisallowMultipleComponent]
public class PackOpenOverlay : MonoBehaviour
{
    public static PackOpenOverlay Instance { get; private set; }

    /// <summary>개봉 화면이 떠 있는가. 로비 쪽 안내·입력을 억제할 때 본다.</summary>
    public static bool IsOpen { get; private set; }

    // 세션을 뷰에 태운 직후. 이 시점엔 IsOpen이 이미 true다.
    public static event Action OnOpened;
    // 닫고 뷰를 되돌린 직후. 이 시점엔 IsOpen이 이미 false다(구독자가 "닫힌 뒤"를 전제로 움직인다).
    public static event Action OnClosed;

    [Tooltip("여닫을 화면 묶음(개봉 캔버스 + 브레인). 미배선이면 첫 자식. 이 오브젝트 자신을 넣지 말 것 — Awake가 죽는다.")]
    [SerializeField] GameObject content;
    [Tooltip("개봉 세션을 태울 브레인. 미배선이면 자식에서 찾는다.")]
    [SerializeField] PackAcquireController controller;
    [Tooltip("개봉 연출 뷰. 닫을 때 세션을 되돌린다. 미배선이면 자식에서 찾는다.")]
    [SerializeField] PackRevealView view;

    /// <summary>PackHandoff에 세션이 실려 있으면 개봉 화면을 연다. 열지 못하면 false(호출부가 판단).</summary>
    public static bool TryOpen()
    {
        if (Instance == null)
        {
            Debug.LogWarning("[PackOpenOverlay] 인스턴스 없음 — 개봉 화면을 열 수 없다(로비 씬 배치 확인).");
            return false;
        }

        return Instance.Open();
    }

    /// <summary>개봉 화면을 닫고 뷰 세션을 되돌린다(다음 개봉을 받을 수 있는 상태로).</summary>
    public void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;

        // 먼저 되돌린 뒤 끈다 — 반대로 하면 OnDisable이 요약 상태를 일부러 남겨(중복 발화 방지)
        // 다음 BeginOpen이 재진입 가드에 막힌다.
        if (this.view != null) this.view.ResetSession();
        if (this.content != null) this.content.SetActive(false);

        OnClosed?.Invoke();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[PackOpenOverlay] 중복 인스턴스 — 나중 것을 버린다.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Resolve();

        if (this.content != null) this.content.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance != this) return;

        Instance = null;
        // 열린 채 씬이 바뀌면(첫실행 전투 진입) 플래그가 남아 다음 씬의 안내가 영영 억제된다.
        IsOpen = false;
    }

    bool Open()
    {
        if (IsOpen) return false;   // 이미 열려 있으면 진행 중인 세션을 덮지 않는다.
        if (!PackHandoff.HasPending) return false;

        if (this.content == null || this.controller == null || this.view == null)
        {
            Debug.LogWarning("[PackOpenOverlay] content/controller/view 미배선 — 개봉 화면을 열 수 없다.");
            return false;
        }

        // 켜는 것이 먼저다 — 브레인·뷰의 OnEnable 구독이 붙기 전에 세션을 태우면 연출 신호가 유실된다.
        this.content.SetActive(true);

        // IsOpen도 BeginSession보다 앞서야 한다 — 연출이 그 안에서 곧장 개봉 신호를 쏘는 배선이면
        // 구독자가 "안 열린 개봉"을 보게 된다(튜토리얼이 이 플래그로 안내를 억제한다).
        IsOpen = true;

        if (!this.controller.BeginSession())
        {
            IsOpen = false;
            this.content.SetActive(false);
            return false;
        }

        OnOpened?.Invoke();
        return true;
    }

    // 배선을 비워둬도 동작하게 한다(프리팹 구조가 고정이라 자식 탐색으로 충분하다).
    void Resolve()
    {
        if (this.content == null && transform.childCount > 0) this.content = transform.GetChild(0).gameObject;
        if (this.controller == null) this.controller = GetComponentInChildren<PackAcquireController>(true);
        if (this.view == null) this.view = GetComponentInChildren<PackRevealView>(true);
    }
}
