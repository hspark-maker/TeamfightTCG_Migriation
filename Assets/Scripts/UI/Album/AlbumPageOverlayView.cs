using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
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

    [Tooltip("선택 — 지금 열어둔 테마 이름(CollectionTitle). 미배선이면 저작된 글자를 그대로 둔다.")]
    [SerializeField] TMP_Text titleLabel;

    [Header("페이지 넘기기")]
    [SerializeField] Button prevButton;
    [SerializeField] Button nextButton;
    [SerializeField] TMP_Text pageLabel;
    [Tooltip("선택 — 오버레이 전면 raycastTarget 위에 올린 스와이프 감지기.")]
    [SerializeField] HorizontalSwipeDetector swipeDetector;

    [Header("연출")]
    [SerializeField] PopupTransition transition = new PopupTransition();
    [SerializeField] AlbumPageFlipView pageFlip = new AlbumPageFlipView();
    [Tooltip("선택 — 종이와 따로 크로스페이드할 주변 UI 묶음(Row_PageGauge). 미배선이면 페이드를 건너뛴다.")]
    [SerializeField] RectTransform sideFadeRoot;

    AlbumTheme m_theme;
    int m_pageIndex;
    bool m_built;
    readonly List<AlbumCardSlotView> m_slots = new List<AlbumCardSlotView>();

    // 상세에서 넘겨볼 목록 = 이 테마의 **소유** 카드 전체(페이지 순). 미소유를 담지 않으므로 잠김 상세로 새지 않고,
    // 페이지 경계에서도 끊기지 않는다. CardDetailOverlayView가 참조로 쥔다 — 인스턴스를 유지하고 Clear+재충전만 한다
    readonly List<CardData> m_order = new List<CardData>();

    // 삽입 세션이 켜는 잠금 — 탈출로를 세션의 건너뛰기 하나로 좁힌다
    bool m_sessionLocked;
    // 넘김 한 번 동안만. 세션과 bool을 공유하면 넘김이 끝날 때 세션 잠금까지 같이 풀린다
    bool m_flipLocked;
    bool m_flipping;
    // 진행 중이던 넘김이 Open/OnDisable로 잘렸는지 판정. 잘린 넘김이 뒤늦게 인덱스를 덮어쓰면 안 된다
    int m_flipGen;

    bool IsLocked => m_sessionLocked || m_flipLocked;

    public int PageIndex => m_pageIndex;

    /// <summary>넘김 한 번에 걸리는 시간의 단일 진실원 — 삽입 세션이 따로 값을 들고 있지 않게 한다.</summary>
    public float FlipDuration => pageFlip.Duration;

    public bool IsFlipping => m_flipping;

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

        CancelFlip();   // 잘린 넘김 자세를 안고 열리지 않게

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
        m_sessionLocked = _locked;
        ApplyInteractable();
    }

    void SetFlipLocked(bool _locked)
    {
        m_flipLocked = _locked;
        ApplyInteractable();
    }

    void ApplyInteractable()
    {
        bool t_locked = IsLocked;

        if (dimButton != null) dimButton.interactable = !t_locked;
        if (closeButton != null) closeButton.interactable = !t_locked;
        if (swipeDetector != null) swipeDetector.Interactable = !t_locked;

        // 잠금 해제는 페이지 수가 정하던 원래 값으로 되돌린다
        bool t_steppable = !t_locked && m_theme != null && m_theme.Pages.Count > 1;
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

        // 회전 대상은 Panel_Page가 아니라 slotRoot(Grid_Slots)다 — 같은 사각형이면서 부모 레이아웃이
        // anchoredPosition을 안 덮어쓰는 유일한 노드라 축 보정이 되돌려지지 않는다
        pageFlip.Bind(slotRoot as RectTransform, sideFadeRoot, pageLabel);
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

        // 탭 전환 등으로 넘김 도중에 꺼지면 종이가 세워진 채 굳는다
        CancelFlip();

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
                if (IsLocked) return;   // 삽입 중이거나 넘기는 중엔 상세로 새지 않는다
                CardDetailOverlayView.Open(m_order, t_orderIndex);
            });
        }

        if (pageLabel != null) pageLabel.text = $"{m_pageIndex + 1} / {m_theme.Pages.Count}";

        // 제목은 페이지를 넘겨도 같은 테마다. 그래도 페이지 갱신과 같은 자리에서 찍는다 —
        // 테마가 바뀌는 경로(Open·세션의 GoToPage)가 전부 여기를 지나므로 갱신 지점을 늘리지 않는다.
        if (titleLabel != null) titleLabel.text = m_theme.DisplayName;

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

        // 잠금은 리프레시로 풀리지 않는다 — 넘김 중에도 이벤트가 이 함수를 부른다
        ApplyInteractable();
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
        FlipStepAsync(_dir).Forget();
    }

    /// <summary>테마·페이지를 지정해 옮긴다. 열려 있으면 넘김 연출을 태우고, 닫혀 있으면 팝업 열기에 맡긴다.</summary>
    public async UniTask GoToPageAsync(AlbumTheme _theme, int _pageIndex)
    {
        if (_theme == null || _theme.Pages == null || _theme.Pages.Count == 0) return;

        // 닫혀 있으면 넘길 옛 페이지가 없다 — 팝업 등장에 맡기고 그게 끝날 때까지 기다린다
        if (!gameObject.activeSelf || m_theme == null || m_flipping || pageFlip.Duration <= 0f)
        {
            Open(_theme, _pageIndex);
            await UniTask.Delay((int)(transition.OpenDuration * 1000f), ignoreTimeScale: true);
            return;
        }

        int t_target = Mathf.Clamp(_pageIndex, 0, _theme.Pages.Count - 1);
        if (m_theme == _theme && t_target == m_pageIndex) return;

        // 테마가 통째로 바뀌면 인덱스 비교가 뜻이 없다 — "다음 장"으로 읽히게 한다
        int t_dir = (m_theme != _theme || t_target > m_pageIndex) ? 1 : -1;
        await FlipAsync(t_target, t_dir, _theme);
    }

    async UniTask FlipStepAsync(int _dir)
    {
        if (m_flipping) return;   // 넘기는 중 재입력은 무시 — 인덱스만 앞서가는 분기를 원천 차단한다
        if (m_theme == null || m_theme.Pages.Count == 0) return;

        int t_count  = m_theme.Pages.Count;
        int t_target = (m_pageIndex + _dir + t_count) % t_count;

        if (t_count <= 1 || pageFlip.Duration <= 0f)
        {
            m_pageIndex = t_target;
            RefreshPage();
            return;
        }

        await FlipAsync(t_target, _dir, null);
    }

    async UniTask FlipAsync(int _target, int _dir, AlbumTheme _theme)
    {
        int t_gen = ++m_flipGen;

        m_flipping = true;
        SetFlipLocked(true);
        pageFlip.Begin(_dir);

        try
        {
            float t_p = 0f;
            await DOTween.To(() => t_p, _v => { t_p = _v; pageFlip.SetFlipProgress(_v); }, 0.5f, pageFlip.Duration * 0.5f)
                .SetEase(Ease.InQuad).SetLink(gameObject).SetId(this).ToUniTask();

            if (t_gen != m_flipGen) return;   // 도중에 잘렸다 — 새 페이지를 덮어쓰면 안 된다

            // edge-on(종이가 안 보이는 순간)에 교체한다. RefreshPage는 m_pageIndex의 순수 함수라
            // 연출을 전혀 몰라도 되고, 도중에 이벤트가 난입해도 화면이 어긋나지 않는다
            if (_theme != null) m_theme = _theme;
            m_pageIndex = _target;
            RefreshPage();
            pageFlip.EnsureShadeOnTop();   // 슬롯이 새로 생겼으면 그늘이 카드 뒤로 묻힌다

            await DOTween.To(() => t_p, _v => { t_p = _v; pageFlip.SetFlipProgress(_v); }, 1f, pageFlip.Duration * 0.5f)
                .SetEase(Ease.OutQuad).SetLink(gameObject).SetId(this).ToUniTask();
        }
        finally
        {
            if (t_gen == m_flipGen)
            {
                pageFlip.Cancel();
                m_flipping = false;
                SetFlipLocked(false);
            }
        }
    }

    void CancelFlip()
    {
        m_flipGen++;                 // 진행 중이던 넘김의 커밋·정리를 무효화한다
        DOTween.Kill(this, true);    // SetId(this)를 단 넘김 트윈만. complete=true라야 대기가 취소가 아닌 완료로 풀린다
        pageFlip.Cancel();
        m_flipping = false;
        SetFlipLocked(false);
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
