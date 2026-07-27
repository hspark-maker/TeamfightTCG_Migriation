using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

// 개봉 결과 격자. 더미를 전부 밀어낸 뒤, 이번에 뽑힌 카드 전체를 3열로 다시 늘어놓아 한눈에 보여준다.
// 더미의 카드는 밀려나며 사라지고 여기서 결과용 사본을 새로 만든다 —
//   밀어내기(PackCardStack의 좌표 직접 조작)와 결과 배치의 수명·좌표계를 갈라 서로를 신경 쓰지 않게 한다.
//
// 배치는 레이아웃 그룹이 아니라 이 코드가 직접 잡는다. 카드 프리팹은 광채·NEW 배지가 고정 크기라
//   셀에 맞춰 rect만 줄이면 배지가 카드를 덮어버린다 — 카드는 통째로 스케일을 낮춰야 비율이 산다.
// 표시 전용이다. 획득·소유·환급은 이 화면 밖에서 이미 확정돼 있고 여기서는 결과만 읽는다.
public class PackResultGrid : MonoBehaviour
{
    // 3열은 이 화면의 고정 규격(열 수는 배선의 자유가 아니다).
    public const int COLUMN_COUNT = 3;

    [SerializeField] CanvasGroup panel;      // 페이드 대상. 미배선이면 이 오브젝트에서 확보한다.
    [Tooltip("카드가 놓이는 좌표계. 격자는 이 사각형의 중심을 기준으로 정렬된다. 미배선이면 이 오브젝트.")]
    [SerializeField] RectTransform content;
    [SerializeField] PackCardView cardPrefab;
    [SerializeField] float fadeDuration = 0.35f;

    [Header("격자 칸")]
    [Tooltip("카드 한 장이 차지하는 칸 크기. 카드는 이 안에 들어가도록 비율을 지킨 채 축소된다.")]
    [SerializeField] float cellWidth = 420f;
    [SerializeField] float cellHeight = 590f;
    [SerializeField] float spacingX = 24f;
    [SerializeField] float spacingY = 24f;

    [Header("카드 등장")]
    [Tooltip("카드가 순서대로 튀어오르는 간격. 0이면 전부 동시에.")]
    [SerializeField] float cardStagger = 0.05f;
    [SerializeField] float cardPopDuration = 0.25f;
    [Tooltip("튀어오르기 시작하는 크기(격자 크기 대비 비율).")]
    [SerializeField] float cardPopFromScale = 0.7f;

    readonly List<PackCardView> m_views = new List<PackCardView>();

    CanvasGroup m_panel;

    /// <summary>결과 카드를 3열로 세우고 패널을 띄운다. _instant면 페이드도 팝도 없이 곧장 최종 상태(스킵 경로).</summary>
    public void Show(IReadOnlyList<DrawnCard> _cards, bool _instant = false)
    {
        Clear();
        Build(_cards, _instant);

        // 패널의 표시 여부(SetActive)는 진행자가 쥔다 — 여기서는 페이드만 맡아 소유권이 갈리지 않게 한다.
        var t_panel = ResolvePanel();
        t_panel.DOKill();

        if (_instant)
        {
            t_panel.alpha = 1f;
            return;
        }

        t_panel.alpha = 0f;
        t_panel.DOFade(1f, fadeDuration).SetLink(gameObject);
    }

    /// <summary>패널을 감추고 카드를 걷는다(다음 개봉 세션 대비).</summary>
    public void Hide()
    {
        var t_panel = ResolvePanel();
        t_panel.DOKill();
        t_panel.alpha = 0f;

        Clear();
    }

    // ── 내부 ────────────────────────────────────────────────────

