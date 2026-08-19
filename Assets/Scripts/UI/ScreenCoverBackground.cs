using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 배경 이미지를 <b>안전영역과 무관하게 화면 전체</b>를 덮도록 매 해상도마다 다시 재운다.
/// 원본 비율은 유지하고 남는 쪽은 화면 밖으로 잘린다(늘어나지 않는다).
/// 그림이 아니라 칸 자체를 꽉 채워야 하면 <see cref="stretchToScreen"/>으로 비율을 버린다.
///
/// 배경은 노치·홈바까지 덮는 게 맞다 — <see cref="SafeAreaFitter"/> 밑에 그냥 두면 안전영역만큼
/// 안으로 밀려 가장자리에 빈 띠가 생긴다. 이 컴포넌트는 SafeArea 안에 있는 배경을 화면 기준으로 되돌린다.
/// (안전영역을 지켜야 하는 <b>UI</b>에는 절대 붙이지 말 것. 오직 배경·딤처럼 끝까지 덮을 것에만.)
///
/// 고정 크기로 저작된 배경은 기기마다 어긋난다 — 안전영역이 짧은 기기에선 삐져나오고
/// 긴 기기에선 모자라 가장자리가 빈다. 그래서 크기를 저작값이 아니라 <b>매번 계산</b>한다.
/// </summary>
/// <remarks>
/// 앵커·피벗·크기·위치를 이 컴포넌트가 쥔다. 인스펙터에서 저작해도 덮어쓰므로,
/// 그림이 잡히는 위치를 바꾸고 싶으면 <see cref="offset"/>으로 민다.
///
/// <see cref="ExecuteAlways"/> — Game 뷰 해상도를 노치 프리셋으로 바꾸면 에디터에서 바로 반영된다.
/// </remarks>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class ScreenCoverBackground : MonoBehaviour
{
    [Tooltip("화면 중앙 기준으로 그림을 미는 양(UI 단위). 잘려 나가는 쪽을 조절할 때만 쓴다.\n" +
             "예: 천장을 더 보여주고 싶으면 y를 음수로")]
    [SerializeField] Vector2 offset = Vector2.zero;

    [Tooltip("덮을 최소 배율. 1이면 화면에 딱 맞게 덮는다. 1.05쯤 주면 기기 오차에도 가장자리가 안 뜬다")]
    [Min(1f)] [SerializeField] float overscan = 1f;

    [Tooltip("비율을 버리고 화면 크기 그대로 늘린다. 그림이 아니라 스크롤 뷰·딤처럼 " +
             "**칸 자체**가 화면을 꽉 채워야 할 때 켠다.\n" +
             "끄면(기본) 스프라이트 원본 비율을 지키고 남는 쪽이 잘린다.")]
    [SerializeField] bool stretchToScreen = false;

    RectTransform rect;
    RectTransform canvasRect;
    Image         image;

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
        this.rect  = (RectTransform)transform;
        this.image = GetComponent<Image>();

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

        Vector2 t_size;
        if (this.stretchToScreen)
        {
            t_size = t_canvas * this.overscan;
        }
        else
        {
            Vector2 t_source = SourceSize();
            if (t_source.x <= 0f || t_source.y <= 0f) return;

            // 가로·세로 중 더 많이 키워야 하는 쪽에 맞춘다 = 화면을 반드시 덮고 남는 쪽만 잘린다.
            t_size = t_source * (Mathf.Max(t_canvas.x / t_source.x, t_canvas.y / t_source.y) * this.overscan);
        }

        this.lastCanvasSize = t_canvas;
        this.lastParentSize = ParentSize();

        // 크기를 sizeDelta로 직접 쥐려면 앵커가 stretch가 아니라 한 점이어야 한다.
        this.rect.anchorMin = this.rect.anchorMax = new Vector2(0.5f, 0.5f);
        this.rect.pivot     = new Vector2(0.5f, 0.5f);
        this.rect.sizeDelta = t_size;

        // 부모(안전영역)가 아니라 **화면** 중앙에 맞춘다 — 부모 기준으로 두면 노치만큼 아래로 밀린다.
        Vector3 t_center = this.canvasRect.TransformPoint(this.canvasRect.rect.center);
        Vector3 t_shift  = this.canvasRect.TransformVector(this.offset);
        this.rect.position = new Vector3(t_center.x + t_shift.x, t_center.y + t_shift.y, this.rect.position.z);
    }

    /// <summary>덮을 원본 비율. 스프라이트 원본 픽셀 크기를 쓴다(저작된 rect 크기는 이미 어긋나 있을 수 있다).</summary>
    Vector2 SourceSize()
    {
        if (this.image != null && this.image.sprite != null) return this.image.sprite.rect.size;

        return this.rect.rect.size;   // 스프라이트가 없으면 현재 비율을 유지한다
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
