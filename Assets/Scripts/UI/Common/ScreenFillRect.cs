using UnityEngine;

/// <summary>
/// 이 칸을 <b>안전영역과 무관하게 화면 전체</b> 크기로 매 해상도마다 다시 맞춘다.
///
/// <see cref="SafeAreaFitter"/> 밑에 있는 화면은 노치·홈바만큼 안으로 밀려 가장자리에 빈 띠가 생긴다.
/// 그 띠까지 자기 그림으로 채워야 하는 <b>전체 화면 콘텐츠</b>(스크롤 맵 같은 것)를 화면 기준으로 되돌린다.
///
/// 그림 한 장을 덮는 것이라면 <see cref="ScreenCoverBackground"/>를 써라 — 그쪽은 원본 비율을 지킨다.
/// 이건 비율을 버리고 칸을 화면에 맞춰 늘리므로, 안쪽 내용이 잘려도 되는 것에만 쓴다.
/// </summary>
/// <remarks>
/// 앵커·피벗·크기·위치를 이 컴포넌트가 쥔다 — 인스펙터에서 저작해도 덮어쓴다.
///
/// <see cref="ExecuteAlways"/> — Game 뷰 해상도를 노치 프리셋으로 바꾸면 에디터에서 바로 반영된다.
/// </remarks>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class ScreenFillRect : MonoBehaviour
{
    [Tooltip("화면 중앙 기준으로 칸을 미는 양(UI 단위). 한쪽만 더 내보내야 할 때만 쓴다")]
    [SerializeField] Vector2 offset = Vector2.zero;

    [Tooltip("채울 최소 배율. 1이면 화면에 딱 맞는다. 1.02쯤 주면 기기 오차에도 가장자리가 안 뜬다")]
    [Min(1f)] [SerializeField] float overscan = 1f;

    RectTransform rect;
    RectTransform canvasRect;

    Vector2 lastCanvasSize = Vector2.zero;
    Vector2 lastParentSize = Vector2.zero;

    void OnEnable()
    {
        Cache();
        this.lastCanvasSize = Vector2.zero;   // 다시 켜질 때 한 번은 반드시 계산
        Apply();
    }

    // 캔버스 크기(해상도)와 부모 크기(안전영역)는 둘 다 언제든 바뀐다(회전·폴더블·시뮬레이터).
    // 이벤트가 없어 폴링이 유일한 방법인데 비교 2개라 비용은 없다.
    void Update()
    {
        if (this.canvasRect == null || this.rect == null) Cache();
        if (this.canvasRect == null || this.rect == null) return;

        Vector2 t_canvas = this.canvasRect.rect.size;
        Vector2 t_parent = ParentSize();
        if (t_canvas == this.lastCanvasSize && t_parent == this.lastParentSize) return;

        Apply();
    }

    void Cache()
    {
        this.rect = (RectTransform)transform;

        Canvas t_canvas = GetComponentInParent<Canvas>();
        if (t_canvas != null) t_canvas = t_canvas.rootCanvas;
        this.canvasRect = t_canvas != null ? (RectTransform)t_canvas.transform : null;
    }

    Vector2 ParentSize()
    {
        var t_parent = this.rect.parent as RectTransform;

        return t_parent != null ? t_parent.rect.size : Vector2.zero;
    }

    void Apply()
    {
        if (this.rect == null || this.canvasRect == null) return;

        Vector2 t_canvas = this.canvasRect.rect.size;
        if (t_canvas.x <= 0f || t_canvas.y <= 0f) return;

        this.lastCanvasSize = t_canvas;
        this.lastParentSize = ParentSize();

        // 크기를 sizeDelta로 직접 쥐려면 앵커가 stretch가 아니라 한 점이어야 한다.
        this.rect.anchorMin = this.rect.anchorMax = new Vector2(0.5f, 0.5f);
        this.rect.pivot     = new Vector2(0.5f, 0.5f);
        this.rect.sizeDelta = t_canvas * this.overscan;

        // 부모(안전영역)가 아니라 **화면** 중앙에 맞춘다 — 부모 기준으로 두면 노치만큼 아래로 밀린다.
        Vector3 t_center = this.canvasRect.TransformPoint(this.canvasRect.rect.center);
        Vector3 t_shift  = this.canvasRect.TransformVector(this.offset);
        this.rect.position = new Vector3(t_center.x + t_shift.x, t_center.y + t_shift.y, this.rect.position.z);
    }

#if UNITY_EDITOR
    // 인스펙터 값을 바꾸면 즉시 반영. 다음 틱으로 미루는 이유는 SafeAreaFitter와 같다 —
    // OnValidate 안에서 rect를 건드리면 자식에게 SendMessage가 돌아 경고가 쏟아진다.
    void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            Cache();
            this.lastCanvasSize = Vector2.zero;
            Apply();
        };
    }
#endif
}
