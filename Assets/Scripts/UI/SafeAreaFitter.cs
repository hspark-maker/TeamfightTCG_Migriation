using UnityEngine;

/// <summary>
/// 노치·홈바·둥근 모서리를 피해 UI를 안전 영역 안으로 밀어 넣는다.
/// <b>Canvas 바로 아래에 이 컴포넌트를 단 빈 RectTransform을 두고, 실제 UI를 전부 그 자식으로</b> 넣는 구조다.
/// (개별 UI마다 붙이면 앵커 계산이 중첩돼 두 번 들어간다 — 삽입 지점은 캔버스당 하나여야 한다.)
///
/// 안전 영역이 화면 전체인 기기(노치 없음)에서는 앵커가 0~1이 되어 **아무 변화도 없다**.
/// 즉 이 컴포넌트를 넣는다고 기존 레이아웃이 바뀌지 않는다 — 잘리는 기기에서만 안쪽으로 들어온다.
///
/// 전체 화면을 덮어야 하는 연출(딤, 컷씬 영상, 배경)은 이 아래에 넣지 말 것.
/// 그건 노치까지 덮는 게 맞고, 안으로 밀면 가장자리에 빈 띠가 생긴다.
/// </summary>
/// <remarks>
/// <see cref="ExecuteAlways"/> — 플레이 중이 아니어도 계속 돈다. Game 뷰 해상도를 노치 프리셋으로 바꾸거나
/// Device Simulator를 켜면 <b>에디터에서 바로</b> 안전 영역이 반영된다(플레이 눌러야만 보이면 배치를 못 잡는다).
/// 대신 에디터에서 해상도를 바꿀 때마다 앵커가 갱신되어 씬이 dirty로 표시될 수 있다 — 값이 실제로
/// 바뀔 때만 쓰므로 저장하지 않으면 그만이다.
/// </remarks>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    [Tooltip("각 변에 안전 영역을 적용할지. 끄면 그 방향은 화면 끝까지 쓴다")]
    [SerializeField] bool applyLeft   = true;
    [SerializeField] bool applyRight  = true;
    [SerializeField] bool applyTop    = true;
    [SerializeField] bool applyBottom = true;

    RectTransform     rect;
    Rect              lastSafeArea = new Rect(0f, 0f, 0f, 0f);
    Vector2Int        lastScreen   = Vector2Int.zero;
    ScreenOrientation lastOrientation = ScreenOrientation.AutoRotation;
    bool              suppressedByAncestor;
    bool              warnedAboutAncestor;

    void Awake() => this.rect = (RectTransform)transform;

    void OnEnable()
    {
        if (this.rect == null) this.rect = (RectTransform)transform;
        RefreshSuppression();
        if (!this.suppressedByAncestor) Apply();
    }

    void OnTransformParentChanged()
    {
        if (this.rect == null) this.rect = (RectTransform)transform;
        RefreshSuppression();
        if (!this.suppressedByAncestor) Apply();
    }

    // 회전·해상도·안전영역은 언제든 바뀐다(분할화면, 폴더블 접기, 회전).
    // 이벤트가 없는 값이라 폴링이 유일한 방법인데, 비교 3개뿐이라 비용은 무시할 수준이다.
    // 조상 Fitter 검사(RefreshSuppression)는 여기서 하지 않는다 — 계층 탐색이라 값 비교보다 훨씬 비싸고,
    // 억제 여부가 바뀌는 계기는 켜질 때와 부모가 바뀔 때뿐이다(둘 다 아래 훅이 잡는다).
    void Update()
    {
        if (this.suppressedByAncestor) return;
        if (Screen.safeArea == this.lastSafeArea
            && Screen.width == this.lastScreen.x && Screen.height == this.lastScreen.y
            && Screen.orientation == this.lastOrientation)
            return;
        Apply();
    }

    void Apply()
    {
        if (this.rect == null) return;

        int t_w = Screen.width, t_h = Screen.height;
        if (t_w <= 0 || t_h <= 0) return;   // 초기화 전 프레임 방어(0으로 나누면 앵커가 NaN이 된다)

        Rect t_safe = Screen.safeArea;
        this.lastSafeArea    = t_safe;
        this.lastScreen      = new Vector2Int(t_w, t_h);
        this.lastOrientation = Screen.orientation;

        Vector2 t_min = new Vector2(t_safe.xMin / t_w, t_safe.yMin / t_h);
        Vector2 t_max = new Vector2(t_safe.xMax / t_w, t_safe.yMax / t_h);

        if (!this.applyLeft)   t_min.x = 0f;
        if (!this.applyBottom) t_min.y = 0f;
        if (!this.applyRight)  t_max.x = 1f;
        if (!this.applyTop)    t_max.y = 1f;

        // 비정상 값(에디터 시뮬레이터 전환 순간 등)에서 UI가 사라지지 않게 막는다.
        if (t_max.x <= t_min.x || t_max.y <= t_min.y) return;

        this.rect.anchorMin = t_min;
        this.rect.anchorMax = t_max;
        this.rect.offsetMin = Vector2.zero;
        this.rect.offsetMax = Vector2.zero;
    }

    void RefreshSuppression()
    {
        SafeAreaFitter t_ancestor = transform.parent != null
            ? transform.parent.GetComponentInParent<SafeAreaFitter>(true)
            : null;
        bool t_suppressed = t_ancestor != null;
        if (t_suppressed == this.suppressedByAncestor) return;

        this.suppressedByAncestor = t_suppressed;

        if (!this.suppressedByAncestor)
        {
            this.lastScreen = Vector2Int.zero;
            this.warnedAboutAncestor = false;
            return;
        }

        // 조상이 이미 안전영역을 적용하므로 이 래퍼는 전체 stretch로 남아야 두 번 줄지 않는다.
        this.rect.anchorMin = Vector2.zero;
        this.rect.anchorMax = Vector2.one;
        this.rect.offsetMin = Vector2.zero;
        this.rect.offsetMax = Vector2.zero;

        if (this.warnedAboutAncestor) return;
        this.warnedAboutAncestor = true;
        Debug.LogWarning($"[SafeAreaFitter] 조상 '{t_ancestor.name}'이 이미 SafeArea를 적용해 '{name}'은 적용을 생략합니다.", this);
    }

#if UNITY_EDITOR
    // 인스펙터에서 변 토글을 바꾸면 즉시 반영(플레이 중이 아니어도 확인 가능).
    //
    // **다음 에디터 틱으로 미룬다.** OnValidate 안에서 앵커를 바꾸면 RectTransform이 자식들에게
    // OnRectTransformDimensionsChange를 SendMessage로 돌리는데, 그 시점은 SendMessage가 금지돼 있어
    // "SendMessage cannot be called during Awake, CheckConsistency, or OnValidate" 경고가 자식 수만큼 쏟아진다.
    void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;               // 지연 중 삭제됐을 수 있다
            this.rect = (RectTransform)transform;
            this.lastScreen = Vector2Int.zero;      // 다음 Apply를 강제
            RefreshSuppression();
            if (!this.suppressedByAncestor) Apply();
        };
    }
#endif
}
