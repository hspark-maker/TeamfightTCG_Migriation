using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 팩 진열대의 좌우 넘김. 페이지를 한 줄로 늘어놓은 Track 하나만 굴린다 —
// "지금 몇 번째"라는 상태를 좌표 하나(Track.x)로 환원해 드래그·화살표·스냅이 같은 값을 두고 싸우지 않게 한 분리다.
//
// 한 장짜리 뷰의 스프라이트를 바꿔치기하는 방식은 쓰지 않는다. 드래그 중 "들어오는 페이지"가 없어 화면이 비고,
// 되감기와 스냅에서 그림이 갈린다. 트랙 방식은 손가락을 따라 현재 팩이 나가고 다음 팩이 들어오는 그림이
// 좌표 하나로 자동 성립한다.
//
// 인접 팩이 살짝 보이는 코버플로우가 아니다 — pageStep이 뷰포트 폭과 같고 Viewport에 RectMask2D가 걸려 있어
// 정지 상태에선 이웃이 마스크 밖으로 완전히 잘린다. 이웃이 드러나는 건 손가락이 닿아 있는 동안뿐이다.
//
// 순환(loop)은 인덱스만 감싸는 게 아니라 페이지를 재배치해서 만든다. 인덱스만 감싸면 마지막→첫으로 갈 때
// 트랙이 끝에서 처음까지 되감기는 게 그대로 보인다. 대신 논리 인덱스는 무한히 흐르게 두고(±로 계속 증가),
// 각 페이지를 "현재 페이지에서 최단 거리"인 자리에 놓는다 — 양옆에는 항상 올바른 이웃이 있고,
// 자리를 옮기는 건 반대편 페이지 하나뿐이며 그건 언제나 마스크 밖이다.
// 페이지가 3장 미만이면 한 페이지가 좌우에 동시에 있어야 해서 성립하지 않는다 → 그때는 끝단 저항 방식으로 되돌아간다.
//
// 이 컴포넌트는 팩을 모른다 — 그림 N장과 "지금 몇 번째"만 안다.
// 무엇을 팔지·얼마인지·튜토리얼이 무엇을 강제하는지는 전부 PackShowcaseController가 쥔다.
// 그래야 표시와 결제가 갈릴 여지가 구조적으로 없다.
//
// ⚠ 배선 전제: 이 컴포넌트는 raycastTarget인 Graphic(투명 Image)이 붙은 Viewport에 있어야 하고,
//   track은 그 자식이어야 한다(마스크 안에서 잘리도록).
public class PackCarouselView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    /// <summary>중앙 페이지가 실제로 바뀌었다(스냅 확정 시점 — 연출 종료가 아니다).
    /// 표시·구매 잠금이 슬라이드보다 늦게 따라오면 "보이는 것과 살 것"이 어긋난다.</summary>
    public event Action<int> OnIndexChanged;

    [Header("배치")]
    [Tooltip("페이지가 잘리는 창. 미배선이면 자기 RectTransform.")]
    [SerializeField] RectTransform viewport;
    [Tooltip("x만 움직이는 유일한 노드. Viewport의 자식이어야 마스크가 먹는다.")]
    [SerializeField] RectTransform track;
    [Tooltip("비활성 원본 페이지. 자식 Art(Image)에 팩 그림이 들어간다.")]
    [SerializeField] RectTransform pageTemplate;
    [SerializeField] string artChildName = "Art";

    [Header("연동")]
    [SerializeField] Button prevButton;
    [SerializeField] Button nextButton;
    [Tooltip("페이지 인디케이터. 미배선이면 점 없음.")]
    [SerializeField] PackCarouselDotsView dots;

    [Header("스와이프")]
    [Tooltip("넘기는 데 필요한 가로 이동량(페이지 폭 대비). 해상도와 무관하게 같은 손맛.")]
    [Range(0.08f, 0.5f)] [SerializeField] float snapRatio = 0.22f;
    [Tooltip("이 속도(캔버스단위/초) 이상이면 거리가 부족해도 넘어간다. 0이면 속도 판정 없음.")]
    [SerializeField] float flickSpeed = 700f;
    [Tooltip("속도로 넘길 때 최소한 이만큼(페이지 폭 대비)은 밀어야 한다.")]
    [Range(0f, 0.3f)] [SerializeField] float flickMinRatio = 0.03f;
    [Tooltip("끝과 끝을 잇는다. 페이지가 3장 미만이면 성립하지 않아 자동으로 꺼진다(끝단 저항으로 폴백).")]
    [SerializeField] bool loop = true;
    [Tooltip("양 끝에서 더 끌 때의 저항. 0.35면 민 거리의 35%만 따라온다. 순환 중에는 끝이 없으므로 쓰이지 않는다.")]
    [Range(0f, 1f)] [SerializeField] float edgeResistance = 0.35f;
    [SerializeField] float snapDuration = 0.28f;

    [Header("레이아웃")]
    [Tooltip("페이지 간격. 0이면 뷰포트 폭을 쓴다(권장 — 이웃이 마스크에 정확히 잘린다).")]
    [SerializeField] float pageStepOverride;

    readonly List<RectTransform> m_pages = new List<RectTransform>();
    readonly List<Image> m_artImages = new List<Image>();
    readonly List<Color> m_artBaseColors = new List<Color>();

    // 페이지별 아이들 모션. 중앙 한 장만 켜 둔다 — 화면 밖 N-1개가 매 프레임 sin을 도는 낭비를 없애고,
    // 꺼지는 순간 PackIdleMotion.OnDisable이 오프셋을 원복하므로 유령 좌표도 남지 않는다.
    readonly List<PackIdleMotion> m_motions = new List<PackIdleMotion>();

    Canvas m_canvas;
    bool m_initialized;
    bool m_interactable = true;
    bool m_dragging;

    // 논리 인덱스. 순환 중에는 0..N-1에 갇히지 않고 계속 흐른다(트랙 좌표와 1:1로 맞물려야 되감기가 안 생긴다).
    // 바깥에 노출하는 Index는 항상 PageOf로 접은 0..N-1이다.
    int m_index;
    int m_baseIndex;      // 임계 판정 기준은 "드래그 시작 시점의 페이지"다.
    float m_pageStep = 1f;

    // 저항을 먹이지 않은 누적 좌표. 이걸 따로 두지 않고 track.x에 저항을 반복 적용하면 배율이 복리로 붙는다.
    float m_rawX;
    float m_dragSpeed;

    public int Index => PageOf(m_index);
    public int PageCount => m_pages.Count;

    /// <summary>중앙에 놓인 페이지의 노드. 구매 임팩트가 연출 대상으로 쓴다(페이지가 없으면 null).</summary>
    public RectTransform CurrentPage
    {
        get
        {
            int t_page = PageOf(m_index);
            return t_page >= 0 && t_page < m_pages.Count ? m_pages[t_page] : null;
        }
    }

    // 한 페이지가 좌우에 동시에 놓여야 하는 2장 이하에서는 순환이 성립하지 않는다.
    bool CanLoop => loop && m_pages.Count >= 3;

    void Awake() => EnsureInit();

    void OnEnable()
    {
        EnsureInit();

        if (prevButton != null)
        {
            prevButton.onClick.RemoveListener(OnPrevPressed);
            prevButton.onClick.AddListener(OnPrevPressed);
        }
        if (nextButton != null)
        {
            nextButton.onClick.RemoveListener(OnNextPressed);
            nextButton.onClick.AddListener(OnNextPressed);
        }
    }

    void OnDisable()
    {
        if (prevButton != null) prevButton.onClick.RemoveListener(OnPrevPressed);
        if (nextButton != null) nextButton.onClick.RemoveListener(OnNextPressed);

        // 탭 전환 중 드래그 상태로 굳으면 재진입 시 유령 오프셋이 남는다.
        if (track != null) track.DOKill();
        m_dragging = false;
        GoTo(m_index, false);
    }

    // 뷰포트 크기가 바뀌면 페이지 간격도 바뀐다 — 재해석하지 않으면 정렬이 어긋난 채 굳는다.
    void OnRectTransformDimensionsChange()
    {
        if (!m_initialized) return;

        float t_step = ResolvePageStep();
        if (Mathf.Approximately(t_step, m_pageStep)) return;

        m_pageStep = t_step;
        GoTo(m_index, false);   // GoTo가 재배치까지 한다.
    }

    /// <summary>그림 N장으로 페이지를 다시 세운다. 현재 인덱스는 범위 안이면 유지된다
    /// (튜토리얼 스텝 전환으로 목록이 갈릴 때 보던 페이지를 잃지 않게).</summary>
    public void Build(IReadOnlyList<Sprite> _arts)
    {
        EnsureInit();
        ClearPages();

        if (track == null || pageTemplate == null) return;

        int t_count = _arts != null ? _arts.Count : 0;
        for (int t_i = 0; t_i < t_count; t_i++)
        {
            var t_page = Instantiate(pageTemplate, track);
            t_page.gameObject.SetActive(true);
            t_page.name = $"PackPage_{t_i}";

            // 그림이 없는 팩(packArt 미지정)은 템플릿 기본 이미지를 그대로 둔다 — 빈 사각형보다 낫다.
            var t_art = _arts[t_i];
            var t_child = t_page.Find(artChildName);
            var t_image = t_child != null ? t_child.GetComponent<Image>() : null;
            if (t_art != null)
            {
                if (t_image != null) t_image.sprite = t_art;
                else Debug.LogWarning($"[PackCarouselView] pageTemplate에 '{artChildName}'(Image) 자식이 없다 — 팩 그림이 반영되지 않는다.", this);
            }

            m_pages.Add(t_page);
            m_artImages.Add(t_image);
            m_artBaseColors.Add(t_image != null ? t_image.color : Color.white);
            m_motions.Add(t_page.GetComponent<PackIdleMotion>());
        }

        m_pageStep = ResolvePageStep();

        // 재구축은 논리 인덱스를 접어 다시 시작한다 — 순환으로 흘러간 값이 새 목록의 범위를 넘어 남지 않게.
        m_index = m_pages.Count > 0 ? Mathf.Clamp(PageOf(m_index), 0, m_pages.Count - 1) : 0;

        if (dots != null) dots.Rebuild(m_pages.Count);

        GoTo(m_index, false);
    }

    public void SetPageLocked(int _index, bool _locked)
    {
        if (_index < 0 || _index >= m_artImages.Count) return;
        Image t_art = m_artImages[_index];
        if (t_art == null) return;

        t_art.color = _locked
            ? new Color(0.28f, 0.28f, 0.28f, 0.82f)
            : m_artBaseColors[_index];
    }

    /// <summary>바깥에서 페이지를 지정한다(튜토리얼 고정·복원). 범위 밖은 클램프(순환 중엔 최단 방향으로 간다).</summary>
    public void SetIndex(int _index, bool _animated)
    {
        EnsureInit();

        // 순환 중엔 논리 인덱스가 흘러 있으므로 0..N-1을 그대로 넣으면 트랙이 되감긴다 — 최단 거리로 환산한다.
        if (CanLoop) GoTo(m_index + ShortestDelta(_index - PageOf(m_index), m_pages.Count), _animated);
        else GoTo(_index, _animated);
    }

    /// <summary>입력을 켜고 끈다. 끄면 드래그·화살표가 모두 죽고 현재 페이지로 즉시 정렬된다
    /// (잠금이 드래그 도중에 들어와도 트랙이 어중간한 자리에 남지 않게).</summary>
    public void SetInteractable(bool _on)
    {
        EnsureInit();
        m_interactable = _on;

        if (!_on)
        {
            m_dragging = false;
            GoTo(m_index, false);
        }
        RefreshArrows();
    }

    public void OnBeginDrag(PointerEventData _e)
    {
        if (!m_interactable || m_pages.Count <= 1 || track == null) return;
        EnsureInit();

        // 스냅 중 재입력은 무시가 아니라 현재 자리에서 인계받는다 — 무시하면 손가락과 그림이 따로 논다.
        track.DOKill();

        m_dragging = true;
        m_baseIndex = m_index;
        m_rawX = track.anchoredPosition.x;
        m_dragSpeed = 0f;
    }

    public void OnDrag(PointerEventData _e)
    {
        if (!m_dragging || track == null) return;

        // 캔버스 스케일을 나눠야 화면 이동량과 트랙 이동량이 일치한다.
        float t_scale = m_canvas != null ? m_canvas.scaleFactor : 1f;
        if (t_scale <= 0f) t_scale = 1f;

        float t_move = _e.delta.x / t_scale;
        m_rawX += t_move;
        track.anchoredPosition = new Vector2(ApplyEdgeResistance(m_rawX), track.anchoredPosition.y);

        // 속도는 거리와 같은 좌표계에서 재야 두 임계를 나란히 비교할 수 있다.
        float t_dt = Time.unscaledDeltaTime;
        if (t_dt > 0f) m_dragSpeed = t_move / t_dt;
    }

    public void OnEndDrag(PointerEventData _e)
    {
        if (!m_dragging) return;
        m_dragging = false;

        float t_delta = m_rawX - HomeXOf(m_baseIndex);   // 오른쪽으로 끌면 +
        bool t_flick = flickSpeed > 0f
                       && Mathf.Abs(m_dragSpeed) >= flickSpeed
                       && Mathf.Abs(t_delta) >= m_pageStep * flickMinRatio;

        int t_dir = 0;
        if (Mathf.Abs(t_delta) >= m_pageStep * snapRatio || t_flick)
            t_dir = t_delta > 0f ? -1 : 1;   // 오른쪽으로 끌면 이전 페이지가 들어온다.

        GoTo(m_baseIndex + t_dir, true);
    }

    void EnsureInit()
    {
        if (m_initialized) return;

        m_canvas = GetComponentInParent<Canvas>();
        if (viewport == null) viewport = transform as RectTransform;
        if (pageTemplate != null) pageTemplate.gameObject.SetActive(false);

        m_pageStep = ResolvePageStep();
        m_initialized = true;
    }

    void OnPrevPressed() => GoTo(m_index - 1, true);
    void OnNextPressed() => GoTo(m_index + 1, true);

    void GoTo(int _index, bool _animated)
    {
        EnsureInit();
        if (track == null) return;

        int t_next = CanLoop ? _index : Mathf.Clamp(_index, 0, Mathf.Max(0, m_pages.Count - 1));
        bool t_moved = PageOf(t_next) != PageOf(m_index);
        m_index = t_next;
        m_rawX = HomeXOf(m_index);

        // 페이지 재배치가 트랙 이동보다 먼저다 — 자리를 옮기는 페이지는 언제나 이동 구간 밖(마스크 밖)이라 티가 나지 않는다.
        LayoutPages();

        track.DOKill();
        float t_home = m_rawX;
        if (_animated && isActiveAndEnabled)
        {
            // 페이지가 바뀌면 이동(OutCubic), 되감기면 되튐(OutBack).
            track.DOAnchorPosX(t_home, snapDuration)
                 .SetEase(t_moved ? Ease.OutCubic : Ease.OutBack)
                 .SetLink(gameObject)
                 .OnComplete(NormalizeIndex);
        }
        else
        {
            track.anchoredPosition = new Vector2(t_home, track.anchoredPosition.y);
            NormalizeIndex();
        }

        ApplyIdleFocus(PageOf(m_index));
        RefreshArrows();
        if (dots != null) dots.SetIndex(m_pages.Count > 0 ? PageOf(m_index) : -1);

        if (t_moved) OnIndexChanged?.Invoke(PageOf(m_index));
    }

    // 흘러간 논리 인덱스를 0..N-1로 되접는다. 배치·트랙을 함께 다시 잡으므로 화면은 한 픽셀도 바뀌지 않는다 —
    // 순전히 인덱스와 트랙 좌표가 무한정 커지지 않게 하는 정리다.
    void NormalizeIndex()
    {
        if (!CanLoop || track == null) return;

        int t_page = PageOf(m_index);
        if (t_page == m_index) return;

        m_index = t_page;
        m_rawX = HomeXOf(m_index);
        LayoutPages();
        track.anchoredPosition = new Vector2(m_rawX, track.anchoredPosition.y);
    }

    // 페이지 i의 정착 좌표. 페이지는 x = i*step에 늘어서 있으므로 트랙은 그 반대로 밀려야 한다.
    float HomeXOf(int _i) => -_i * m_pageStep;

    // 논리 인덱스 → 실제 페이지 번호(0..N-1).
    int PageOf(int _i)
    {
        int t_n = m_pages.Count;
        return t_n > 0 ? ((_i % t_n) + t_n) % t_n : 0;
    }

    // 두 페이지 사이의 최단 방향 거리. 3장에서 +2는 -1과 같다(오른쪽으로 두 칸 = 왼쪽으로 한 칸).
    static int ShortestDelta(int _delta, int _count)
    {
        if (_count <= 0) return 0;

        int t_d = ((_delta % _count) + _count) % _count;
        return t_d * 2 > _count ? t_d - _count : t_d;
    }

    // 양 끝을 넘겨 끌면 저항이 붙는다. 완전히 안 움직이면 "고장"으로 읽히고, 그대로 따라오면 빈 화면이 나온다.
    // 순환 중에는 끝이 없으므로 그대로 따라온다.
    float ApplyEdgeResistance(float _x)
    {
        if (CanLoop) return _x;

        float t_max = HomeXOf(0);
        float t_min = HomeXOf(Mathf.Max(0, m_pages.Count - 1));

        if (_x > t_max) return t_max + (_x - t_max) * edgeResistance;
        if (_x < t_min) return t_min + (_x - t_min) * edgeResistance;
        return _x;
    }

    float ResolvePageStep()
    {
        if (pageStepOverride > 0f) return pageStepOverride;

        var t_vp = viewport != null ? viewport : transform as RectTransform;
        float t_width = t_vp != null ? t_vp.rect.width : 0f;
        return t_width > 1f ? t_width : m_pageStep;   // rect가 아직 드라이브되기 전이면 직전 값을 유지.
    }

    // 순환이 아니면 페이지를 0..N-1 자리에 한 줄로 고정한다.
    // 순환이면 현재 페이지를 기준으로 각 페이지를 최단 거리 자리에 놓는다 — 양옆이 항상 채워지고,
    // 자리를 옮기는 건 반대편 하나뿐이며 그건 이동 구간 밖이라 보이지 않는다.
    void LayoutPages()
    {
        int t_n = m_pages.Count;
        if (t_n == 0) return;

        if (!CanLoop)
        {
            for (int t_i = 0; t_i < t_n; t_i++)
                if (m_pages[t_i] != null) m_pages[t_i].anchoredPosition = new Vector2(t_i * m_pageStep, 0f);
            return;
        }

        int t_page = PageOf(m_index);
        for (int t_i = 0; t_i < t_n; t_i++)
        {
            if (m_pages[t_i] == null) continue;

            int t_d = ShortestDelta(t_i - t_page, t_n);
            m_pages[t_i].anchoredPosition = new Vector2((m_index + t_d) * m_pageStep, 0f);
        }
    }

    void ApplyIdleFocus(int _index)
    {
        for (int t_i = 0; t_i < m_motions.Count; t_i++)
            if (m_motions[t_i] != null) m_motions[t_i].enabled = t_i == _index;
    }

    void RefreshArrows()
    {
        bool t_multi = m_pages.Count > 1;

        // 순환 중에는 끝이 없으므로 양쪽 모두 항상 살아 있다.
        if (prevButton != null)
        {
            prevButton.gameObject.SetActive(t_multi);
            prevButton.interactable = m_interactable && (CanLoop || m_index > 0);
        }
        if (nextButton != null)
        {
            nextButton.gameObject.SetActive(t_multi);
            nextButton.interactable = m_interactable && (CanLoop || m_index < m_pages.Count - 1);
        }
    }

    void ClearPages()
    {
        for (int t_i = 0; t_i < m_pages.Count; t_i++)
        {
            if (m_pages[t_i] == null) continue;

            // Destroy는 프레임 끝에 실행된다 — 같은 프레임에 다시 세우면 옛 페이지가 x=0에 겹쳐 보인다.
            // 먼저 꺼서 화면에서 지우고 파괴는 엔진에 맡긴다.
            m_pages[t_i].gameObject.SetActive(false);
            Destroy(m_pages[t_i].gameObject);
        }

        m_pages.Clear();
        m_artImages.Clear();
        m_artBaseColors.Clear();
        m_motions.Clear();
    }
}
