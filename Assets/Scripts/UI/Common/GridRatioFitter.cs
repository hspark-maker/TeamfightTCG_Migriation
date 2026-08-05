using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// GridLayoutGroup의 cellSize를 컨테이너 크기에서 매번 다시 계산한다(GridLayoutGroup과 같은 오브젝트에 부착).
//
// cellSize를 픽셀로 박아두면 캔버스 해상도·부모 비율이 바뀔 때 칸이 잘리거나 여백이 벌어진다.
// 그래서 이 컴포넌트가 고정하는 건 "열 수"와 "칸 종횡비" 둘뿐이고, 실제 픽셀은 컨테이너가 정한다.
// 단일진실원: 칸 크기는 여기서만 쓴다 — 인스펙터의 Cell Size 값은 이 컴포넌트가 덮어쓴다.
//
// artUsableRatio를 1보다 작게 주면 "그림 기준" 모드가 켜져 spacing과 좌우 padding까지 이 컴포넌트가 쓴다(아래 참고).
[ExecuteAlways]
[RequireComponent(typeof(GridLayoutGroup))]
[DisallowMultipleComponent]
public class GridRatioFitter : UIBehaviour
{
    [Tooltip("가로 칸 수. GridLayoutGroup의 Constraint도 이 값으로 강제된다.")]
    [SerializeField] int columns = 3;

    [Tooltip("칸의 가로/세로 비. 카드 원본 290x386 → 0.751")]
    [SerializeField] float cellAspect = 0.751f;

    [Tooltip("0보다 크면 세로도 이 행 수에 맞춰 채운다(스크롤 없는 고정 그리드). 0이면 폭만 기준으로 계산한다(스크롤 그리드).")]
    [SerializeField] int rowsToFit;

    // ── 그림 기준 모드 ──────────────────────────────────────────────────
    // 스프라이트에 투명 여백이 구워져 있으면 칸을 꽉 채워도 화면엔 그만큼 빈 자리가 남는다
    // (도감 카드 Frame.png: 1024x1361 캔버스에 실제 그림은 875x1319 → 좌우 각 7.2%, 상하 3.1%가 투명).
    // 그 비율을 알려주면 아래 두 값을 "칸"이 아니라 "보이는 그림" 기준으로 해석해 투명분을 스스로 상쇄한다.
    // 목적은 인스펙터에서 다루는 숫자를 눈에 보이는 그대로 만드는 것 — 음수 spacing을 손계산할 일이 없어진다.

    [Header("그림 기준 여백 (선택)")]
    [Tooltip("칸 안에서 그림이 실제로 차지하는 비율. (1,1)이면 이 모드는 꺼지고 아래 두 값도 무시된다(spacing·padding은 인스펙터 값 그대로).")]
    [SerializeField] Vector2 artUsableRatio = Vector2.one;

    [Tooltip("양옆 여백(px). 칸이 아니라 보이는 그림 기준.")]
    [SerializeField] float sideMargin;

    [Tooltip("카드 사이 간격(px). 칸이 아니라 보이는 그림 기준 — 음수면 그림끼리 겹친다.")]
    [SerializeField] Vector2 visibleGap;

    GridLayoutGroup m_grid;
    RectTransform   m_rect;

    // 마지막으로 계산에 쓴 컨테이너 크기. OnRectTransformDimensionsChange는 cellSize를 바꾼 결과로도
    // 다시 불리므로(ContentSizeFitter가 붙은 Content가 특히 그렇다) 같은 크기면 즉시 빠져나가 되먹임을 끊는다.
    Vector2 m_lastSize = new Vector2(float.NaN, float.NaN);

    GridLayoutGroup Grid => m_grid != null ? m_grid : (m_grid = GetComponent<GridLayoutGroup>());
    RectTransform   Rect => m_rect != null ? m_rect : (m_rect = (RectTransform)transform);

    // 비율이 (1,1)이면 기존 계약(칸 크기만 쓰고 spacing·padding은 읽기만)을 그대로 지킨다.
    bool UseArtMetrics => artUsableRatio.x > 0f && artUsableRatio.y > 0f
                       && (artUsableRatio.x < 1f || artUsableRatio.y < 1f);

