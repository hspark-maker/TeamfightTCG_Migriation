using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

// 개봉 카드 더미. 카드를 한 자리에 겹쳐 쌓고, 맨 위부터 스와이프로 한 장씩 밀어낸다.
// 카드는 앞면이라 맨 위가 처음부터 보인다 — 서스펜스는 "밀어냈을 때 그 아래 뭐가 있나"에 있다.
//
// 배선 전제: 카드·stackAnchor·discardArea가 모두 cardLayer의 자식이고 앵커가 같아야 한다
//   (anchoredPosition을 같은 좌표계로 직접 비교·보간하므로).
// 입력은 이 컴포넌트가 붙은 오브젝트의 Graphic(raycastTarget)이 받는다 — 카드 프리팹은 입력을 모른다.
public class PackCardStack : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    // 새 카드가 맨 위로 드러났다(첫 장 포함). 강조 재생은 구독자가 결정한다.
    public event Action<PackCardView> OnCardRevealed;

    // 남은 장수가 바뀌었다(카운터 표시용).
    public event Action<int> OnRemainingChanged;

    // 마지막 장까지 밀려 더미가 비었다.
    public event Action OnEmptied;

    // 드래그가 아닌 단순 탭 — 스킵 요청. 이 컴포넌트가 입력을 먹으므로 상위로 올려준다.
    public event Action OnSkipRequested;

    [Header("배치")]
    [Tooltip("카드·앵커가 함께 사는 좌표계 루트. 카드는 전부 이 아래 생성된다.")]
    [SerializeField] RectTransform cardLayer;
    [Tooltip("더미가 쌓이는 자리. 모든 카드가 여기 겹쳐 놓인다.")]
    [SerializeField] RectTransform stackAnchor;
    [Tooltip("밀어낸 카드가 정리되는 자리(하단 라인업 중심).")]
    [SerializeField] RectTransform discardArea;
    [SerializeField] PackCardView cardPrefab;

    [Header("더미 겹침")]
    [Tooltip("겹친 카드 사이의 미세 어긋남. 0이면 종이 느낌이 죽는다.")]
    [SerializeField] float stackJitterPos = 2f;
    [SerializeField] float stackJitterAngle = 1f;

    [Header("정리 라인업")]
    [SerializeField] float discardSpacing = 220f;
    [SerializeField] float discardScale = 0.7f;
    [SerializeField] float discardDuration = 0.35f;

    [Header("스와이프")]
    [Tooltip("이만큼 밀면 넘어간다. 못 미치면 제자리로 되돌아온다.")]
    [SerializeField] float flickThreshold = 120f;
    [Tooltip("미는 양에 비례해 카드가 기우는 정도(도/픽셀).")]
    [SerializeField] float dragTiltPerPixel = 0.06f;
    [SerializeField] float returnDuration = 0.25f;

    // 더미(위→아래 순). 인덱스 0이 항상 현재 맨 위다.
    readonly List<PackCardView> m_stack = new List<PackCardView>();

    // 밀어낸 카드가 갈 자리를 정하는 카운터. 최종 라인업 기준이라 재정렬이 필요 없다.
    int m_discarded;
    int m_total;

    // 카드가 처음 놓인 자리(되감기 기준). 미세 지터가 섞여 있어 앵커 값과 다르다.
    readonly Dictionary<PackCardView, Vector2> m_home = new Dictionary<PackCardView, Vector2>();

    Canvas m_canvas;
    bool m_interactable;
    bool m_dragging;

    public int Remaining => m_stack.Count;

    void Awake()
    {
        m_canvas = GetComponentInParent<Canvas>();
    }

    /// <summary>뽑힌 카드로 더미를 만든다. 아직 입력은 받지 않는다(분출 연출이 끝난 뒤 BeginInteraction).</summary>
    public void Build(IReadOnlyList<DrawnCard> _cards)
    {
        Clear();

        if (cardPrefab == null || cardLayer == null || stackAnchor == null)
        {
            Debug.LogWarning("[PackCardStack] cardPrefab/cardLayer/stackAnchor 미배선 → 더미 생성 불가.");
            return;
        }

        int t_count = _cards != null ? _cards.Count : 0;
        m_total = t_count;
        m_discarded = 0;

        // 뒤에서부터 만들어 인덱스 0이 마지막 sibling(=가장 위에 그려짐)이 되게 한다.
        for (int t_i = t_count - 1; t_i >= 0; t_i--)
        {
            var t_drawn = _cards[t_i];
            if (t_drawn.Card == null) continue;

            var t_view = Instantiate(cardPrefab, cardLayer);
            t_view.Bind(t_drawn);

            var t_rt = (RectTransform)t_view.transform;
            var t_home = stackAnchor.anchoredPosition + new Vector2(
                UnityEngine.Random.Range(-stackJitterPos, stackJitterPos),
                UnityEngine.Random.Range(-stackJitterPos, stackJitterPos));

            t_rt.anchoredPosition = t_home;
            t_rt.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-stackJitterAngle, stackJitterAngle));
            t_rt.localScale = Vector3.one;

            m_home[t_view] = t_home;
            m_stack.Insert(0, t_view);   // 역순 생성이라 앞에 꽂아야 위→아래 순서가 맞는다.
        }

        OnRemainingChanged?.Invoke(m_stack.Count);
    }

    /// <summary>입력을 켜고 첫 장이 드러났음을 알린다(앞면이라 이미 보이고 있다).</summary>
    public void BeginInteraction()
    {
        m_interactable = true;
        if (m_stack.Count > 0) OnCardRevealed?.Invoke(m_stack[0]);
    }

    /// <summary>남은 카드를 전부 즉시 라인업으로 정리한다(스킵). 강조는 즉시 모드로 남긴다.</summary>
    public void FlickAllImmediate()
    {
        m_interactable = false;
        m_dragging = false;

        while (m_stack.Count > 0)
        {
            var t_view = m_stack[0];
            m_stack.RemoveAt(0);

            t_view.PlayRevealAccent(true);
            SendToDiscard(t_view, true);
        }

        OnRemainingChanged?.Invoke(0);
        OnEmptied?.Invoke();
    }

    /// <summary>생성된 카드를 모두 제거(다음 개봉 세션 대비).</summary>
    public void Clear()
    {
        for (int t_i = 0; t_i < m_stack.Count; t_i++)
            if (m_stack[t_i] != null) Destroy(m_stack[t_i].gameObject);

        m_stack.Clear();
        m_home.Clear();
        m_discarded = 0;
        m_total = 0;
        m_interactable = false;
        m_dragging = false;
    }

    public void OnBeginDrag(PointerEventData _e)
    {
        if (!m_interactable || m_stack.Count == 0) return;
        m_dragging = true;

        var t_top = m_stack[0];
        if (t_top != null) t_top.transform.DOKill();
    }

    public void OnDrag(PointerEventData _e)
    {
        if (!m_dragging || m_stack.Count == 0) return;

        var t_rt = TopRect();
        if (t_rt == null) return;

        // 캔버스 스케일을 나눠야 화면 이동량과 카드 이동량이 일치한다.
        float t_scale = m_canvas != null ? m_canvas.scaleFactor : 1f;
        if (t_scale <= 0f) t_scale = 1f;

        t_rt.anchoredPosition += _e.delta / t_scale;

        // 민 만큼 기운다 — 손에 붙는 느낌은 위치보다 회전에서 온다.
        float t_dx = t_rt.anchoredPosition.x - HomeOf(m_stack[0]).x;
        t_rt.localRotation = Quaternion.Euler(0f, 0f, -t_dx * dragTiltPerPixel);
    }

    public void OnEndDrag(PointerEventData _e)
    {
        if (!m_dragging || m_stack.Count == 0) return;
        m_dragging = false;

        var t_top = m_stack[0];
        var t_rt = TopRect();
        if (t_rt == null) return;

        float t_dx = t_rt.anchoredPosition.x - HomeOf(t_top).x;

        // 임계 미만은 되감기 — 실수로 건드린 것을 넘김으로 처리하지 않는다.
        if (Mathf.Abs(t_dx) < flickThreshold)
        {
            ReturnHome(t_top);
            return;
        }

        m_stack.RemoveAt(0);
        SendToDiscard(t_top, false);

        OnRemainingChanged?.Invoke(m_stack.Count);

        // 위 장이 비켜난 순간 아래 장은 이미 완전히 드러나 있다.
        if (m_stack.Count > 0) OnCardRevealed?.Invoke(m_stack[0]);
        else OnEmptied?.Invoke();
    }

    public void OnPointerClick(PointerEventData _e)
    {
        // 드래그로 소비된 포인터는 클릭으로 오지 않는다 — 순수 탭만 스킵으로 본다.
        if (_e.dragging) return;
        OnSkipRequested?.Invoke();
    }

    // 밀려난 카드를 최종 라인업 자리로 보낸다. 자리는 밀린 순서로 이미 정해져 있어 재정렬이 없다.
    void SendToDiscard(PackCardView _view, bool _instant)
    {
        if (_view == null) return;

        var t_rt = (RectTransform)_view.transform;
        t_rt.DOKill();

        var t_target = DiscardSlot(m_discarded);
        m_discarded++;

        if (_instant)
        {
            t_rt.anchoredPosition = t_target;
            t_rt.localRotation = Quaternion.identity;
            t_rt.localScale = Vector3.one * discardScale;
            return;
        }

        DOTween.Sequence()
            .SetLink(_view.gameObject)
            .Join(t_rt.DOAnchorPos(t_target, discardDuration).SetEase(Ease.OutCubic))
            .Join(t_rt.DOScale(discardScale, discardDuration).SetEase(Ease.OutCubic))
            .Join(t_rt.DOLocalRotate(Vector3.zero, discardDuration));
    }

    // 라인업 i번째 자리. 전체 장수를 알고 있으므로 중앙 정렬을 미리 계산한다.
    Vector2 DiscardSlot(int _index)
    {
        var t_center = discardArea != null ? discardArea.anchoredPosition : Vector2.zero;
        float t_offset = (_index - (m_total - 1) * 0.5f) * discardSpacing;
        return t_center + new Vector2(t_offset, 0f);
    }

    // 임계에 못 미친 카드를 원래 자리로 되돌린다.
    void ReturnHome(PackCardView _view)
    {
        if (_view == null) return;

        var t_rt = (RectTransform)_view.transform;
        t_rt.DOKill();
        t_rt.DOAnchorPos(HomeOf(_view), returnDuration).SetEase(Ease.OutBack).SetLink(_view.gameObject);
        t_rt.DOLocalRotate(Vector3.zero, returnDuration).SetLink(_view.gameObject);
    }

    RectTransform TopRect()
        => m_stack.Count > 0 && m_stack[0] != null ? (RectTransform)m_stack[0].transform : null;

    Vector2 HomeOf(PackCardView _view)
        => _view != null && m_home.TryGetValue(_view, out var t_home) ? t_home : Vector2.zero;

    void OnDisable()
    {
        // 연출 도중 비활성 시 좀비 트윈 정리 + 입력 상태 리셋(재활성 후 유령 드래그 방지).
        for (int t_i = 0; t_i < m_stack.Count; t_i++)
            if (m_stack[t_i] != null) m_stack[t_i].transform.DOKill();

        m_interactable = false;
        m_dragging = false;
    }
}
