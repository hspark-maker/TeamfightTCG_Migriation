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
//
// 이 화면이 답해야 하는 질문은 "이번에 뭘 건졌나" 하나다. 그 답은 배치나 팝이 아니라 카드의 상태 차이가 준다
//   — 신규는 테두리 림라이트가 계속 돌고 중복은 탈채도된 채 놓인다(PackCardView.ApplyResultContrast).
//   팝은 전 카드 동일하게 둔다: 정렬과 리듬까지 갈라지면 결과판이 또 한 번의 연출로 읽힌다.
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

    // 상세에서 좌우로 넘겨볼 목록 = 격자에 놓인 카드들, 놓인 순서 그대로. 상세 오버레이가 이것을 **참조**로 쥐므로
    // 인스턴스를 갈아치우지 않고 Clear + 재충전만 한다(CardDetailOverlayView.BindTile 주석과 같은 규약).
    readonly List<CardData> m_order = new List<CardData>();

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
        int t_detailOrder = DetailSortingOrder();   // 카드마다 캔버스를 거슬러 올라갈 이유가 없다

        for (int t_i = 0; t_i < _cards.Count; t_i++)
        {
            var t_drawn = _cards[t_i];
            if (t_drawn.Card == null) continue;

            var t_view = Instantiate(cardPrefab, t_parent);
            t_view.Bind(t_drawn);
            t_view.PlayRevealAccent(true);

            // 결과판의 본론. 위 한 줄이 "연출 없이 최종 상태로 세운다"라면 이 한 줄이 "그래서 뭘 건졌나"다 —
            // 신규는 림라이트가 계속 돌고 중복은 탈채도된 채 놓인다.
            t_view.ApplyResultContrast();

            int t_index = m_views.Count;
            m_views.Add(t_view);
            m_order.Add(t_drawn.Card);

            // 카드를 누르면 그 카드의 상세가 이 화면 위에 뜬다. 강화·진화는 걷은 채로 연다 —
            // 여기는 방금 뽑은 것을 확인하는 자리라, 그 자리에서 재화를 쓰기 시작하면 개봉 흐름이 갈라진다.
            CardDetailOverlayView.BindTile(t_view.Visual, m_order, t_index,
                                           _readOnly: true, _sortingOrder: t_detailOrder);

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

    // 격자를 비우는 곳이자 상세를 닫는 곳. 두 일을 여기 하나로 묶는 이유는 상세가 m_order를 **참조**로 쥐기
    // 때문이다 — 격자가 걷히는 길(오버레이 닫기·"한 번 더" 재개봉·다음 Show)은 전부 여기를 지난다.
    //
    // 상세는 이 화면과 다른 캔버스(로비)에 있어 결과 격자가 사라져도 저 혼자 떠 있는다. 그래서 닫는 일이 필요하다.
    // 닫아도 퇴장 트윈이 도는 동안(0.15s)은 살아 있어 목록이 빈 창을 잠깐 보게 되는데, 그 구간은 이미
    // 입력이 닫혀 있고 표시 갱신 경로가 모두 null 카드를 견디므로 그대로 둔다(순서로 막는 것이 아니다).
    //
    // ⚠ Close는 전역 싱글턴을 끈다 — "이 화면이 연 상세만" 닫는 것이 아니다.
    //   지금은 개봉 중에 다른 경로로 상세를 열 길이 없어 문제가 되지 않는다.
    void Clear()
    {
        CardDetailOverlayView.Close();

        for (int t_i = 0; t_i < m_views.Count; t_i++)
            if (m_views[t_i] != null) Destroy(m_views[t_i].gameObject);

        m_views.Clear();
        m_order.Clear();
    }

    // 상세가 이 화면 위에 뜨기 위한 순서. 상세 오버레이는 로비 캔버스 안에 있고 개봉 화면은 그보다 위에 뜨는
    // 별도 캔버스라, 그 값을 읽어 한 칸 위로 올린다 — 숫자를 여기 적어 두면 개봉 캔버스를 옮길 때 조용히 어긋난다.
    int DetailSortingOrder()
    {
        Canvas t_canvas = GetComponentInParent<Canvas>();
        if (t_canvas == null) return 0;   // 0 = 순서를 건드리지 않는다(상세가 제 캔버스의 제자리에 뜬다)

        // 중첩 캔버스는 스스로 순서를 덮어쓰지 않는 한 루트의 순서로 그려진다 — 그 실제 값을 봐야 한다.
        int t_order = t_canvas.overrideSorting ? t_canvas.sortingOrder : t_canvas.rootCanvas.sortingOrder;

        return t_order + 1;
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