    protected override void OnEnable()
    {
        base.OnEnable();
        Apply(true);
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        Apply(false);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        Apply(true);
    }
#endif

    void Apply(bool _force)
    {
        if (Grid == null || Rect == null) return;
        if (columns < 1 || cellAspect <= 0f) return;

        var t_size = Rect.rect.size;
        if (!_force && Mathf.Approximately(t_size.x, m_lastSize.x) && Mathf.Approximately(t_size.y, m_lastSize.y))
            return;

        m_lastSize = t_size;

        float t_w;

        if (UseArtMetrics)
        {
            // 보이는 그림 한 장의 폭을 먼저 구하고, 투명분을 되돌려 칸 폭으로 환산한다.
            // 여기서 padding·spacing을 읽지 않는 게 중요하다 — 그 둘은 이 모드에선 입력이 아니라 산출값이다.
            float t_visibleW = (t_size.x - sideMargin * 2f - visibleGap.x * (columns - 1)) / columns;
            if (t_visibleW <= 1f) return;   // 레이아웃이 아직 확정되지 않은 프레임 — 이번엔 건너뛴다

            t_w = t_visibleW / artUsableRatio.x;
        }
        else
        {
            var t_pad = Grid.padding;

            float t_availW = t_size.x - t_pad.horizontal - Grid.spacing.x * (columns - 1);
            if (t_availW <= 1f) return;   // 레이아웃이 아직 확정되지 않은 프레임 — 이번엔 건너뛴다

            t_w = t_availW / columns;
        }

        float t_h = t_w / cellAspect;

        // 행 수가 지정된 고정 그리드는 세로도 넘치면 안 된다 — 비를 유지한 채 세로 기준으로 다시 계산한다.
        if (rowsToFit > 0)
        {
            float t_gapY   = UseArtMetrics ? visibleGap.y : Grid.spacing.y;
            float t_availH = t_size.y - Grid.padding.vertical - t_gapY * (rowsToFit - 1);
            if (t_availH > 1f)
            {
                // 그림 기준 모드에선 이 여유도 "보이는 높이"라 칸 높이로 환산해야 비교가 성립한다.
                float t_maxH = t_availH / rowsToFit;
                if (UseArtMetrics) t_maxH /= artUsableRatio.y;

                if (t_h > t_maxH)
                {
                    t_h = t_maxH;
                    t_w = t_h * cellAspect;
                }
            }
        }

        Grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        Grid.constraintCount = columns;

        var t_cell = new Vector2(t_w, t_h);
        if ((Grid.cellSize - t_cell).sqrMagnitude >= 0.01f)
            Grid.cellSize = t_cell;

        if (UseArtMetrics) ApplyArtMetrics(t_cell);
    }

    // 그림 기준 모드의 산출물. 칸은 그림보다 투명분만큼 크므로, 그 차이를 spacing과 padding에서 되돌려
    // "보이는 그림"이 visibleGap 간격으로 서고 양끝이 sideMargin에 맞게 만든다.
    void ApplyArtMetrics(Vector2 _cell)
    {
        var t_visible = new Vector2(_cell.x * artUsableRatio.x, _cell.y * artUsableRatio.y);

        // 칸 피치 = 보이는 폭 + 원하는 간격 → spacing은 칸 크기와의 차이(투명분만큼 자연히 음수가 된다).
        var t_spacing = new Vector2(t_visible.x + visibleGap.x - _cell.x,
                                    t_visible.y + visibleGap.y - _cell.y);
        if ((Grid.spacing - t_spacing).sqrMagnitude >= 0.01f)
            Grid.spacing = t_spacing;

        // 양옆은 첫/마지막 칸의 투명분을 빼야 그림이 sideMargin 자리에 선다 — 음수 padding이 정상이다.
        // 위아래는 화면마다 사정이 달라 저작 값을 그대로 둔다.
        int t_side = Mathf.RoundToInt(sideMargin - (_cell.x - t_visible.x) * 0.5f);
        var t_pad  = Grid.padding;
        if (t_pad.left != t_side || t_pad.right != t_side)
            Grid.padding = new RectOffset(t_side, t_side, t_pad.top, t_pad.bottom);
    }
}
