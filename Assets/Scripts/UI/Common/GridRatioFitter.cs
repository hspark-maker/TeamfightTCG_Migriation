using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// GridLayoutGroup의 cellSize를 컨테이너 크기에서 매번 다시 계산한다(GridLayoutGroup과 같은 오브젝트에 부착).
//
// cellSize를 픽셀로 박아두면 캔버스 해상도·부모 비율이 바뀔 때 칸이 잘리거나 여백이 벌어진다.
// 그래서 이 컴포넌트가 고정하는 건 "열 수"와 "칸 종횡비" 둘뿐이고, 실제 픽셀은 컨테이너가 정한다.
// 단일진실원: 칸 크기는 여기서만 쓴다 — 인스펙터의 Cell Size 값은 이 컴포넌트가 덮어쓴다.
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

    GridLayoutGroup m_grid;
    RectTransform   m_rect;

    // 마지막으로 계산에 쓴 컨테이너 크기. OnRectTransformDimensionsChange는 cellSize를 바꾼 결과로도
    // 다시 불리므로(ContentSizeFitter가 붙은 Content가 특히 그렇다) 같은 크기면 즉시 빠져나가 되먹임을 끊는다.
    Vector2 m_lastSize = new Vector2(float.NaN, float.NaN);

    GridLayoutGroup Grid => m_grid != null ? m_grid : (m_grid = GetComponent<GridLayoutGroup>());
    RectTransform   Rect => m_rect != null ? m_rect : (m_rect = (RectTransform)transform);

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

        var t_pad = Grid.padding;

        float t_availW = t_size.x - t_pad.horizontal - Grid.spacing.x * (columns - 1);
        if (t_availW <= 1f) return;   // 레이아웃이 아직 확정되지 않은 프레임 — 이번엔 건너뛴다

        float t_w = t_availW / columns;
        float t_h = t_w / cellAspect;

        // 행 수가 지정된 고정 그리드는 세로도 넘치면 안 된다 — 비를 유지한 채 세로 기준으로 다시 계산한다.
        if (rowsToFit > 0)
        {
            float t_availH = t_size.y - t_pad.vertical - Grid.spacing.y * (rowsToFit - 1);
            if (t_availH > 1f && t_h * rowsToFit > t_availH)
            {
                t_h = t_availH / rowsToFit;
                t_w = t_h * cellAspect;
            }
        }

        Grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        Grid.constraintCount = columns;

        var t_cell = new Vector2(t_w, t_h);
        if ((Grid.cellSize - t_cell).sqrMagnitude < 0.01f) return;

        Grid.cellSize = t_cell;
    }
}