    // 카드를 만들어 격자 자리에 앉힌다. 신규/중복 강조는 즉시 모드로 — 결과판이지 다시 여는 연출이 아니다.
    void Build(IReadOnlyList<DrawnCard> _cards, bool _instant)
    {
        if (cardPrefab == null)
        {
            Debug.LogWarning("[PackResultGrid] cardPrefab 미배선 → 결과 격자 생성 불가.");
            return;
        }

        var t_parent = ResolveContent();
        int t_count = CountPlaceable(_cards);
        if (t_count == 0) return;

        float t_scale = CardScale();
        int t_rows = Mathf.CeilToInt(t_count / (float)COLUMN_COUNT);

        for (int t_i = 0; t_i < _cards.Count; t_i++)
        {
            var t_drawn = _cards[t_i];
            if (t_drawn.Card == null) continue;

            var t_view = Instantiate(cardPrefab, t_parent);
            t_view.Bind(t_drawn);
            t_view.PlayRevealAccent(true);

            int t_index = m_views.Count;
            m_views.Add(t_view);

            var t_rt = (RectTransform)t_view.transform;
            t_rt.anchoredPosition = SlotPosition(t_index, t_count, t_rows);
            t_rt.localRotation = Quaternion.identity;

            PlayPop(t_rt, t_index, t_scale, _instant);
        }
    }

    // i번째 칸의 자리. 행·열 모두 중앙 정렬이고, 마지막 행이 덜 찼으면 그 행만 따로 가운데로 모은다.
    Vector2 SlotPosition(int _index, int _count, int _rows)
    {
        int t_row = _index / COLUMN_COUNT;
        int t_col = _index % COLUMN_COUNT;

        // 이 행에 실제로 놓이는 장수(마지막 행은 모자랄 수 있다).
        int t_inRow = Mathf.Min(COLUMN_COUNT, _count - t_row * COLUMN_COUNT);

        float t_x = (t_col - (t_inRow - 1) * 0.5f) * (cellWidth + spacingX);
        float t_y = -(t_row - (_rows - 1) * 0.5f) * (cellHeight + spacingY);

        return new Vector2(t_x, t_y);
    }

    // 카드를 칸 안에 넣는 배율. 가로·세로 중 빡빡한 쪽에 맞춰야 넘치지 않는다.
    float CardScale()
    {
        var t_size = ((RectTransform)cardPrefab.transform).rect.size;
        if (t_size.x <= 0f || t_size.y <= 0f) return 1f;

        return Mathf.Min(cellWidth / t_size.x, cellHeight / t_size.y);
    }

    // 카드가 순서대로 톡톡 튀어오른다. 최종 크기는 격자 배율이라 팝은 그 배율을 기준으로 논다.
    void PlayPop(Transform _tr, int _order, float _scale, bool _instant)
    {
        _tr.DOKill();

        if (_instant || cardPopDuration <= 0f)
        {
            _tr.localScale = Vector3.one * _scale;
            return;
        }

        _tr.localScale = Vector3.one * (_scale * cardPopFromScale);
        _tr.DOScale(_scale, cardPopDuration)
           .SetDelay(_order * cardStagger)
           .SetEase(Ease.OutBack)
           .SetLink(_tr.gameObject);
    }

    static int CountPlaceable(IReadOnlyList<DrawnCard> _cards)
    {
        if (_cards == null) return 0;

        int t_count = 0;
        for (int t_i = 0; t_i < _cards.Count; t_i++)
            if (_cards[t_i].Card != null) t_count++;

        return t_count;
    }

    void Clear()
    {
        for (int t_i = 0; t_i < m_views.Count; t_i++)
            if (m_views[t_i] != null) Destroy(m_views[t_i].gameObject);

        m_views.Clear();
    }

    RectTransform ResolveContent()
        => content != null ? content : (RectTransform)transform;

    CanvasGroup ResolvePanel()
    {
        if (m_panel != null) return m_panel;

        m_panel = panel != null ? panel : GetComponent<CanvasGroup>();
        if (m_panel == null) m_panel = gameObject.AddComponent<CanvasGroup>();

        return m_panel;
    }
}
