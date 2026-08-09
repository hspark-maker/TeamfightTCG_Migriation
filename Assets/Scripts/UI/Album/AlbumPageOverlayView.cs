using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 테마 한 개의 페이지 열람 오버레이(Panel_PageOverlay 부착) — 페이지 스테퍼 통합
public class AlbumPageOverlayView : MonoBehaviour
{
    [Header("닫기")]
    [SerializeField] Button dimButton;
    [SerializeField] Button closeButton;

    [Header("페이지 보상")]
    [SerializeField] AlbumGaugeView pageGauge = new AlbumGaugeView();
    [SerializeField] AlbumChestView pageChest = new AlbumChestView();

    [Header("슬롯")]
    [SerializeField] Transform slotRoot;            // Grid_Slots
    [SerializeField] AlbumCardSlotView slotTemplate; // Slot_00

    [Header("페이지 넘기기")]
    [SerializeField] Button prevButton;
    [SerializeField] Button nextButton;
    [SerializeField] TMP_Text pageLabel;
    [Tooltip("선택 — 오버레이 전면 raycastTarget 위에 올린 스와이프 감지기.")]
    [SerializeField] HorizontalSwipeDetector swipeDetector;

    [Header("연출")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    AlbumTheme m_theme;
    int m_pageIndex;
    bool m_built;
    readonly List<AlbumCardSlotView> m_slots = new List<AlbumCardSlotView>();

    // 상세에서 넘겨볼 목록 = 이 테마의 **소유** 카드 전체(페이지 순). 미소유를 담지 않으므로 잠김 상세로 새지 않고,
    // 페이지 경계에서도 끊기지 않는다. CardDetailOverlayView가 참조로 쥔다 — 인스턴스를 유지하고 Clear+재충전만 한다
    readonly List<CardData> m_order = new List<CardData>();

    // 삽입 세션이 켜는 잠금 — 탈출로를 세션의 건너뛰기 하나로 좁힌다
    bool m_interactionLocked;

    public int PageIndex => m_pageIndex;

    public void Open(AlbumTheme _theme)
    {
        Open(_theme, 0);
    }

    public void Open(AlbumTheme _theme, int _pageIndex)
    {
        if (_theme == null || _theme.Pages == null || _theme.Pages.Count == 0)
        {
            Debug.LogWarning("[AlbumPageOverlayView] 빈 테마 — 오버레이를 열지 않는다.", this);
            return;
        }

        bool t_wasActive = gameObject.activeSelf;
        m_theme = _theme;
        m_pageIndex = Mathf.Clamp(_pageIndex, 0, _theme.Pages.Count - 1);

        // 활성화가 OnEnable→RefreshPage를 태우므로 상태 세팅이 먼저다
        transition.SetVisible(gameObject, true);
        if (t_wasActive) RefreshPage();   // 이미 열려 있으면 OnEnable이 안 돈다
    }

    public void Close()
    {
        transition.SetVisible(gameObject, false);
    }

    // 삽입 카드를 꽂을 칸 — 슬롯은 RefreshPage가 만든 뒤에야 존재하고 레이아웃도 그 프레임 이후에 확정된다.
    // 세션은 rect(위치)뿐 아니라 칸 자신(씰 덮개·InsertDock)이 필요하므로 뷰째로 넘긴다.
    public bool TryGetSlot(int _slotIndex, out AlbumCardSlotView _slot)
    {
        _slot = null;
        if (_slotIndex < 0 || _slotIndex >= m_slots.Count) return false;

        var t_slot = m_slots[_slotIndex];
        if (t_slot == null || !t_slot.gameObject.activeSelf) return false;

        _slot = t_slot;
        return true;
    }

    public void SetInteractionLocked(bool _locked)
    {
        m_interactionLocked = _locked;

        if (dimButton != null) dimButton.interactable = !_locked;
        if (closeButton != null) closeButton.interactable = !_locked;
        if (swipeDetector != null) swipeDetector.Interactable = !_locked;

        // 잠금 해제는 페이지 수가 정하던 원래 값으로 되돌린다
        bool t_steppable = !_locked && m_theme != null && m_theme.Pages.Count > 1;
        if (prevButton != null) prevButton.interactable = t_steppable;
        if (nextButton != null) nextButton.interactable = t_steppable;
    }

    void Awake()
    {
        // 런타임 RemoveAllListeners는 퍼시스턴트를 못 지운다 — 목업 onClick은 배선 단계에서 지워야 한다
        if (dimButton != null && dimButton.onClick.GetPersistentEventCount() > 0)
            Debug.LogWarning("[AlbumPageOverlayView] Dim에 목업 퍼시스턴트 onClick이 남아 있다 — 프리팹에서 제거할 것.", this);
        if (closeButton != null && closeButton.onClick.GetPersistentEventCount() > 0)
            Debug.LogWarning("[AlbumPageOverlayView] Button_Close에 목업 퍼시스턴트 onClick이 남아 있다 — 프리팹에서 제거할 것.", this);

        if (dimButton != null) dimButton.onClick.AddListener(Close);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (prevButton != null) prevButton.onClick.AddListener(() => Step(-1));
        if (nextButton != null) nextButton.onClick.AddListener(() => Step(1));
    }

    void OnEnable()
    {
        if (!m_built) BuildSlots();

        OwnershipManager.OnOwnershipChanged += HandleChanged;
        AlbumRewardManager.OnChanged += HandleChanged;
        CardGrowthManager.OnGrowthChanged += HandleChanged;
        AlbumInsertMask.OnChanged += HandleChanged;
        if (swipeDetector != null) swipeDetector.OnSwipe += Step;

        if (m_theme != null) RefreshPage();
    }

    void OnDisable()
    {
        OwnershipManager.OnOwnershipChanged -= HandleChanged;
        AlbumRewardManager.OnChanged -= HandleChanged;
        CardGrowthManager.OnGrowthChanged -= HandleChanged;
        AlbumInsertMask.OnChanged -= HandleChanged;
        if (swipeDetector != null) swipeDetector.OnSwipe -= Step;

        // 안전망 — 세션 없이 위장만 남으면 카드가 영영 빈 칸으로 보인다
        if (!AlbumInsertSession.IsRunning) AlbumInsertMask.Clear();

        transition.HandleDisabled(gameObject);
    }

    void HandleChanged()
    {
        if (m_theme != null) RefreshPage();
    }

    void BuildSlots()
    {
        m_built = true;

        if (slotRoot == null || slotTemplate == null)
        {
            Debug.LogError($"[AlbumPageOverlayView] 배선 누락 — slotRoot={slotRoot}, slotTemplate={slotTemplate}. 슬롯을 만들지 않는다.", this);
            return;
        }

        // Destroy는 프레임 말 지연이라 먼저 꺼야 같은 프레임 레이아웃이 더미까지 읽지 않는다
        for (int t_i = slotRoot.childCount - 1; t_i >= 0; t_i--)
        {
            var t_child = slotRoot.GetChild(t_i).gameObject;
            if (t_child == slotTemplate.gameObject) continue;
            t_child.SetActive(false);
            Destroy(t_child);
        }
        slotTemplate.gameObject.SetActive(false);
    }

    void RefreshPage()
    {
        if (m_theme == null || m_theme.Pages.Count == 0) return;
        if (slotRoot == null || slotTemplate == null) return;

        m_pageIndex = Mathf.Clamp(m_pageIndex, 0, m_theme.Pages.Count - 1);
        var t_page = m_theme.Pages[m_pageIndex];
        var t_cards = t_page.Cards;

        while (m_slots.Count < t_cards.Count)
            m_slots.Add(Instantiate(slotTemplate, slotRoot));

        // 빈 칸에 찍는 도감 번호는 페이지가 아니라 테마 내 통번호다 — 페이지마다 1로 되돌아가면 번호가 자리를 못 가리킨다
        int t_baseNumber = 0;
        for (int t_p = 0; t_p < m_pageIndex; t_p++)
            t_baseNumber += m_theme.Pages[t_p].Cards.Count;

        // 목록은 테마 전체라 이 페이지의 첫 소유 카드가 놓인 자리부터 세어 나간다
        int t_orderOffset = BuildOwnedOrder();
        int t_ownedInPage = 0;

        for (int t_i = 0; t_i < m_slots.Count; t_i++)
        {
            var t_slot = m_slots[t_i];
            if (t_i >= t_cards.Count)
            {
                t_slot.gameObject.SetActive(false);
                continue;
            }

            var t_card = t_cards[t_i];
            bool t_owned = ShownAsOwned(t_card);
            t_slot.gameObject.SetActive(true);
            t_slot.Bind(t_card, t_owned, t_baseNumber + t_i + 1);

            // 자리 소비는 버튼 유무보다 먼저다 — 미배선 칸에서 건너뛰면 이후 칸의 인덱스가 통째로 밀린다
            int t_orderIndex = t_owned ? t_orderOffset + t_ownedInPage++ : -1;

            var t_button = t_slot.Button;
            if (t_button == null) continue;
            t_button.onClick.RemoveAllListeners();
            if (t_orderIndex < 0) continue;

            t_button.onClick.AddListener(() =>
            {
                if (m_interactionLocked) return;   // 삽입 중엔 상세로 새지 않는다
                CardDetailOverlayView.Open(m_order, t_orderIndex);
            });
        }

        if (pageLabel != null) pageLabel.text = $"{m_pageIndex + 1} / {m_theme.Pages.Count}";

        var t_info = AlbumRewardManager.GetPageInfo(t_page);
        int t_hidden = AlbumInsertMask.HiddenCountIn(t_page);
        pageGauge.Set(t_info.Owned - t_hidden, t_info.Total);

        // 위장분이 남아 있으면 상자를 감춘다 — 마지막 칸을 꽂는 순간의 등장이 보상 신호다
        if (t_hidden > 0)
        {
            var t_empty = default(AlbumRewardInfo);
            pageChest.Bind(t_empty, null);
        }
        else
        {
            pageChest.Bind(t_info, ClaimPageReward);
        }

        bool t_steppable = !m_interactionLocked && m_theme.Pages.Count > 1;
        if (prevButton != null) prevButton.interactable = t_steppable;
        if (nextButton != null) nextButton.interactable = t_steppable;
    }

    // 삽입 연출 중에는 아직 안 꽂은 카드를 빈 칸으로 위장한다. 소유는 이미 확정됐지만 화면상 꽂기 전이다.
    // 상세 목록(BuildOwnedOrder)도 같은 함수를 타야 한다 — 아니면 빈 칸인 카드가 상세로 샌다.
    static bool ShownAsOwned(CardData _card)
        => _card != null && OwnershipManager.IsOwned(_card) && !AlbumInsertMask.IsHidden(_card);

    // m_order를 테마의 소유 카드로 다시 채우고, 지금 페이지의 카드들이 시작되는 자리를 돌려준다.
    // 소유가 바뀌면 RefreshPage가 다시 돌아 목록과 배선이 같은 프레임에 함께 갱신된다.
    int BuildOwnedOrder()
    {
        m_order.Clear();

        int t_offset = 0;
        for (int t_p = 0; t_p < m_theme.Pages.Count; t_p++)
        {
            if (t_p == m_pageIndex) t_offset = m_order.Count;

            var t_cards = m_theme.Pages[t_p].Cards;
            for (int t_i = 0; t_i < t_cards.Count; t_i++)
            {
                var t_card = t_cards[t_i];
                if (!ShownAsOwned(t_card)) continue;

                m_order.Add(t_card);
            }
        }

        return t_offset;
    }

    void Step(int _dir)
    {
        if (m_theme == null || m_theme.Pages.Count == 0) return;

        int t_count = m_theme.Pages.Count;
        m_pageIndex = (m_pageIndex + _dir + t_count) % t_count;
        RefreshPage();
    }

    void ClaimPageReward()
    {
        if (m_theme == null || m_theme.Pages.Count == 0) return;

        var t_page = m_theme.Pages[Mathf.Clamp(m_pageIndex, 0, m_theme.Pages.Count - 1)];
        var t_rewards = t_page.Rewards;   // Claim 전에 캡처
        if (!AlbumRewardManager.ClaimPage(t_page)) return;

        if (!CurrencyGainEffectPlayer.TryGet(this, out var t_player)) return;

        var t_bucket = new CurrencyGainBucket();
        for (int t_i = 0; t_i < t_rewards.Count; t_i++)
            t_bucket.Add(t_rewards[t_i].currency, t_rewards[t_i].amount);
        t_player.Play(pageChest.Rect, t_bucket);
    }
}
