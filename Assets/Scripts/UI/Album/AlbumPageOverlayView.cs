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
    [SerializeField] Transform underSlotRoot;       // Grid_Slots_Under — 드래그 중 다음 장을 미리 보여주는 뒤쪽 버퍼
    [SerializeField] AlbumCardSlotView slotTemplate; // Slot_00

    [Tooltip("선택 — 지금 열어둔 테마 이름(CollectionTitle). 미배선이면 저작된 글자를 그대로 둔다.")]
    [SerializeField] TMP_Text titleLabel;

    [Tooltip("한 페이지가 늘 차지하는 칸 수(Grid_Slots는 3열 = 3x3이라 9). 저작 칸이 이보다 적으면 빈 칸으로 채워\n" +
             "페이지마다 격자가 들쭉날쭉해지지 않게 한다. 저작 칸이 더 많으면 그만큼 그대로 늘린다.")]
    [SerializeField] int pageSlotCount = 9;

    [Header("페이지 넘기기")]
    [SerializeField] Button prevButton;
    [SerializeField] Button nextButton;
    [SerializeField] TMP_Text pageLabel;
    [Tooltip("선택 — 오버레이 전면 raycastTarget 위에 올린 스와이프 감지기.")]
    [SerializeField] HorizontalSwipeDetector swipeDetector;

    [Tooltip("좌/우 민감도. 화면 폭의 이 비율만큼 끌면 종이가 완전히 세워진다(진행도 0.5 = 넘어가기 직전).\n" +
             "작을수록 민감하다 — 0.4면 화면 폭의 40%를 끌어야 다 세워지고, 0.15면 15%만 끌어도 다 세워진다.\n" +
             "넘김이 실제로 확정되는 임계는 감지기(HorizontalSwipeDetector.snapRatio)가 따로 정한다 —\n" +
             "이 값은 손가락 이동과 그림이 세워지는 정도의 배율일 뿐이다.")]
    [Range(0.05f, 1f)] [SerializeField] float dragFullRatio = 0.4f;

    [Tooltip("임계 미달로 손을 뗐을 때 제자리로 돌아오는 시간.")]
    [SerializeField] float dragReturnDuration = 0.16f;

    [Header("연출")]
    [SerializeField] PopupTransition transition = new PopupTransition();
    [SerializeField] AlbumPageFlipView pageFlip = new AlbumPageFlipView();
    [Tooltip("선택 — 종이와 따로 크로스페이드할 주변 UI 묶음(Row_PageGauge). 미배선이면 페이드를 건너뛴다.")]
    [SerializeField] RectTransform sideFadeRoot;

    AlbumTheme m_theme;
    int m_pageIndex;
    bool m_built;
    readonly List<AlbumCardSlotView> m_slots = new List<AlbumCardSlotView>();
    readonly List<AlbumCardSlotView> m_underSlots = new List<AlbumCardSlotView>();
    AlbumTheme m_underTheme;
    int m_underPageIndex = -1;

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

    // 손가락이 직접 종이를 밀고 있는 구간. 트윈(m_flipping)과 갈라 둔다 —
    // 드래그 중에는 아직 넘길지 말지 정해지지 않아 페이지를 교체하면 안 되고, 잠금도 걸지 않는다.
    bool  m_dragging;
    // 손가락이 **새로** 내려앉았는가. 감지기는 OnBeginDrag에서만 0을 보내므로 그 통지로만 무장한다 —
    // 이게 없으면 넘김이 끝나 잠금이 풀린 프레임에 남은 이동 통지가 들어와 접힘이 한 번 더 돈다.
    bool  m_dragArmed;
    bool  m_dragReturning;
    float m_dragProgress;   // 0 ~ 0.5(edge-on 직전). 손가락으로는 교체 지점을 넘기지 않는다
    int   m_dragDir;
    bool  m_refreshPending;

    bool IsLocked => m_sessionLocked || m_flipLocked || m_dragReturning;

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
        // 색으로 잠긴 티를 내는 건 **세션 잠금**뿐이다. 넘김 잠금은 0.3초짜리라 Button의 Color Tint가
        // 켜졌다 꺼지는 것이 "dim이 풀렸다 돌아온다 / 칸이 깜빡인다"로 보인다.
        // 짧은 잠금은 색을 건드리지 않고 눌렀을 때 걸러낸다(HandleCloseRequest·HandleStepRequest).
        bool t_dimmed = m_sessionLocked;

        if (dimButton != null) dimButton.interactable = !t_dimmed;
        if (closeButton != null) closeButton.interactable = !t_dimmed;
        if (swipeDetector != null) swipeDetector.Interactable = !IsLocked;

        bool t_steppable = !t_dimmed && m_theme != null && m_theme.Pages.Count > 1;
        if (prevButton != null) prevButton.interactable = t_steppable;
        if (nextButton != null) nextButton.interactable = t_steppable;
    }

    /// <summary>닫기 요청. 잠금은 색이 아니라 여기서 막는다 — 넘김 도중 눌러도 아무 일이 없다.</summary>
    void HandleCloseRequest()
    {
        if (IsLocked) return;
        Close();
    }

    /// <summary>페이지 스테퍼 요청. 스와이프와 같은 잠금 규칙을 탄다.</summary>
    void HandleStepRequest(int _dir)
    {
        if (IsLocked || m_flipping || m_dragging) return;
        pageFlip.ClearTouchAnchor();
        Step(_dir);
    }

    void Awake()
    {
        // 런타임 RemoveAllListeners는 퍼시스턴트를 못 지운다 — 목업 onClick은 배선 단계에서 지워야 한다
        if (dimButton != null && dimButton.onClick.GetPersistentEventCount() > 0)
            Debug.LogWarning("[AlbumPageOverlayView] Dim에 목업 퍼시스턴트 onClick이 남아 있다 — 프리팹에서 제거할 것.", this);
        if (closeButton != null && closeButton.onClick.GetPersistentEventCount() > 0)
            Debug.LogWarning("[AlbumPageOverlayView] Button_Close에 목업 퍼시스턴트 onClick이 남아 있다 — 프리팹에서 제거할 것.", this);

        if (dimButton != null) dimButton.onClick.AddListener(HandleCloseRequest);
        if (closeButton != null) closeButton.onClick.AddListener(HandleCloseRequest);
        if (prevButton != null) prevButton.onClick.AddListener(() => HandleStepRequest(-1));
        if (nextButton != null) nextButton.onClick.AddListener(() => HandleStepRequest(1));

        // 회전 대상은 Panel_Page가 아니라 slotRoot(Grid_Slots)다 — 같은 사각형이면서 부모 레이아웃이
        // anchoredPosition을 안 덮어쓰는 유일한 노드라 축 보정이 되돌려지지 않는다
        pageFlip.Bind(
            slotRoot as RectTransform,
            sideFadeRoot,
            pageLabel,
            underSlotRoot != null ? underSlotRoot.GetComponent<CanvasGroup>() : null,
            underSlotRoot as RectTransform);
    }

    void OnEnable()
    {
        if (!m_built) BuildSlots();

        OwnershipManager.OnOwnershipChanged += HandleChanged;
        AlbumRewardManager.OnChanged += HandleChanged;
        CardGrowthManager.OnGrowthChanged += HandleChanged;
        AlbumInsertMask.OnChanged += HandleChanged;
        if (swipeDetector != null)
        {
            swipeDetector.OnSwipe        += HandleSwipe;
            swipeDetector.OnDragBegin    += HandleDragBegin;
            swipeDetector.OnDragProgress += HandleDragProgress;
            swipeDetector.OnDragCancel   += HandleDragCancel;
        }

        if (m_theme != null) RefreshPage();
    }

    void OnDisable()
    {
        OwnershipManager.OnOwnershipChanged -= HandleChanged;
        AlbumRewardManager.OnChanged -= HandleChanged;
        CardGrowthManager.OnGrowthChanged -= HandleChanged;
        AlbumInsertMask.OnChanged -= HandleChanged;
        if (swipeDetector != null)
        {
            swipeDetector.OnSwipe        -= HandleSwipe;
            swipeDetector.OnDragBegin    -= HandleDragBegin;
            swipeDetector.OnDragProgress -= HandleDragProgress;
            swipeDetector.OnDragCancel   -= HandleDragCancel;
        }

        // 안전망 — 세션 없이 위장만 남으면 카드가 영영 빈 칸으로 보인다
        if (!AlbumInsertSession.IsRunning) AlbumInsertMask.Clear();

        // 탭 전환 등으로 넘김 도중에 꺼지면 종이가 세워진 채 굳는다
        CancelFlip();

        transition.HandleDisabled(gameObject);
    }

    void OnDestroy()
    {
        pageFlip.Dispose();
    }

    void HandleChanged()
    {
        if (m_theme == null) return;

        // 넘기는 표면을 도중에 다시 Bind하면 카드가 튄다. 확정/취소 뒤 최신 상태로 한 번만 맞춘다.
        if (m_dragging || m_flipping)
        {
            m_refreshPending = true;
            return;
        }

        RefreshPage();
    }

    void BuildSlots()
    {
        m_built = true;

        if (slotRoot == null || underSlotRoot == null || slotTemplate == null)
        {
            Debug.LogError($"[AlbumPageOverlayView] 배선 누락 — slotRoot={slotRoot}, underSlotRoot={underSlotRoot}, " +
                           $"slotTemplate={slotTemplate}. 슬롯을 만들지 않는다.", this);
            return;
        }

        PrepareSlotRoot(slotRoot, true);
        PrepareSlotRoot(underSlotRoot, false);
        slotTemplate.gameObject.SetActive(false);
        underSlotRoot.gameObject.SetActive(false);
    }

    void PrepareSlotRoot(Transform _root, bool _keepTemplate)
    {
        // Destroy는 프레임 말 지연이라 먼저 꺼야 같은 프레임 레이아웃이 더미까지 읽지 않는다.
        for (int t_i = _root.childCount - 1; t_i >= 0; t_i--)
        {
            var t_child = _root.GetChild(t_i).gameObject;
            if (_keepTemplate && t_child == slotTemplate.gameObject) continue;
            t_child.SetActive(false);
            Destroy(t_child);
        }
    }

    void RefreshPage()
    {
        if (m_theme == null || m_theme.Pages.Count == 0) return;
        if (slotRoot == null || underSlotRoot == null || slotTemplate == null) return;

        m_pageIndex = Mathf.Clamp(m_pageIndex, 0, m_theme.Pages.Count - 1);
        EnsurePageCapacity(m_theme);
        BindSlots(m_slots, slotRoot, m_theme, m_pageIndex, true);
        RefreshCommittedChrome();
        m_refreshPending = false;
    }

    void EnsurePageCapacity(AlbumTheme _theme)
    {
        int t_max = Mathf.Max(0, this.pageSlotCount);   // 빈 칸 채움분까지 미리 확보한다
        for (int t_i = 0; t_i < _theme.Pages.Count; t_i++)
            t_max = Mathf.Max(t_max, _theme.Pages[t_i].Cards.Count);

        EnsureSlotCapacity(m_slots, slotRoot, t_max);
        EnsureSlotCapacity(m_underSlots, underSlotRoot, t_max);
    }

    void EnsureSlotCapacity(List<AlbumCardSlotView> _slots, Transform _root, int _count)
    {
        while (_slots.Count < _count)
            _slots.Add(Instantiate(slotTemplate, _root));
    }

    void BindSlots(List<AlbumCardSlotView> _slots, Transform _root,
                   AlbumTheme _theme, int _pageIndex, bool _interactive)
    {
        int t_pageIndex = Mathf.Clamp(_pageIndex, 0, _theme.Pages.Count - 1);
        var t_cards = _theme.Pages[t_pageIndex].Cards;

        // 저작 칸이 모자란 페이지도 격자를 다 채운다 — 채움 칸은 카드도 번호도 없는 순수 빈 포켓이다
        int t_shown = Mathf.Max(t_cards.Count, Mathf.Max(0, this.pageSlotCount));

        EnsureSlotCapacity(_slots, _root, t_shown);

        // 빈 칸에 찍는 도감 번호는 페이지가 아니라 테마 내 통번호다 — 페이지마다 1로 되돌아가면 번호가 자리를 못 가리킨다
        int t_baseNumber = 0;
        for (int t_p = 0; t_p < t_pageIndex; t_p++)
            t_baseNumber += _theme.Pages[t_p].Cards.Count;

        // 상세 목록은 확정된 current만 소유한다. under가 목록을 다시 만들면 보이는 버튼의 index가 틀어진다.
        int t_orderOffset = _interactive ? BuildOwnedOrder() : 0;
        int t_ownedInPage = 0;

        // 안내는 이 페이지의 첫 꽂힌 칸 하나만 지목한다(앵커는 키당 1건). 뒤쪽 버퍼는 눌리지 않으므로 제외한다
        bool t_anchorTaken = !_interactive;

        for (int t_i = 0; t_i < _slots.Count; t_i++)
        {
            var t_slot = _slots[t_i];
            if (t_i >= t_shown)
            {
                t_slot.ApplyTutorialAnchor(false);
                t_slot.gameObject.SetActive(false);
                continue;
            }

            // 격자 채움 칸 — 도감에 없는 자리라 번호를 찍지 않는다(0이면 번호가 숨는다)
            if (t_i >= t_cards.Count)
            {
                t_slot.ApplyTutorialAnchor(false);
                t_slot.gameObject.SetActive(true);
                t_slot.Bind(null, false, 0);
                if (t_slot.Button != null) t_slot.Button.onClick.RemoveAllListeners();
                continue;
            }

            var t_card = t_cards[t_i];
            bool t_owned = ShownAsOwned(t_card);
            t_slot.gameObject.SetActive(true);
            t_slot.Bind(t_card, t_owned, t_baseNumber + t_i + 1);

            bool t_anchor = !t_anchorTaken && t_owned;
            t_anchorTaken |= t_anchor;
            t_slot.ApplyTutorialAnchor(t_anchor);

            // 자리 소비는 버튼 유무보다 먼저다 — 미배선 칸에서 건너뛰면 이후 칸의 인덱스가 통째로 밀린다
            int t_orderIndex = t_owned ? t_orderOffset + t_ownedInPage++ : -1;

            // interactable은 건드리지 않는다 — 그 값의 주인은 AlbumCardSlotView.Bind(소유 여부)다.
            // 뒤쪽 버퍼의 입력 차단은 칸마다가 아니라 Grid_Slots_Under의 CanvasGroup이 통째로 맡는다.
            var t_button = t_slot.Button;
            if (t_button == null) continue;
            t_button.onClick.RemoveAllListeners();
            if (!_interactive || t_orderIndex < 0) continue;

            t_button.onClick.AddListener(() =>
            {
                if (IsLocked || m_dragging || m_flipping) return;
                CardDetailOverlayView.Open(m_order, t_orderIndex);
            });
        }
    }

    void RefreshCommittedChrome()
    {
        var t_page = m_theme.Pages[m_pageIndex];

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

    /// <summary>손짓 하나는 넘김 하나만 쓴다. 감지기가 어떤 이유로 스와이프를 두 번 통지해도
    /// (오래 끌다 방향을 되짚는 손짓 등) 두 장이 넘어가지 않는다 — 무장은 새 손가락이 내려앉을 때만 선다.</summary>
    void HandleSwipe(int _dir)
    {
        if (!m_dragArmed) return;
        m_dragArmed = false;   // 이 손짓은 여기서 소진된다

        if (m_flipping || IsLocked) return;

        // 손짓 도중 방향을 잠갔는데 감지기가 반대 방향으로 확정했다 = 되짚다가 반대쪽으로 넘겨버린 경우다.
        // 그대로 넘기면 화면에서 말리던 장과 다른 장이 넘어간다 — 넘기지 않고 제자리로 돌린다.
        if (m_dragDir != 0 && _dir != m_dragDir)
        {
            HandleDragCancel();
            return;
        }

        Step(m_dragDir != 0 ? m_dragDir : _dir);
    }

    /// <summary>새 손가락이 내려앉았다 — 여기서만 무장한다.</summary>
    void HandleDragBegin()
    {
        m_dragArmed = !m_flipping && !IsLocked;
        if (m_dragArmed && swipeDetector != null)
            pageFlip.SetTouchAnchor(swipeDetector.BeginPosition);
    }

    /// <summary>손가락이 끄는 만큼 종이를 세운다. 진행도는 <b>0.5(edge-on)에서 멈춘다</b> —
    /// 거기가 페이지를 교체하는 지점이라, 넘길지 말지 확정되기 전에 넘어가면 되돌릴 수 없다.
    /// 넘김 확정 임계는 감지기(snapRatio·flick)가 정한다. 여기는 손가락과 자세를 잇기만 한다.</summary>
    void HandleDragProgress(float _norm)
    {
        if (m_flipping || IsLocked) return;
        if (!m_dragArmed) return;   // 이 손짓은 이미 소비됐다(넘김으로 확정됐거나 취소됐다)

        // 가로 이동이 0이어도 위아래 손짓은 말림 기울기를 바꾼다. 따라서 _norm 조기 반환보다 먼저 갱신한다.
        if (swipeDetector != null)
            pageFlip.SetTouchAnchor(swipeDetector.CurrentPosition);

        // 진행도 0은 무장 신호가 아니다 — 끌다가 시작점을 되지나가도 0이 온다.
        // 무장은 OnDragBegin 하나만 세운다(HandleDragBegin).
        if (Mathf.Approximately(_norm, 0f)) return;

        if (m_theme == null || m_theme.Pages.Count <= 1 || pageFlip.Duration <= 0f) return;

        // 오른쪽으로 끌면 이전 장이 들어온다(감지기의 OnSwipe 부호 규약과 같다).
        // **방향은 손짓 하나에 한 번만 정한다.** 끌던 도중에 반대로 되짚어도 진영을 갈아타지 않는다 —
        // 갈아타면 한 스윕이 앞장·뒷장을 오가며 뜨는 대상(촬영대)까지 계속 바뀌어, 무엇을 넘기는 중인지 안 읽힌다.
        // 되짚는 손짓은 방향 전환이 아니라 **되감기**로 해석한다(아래 부호 있는 진행도).
        if (m_dragDir == 0)
        {
            m_dragDir = _norm > 0f ? -1 : 1;
            int t_count  = m_theme.Pages.Count;
            int t_target = (m_pageIndex + m_dragDir + t_count) % t_count;
            PrepareUnderPage(m_theme, t_target);
        }

        m_dragging = true;

        pageFlip.Begin(m_dragDir);

        // 잠근 방향으로 얼마나 갔는지. 되짚어서 시작점을 지나면 음수 → 0으로 잘려 종이가 도로 눕는다.
        // (|_norm|을 쓰면 반대로 끄는데도 계속 세워지는, 손가락과 그림이 반대로 노는 상태가 된다.)
        float t_toward = -_norm * m_dragDir;
        float t_full   = Mathf.Max(0.01f, this.dragFullRatio);
        m_dragProgress = Mathf.Clamp01(t_toward / t_full) * 0.5f;
        pageFlip.SetFlipProgress(m_dragProgress);
    }

    /// <summary>임계 미달로 손을 뗐다 — 세운 만큼만 도로 눕힌다. 아무 반응 없이 끝나면
    /// "안 먹었다"인지 "못 넘겼다"인지 구분되지 않는다.</summary>
    void HandleDragCancel()
    {
        if (!m_dragging) return;

        if (m_flipping || m_dragProgress <= 0f) { ResetDrag(); return; }

        float t_from = m_dragProgress;
        float t_dur  = Mathf.Max(0.02f, this.dragReturnDuration) * (t_from / 0.5f);

        // 복귀 트윈과 새 드래그가 같은 자세를 동시에 쓰면, 이전 ResetDrag가 새 입력까지 지운다.
        // 원위치에 닿을 때까지만 감지기와 페이지 버튼을 잠근다.
        m_dragReturning = true;
        ApplyInteractable();

        // 마무리는 OnKill 하나로 모은다 — autoKill이라 정상 완료도 여기를 지나고,
        // CancelFlip의 Kill(complete:true)로 잘려도 같은 길로 끝난다(두 번 걸면 그대로 두 번 돈다).
        DOTween.To(() => t_from, _v => { t_from = _v; pageFlip.SetFlipProgress(_v); }, 0f, t_dur)
               .SetEase(Ease.OutQuad).SetLink(gameObject).SetId(this)
               .OnKill(ResetDrag);
    }

    void ResetDrag()
    {
        m_dragging     = false;
        m_dragArmed    = false;
        m_dragReturning = false;
        m_dragProgress = 0f;
        m_dragDir      = 0;
        if (!m_flipping)
        {
            pageFlip.Cancel();
            pageFlip.ClearTouchAnchor();
            HideUnderPage();
            FlushPendingRefresh();
        }
        ApplyInteractable();
    }

    void PrepareUnderPage(AlbumTheme _theme, int _pageIndex)
    {
        if (underSlotRoot == null || _theme == null || _theme.Pages.Count == 0) return;

        int t_index = Mathf.Clamp(_pageIndex, 0, _theme.Pages.Count - 1);
        EnsurePageCapacity(_theme);   // Open 때 이미 채운다. 다른 테마로 자동 이동할 때만 여기서 늘 수 있다.

        if (m_underTheme != _theme || m_underPageIndex != t_index)
        {
            BindSlots(m_underSlots, underSlotRoot, _theme, t_index, false);
            m_underTheme     = _theme;
            m_underPageIndex = t_index;
        }

        // 순서의 기준은 넘김 뷰가 정한다 — 말림 중에는 넘기는 표면이 슬롯 뿌리가 아니라 그것을 떠 온 판이다
        pageFlip.OrderUnderBelowPage(underSlotRoot);
        underSlotRoot.gameObject.SetActive(true);
    }

    void HideUnderPage()
    {
        if (underSlotRoot != null) underSlotRoot.gameObject.SetActive(false);
        m_underTheme     = null;
        m_underPageIndex = -1;
    }

    void FlushPendingRefresh()
    {
        if (!m_refreshPending || m_dragging || m_flipping || m_theme == null) return;
        RefreshPage();
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
        pageFlip.ClearTouchAnchor();
        await FlipAsync(t_target, t_dir, _theme);
    }

    async UniTask FlipStepAsync(int _dir)
    {
        if (m_flipping) return;   // 넘기는 중 재입력은 무시 — 인덱스만 앞서가는 분기를 원천 차단한다
        if (m_theme == null || m_theme.Pages.Count == 0) return;

        // 손가락이 세워둔 자세를 이어받는다. 여기서 0부터 다시 시작하면 뗀 순간 종이가 도로 눕는다.
        float t_from = m_dragging ? m_dragProgress : 0f;
        m_dragging     = false;
        m_dragArmed    = false;   // 이 손짓은 넘김으로 소비됐다 — 남은 통지가 또 접지 못하게
        m_dragProgress = 0f;
        m_dragDir      = 0;

        // 취소 복귀가 아직 돌고 있으면 여기서 끝낸다 — 그대로 두면 넘김이 끝난 뒤 복귀 트윈이 한 번 더 접었다 편다.
        if (m_dragReturning) DOTween.Kill(this, true);

        int t_count  = m_theme.Pages.Count;
        int t_target = (m_pageIndex + _dir + t_count) % t_count;

        if (t_count <= 1 || pageFlip.Duration <= 0f)
        {
            m_pageIndex = t_target;
            RefreshPage();
            pageFlip.Cancel();
            pageFlip.ClearTouchAnchor();
            HideUnderPage();
            return;
        }

        await FlipAsync(t_target, _dir, null, t_from);
    }

    async UniTask FlipAsync(int _target, int _dir, AlbumTheme _theme, float _from = 0f)
    {
        int t_gen = ++m_flipGen;

        AlbumTheme t_targetTheme = _theme != null ? _theme : m_theme;
        PrepareUnderPage(t_targetTheme, _target);

        m_flipping = true;
        SetFlipLocked(true);
        pageFlip.Begin(_dir);

        try
        {
            // 이미 세워둔 만큼은 빼고 남은 구간만 트윈한다 — 안 그러면 손가락이 민 거리가 두 번 재생된다.
            // 접히는 한 구간이 넘김의 전부이므로 남은 몫에 duration을 통째로 배분한다(예전의 절반 배분 아님).
            float t_p     = Mathf.Clamp(_from, 0f, 0.5f);
            float t_first = pageFlip.Duration * (1f - t_p / 0.5f);

            pageFlip.SetFlipProgress(t_p);
            if (t_first > 0.001f)
                await DOTween.To(() => t_p, _v => { t_p = _v; pageFlip.SetFlipProgress(_v); }, 0.5f, t_first)
                    .SetEase(Ease.InQuad).SetLink(gameObject).SetId(this).ToUniTask();

            if (t_gen != m_flipGen) return;   // 도중에 잘렸다 — 새 페이지를 덮어쓰면 안 된다

            // edge-on(종이가 안 보이는 순간)에 교체한다. RefreshPage는 m_pageIndex의 순수 함수라
            // 연출을 전혀 몰라도 되고, 도중에 이벤트가 난입해도 화면이 어긋나지 않는다
            if (_theme != null) m_theme = _theme;
            m_pageIndex = _target;
            RefreshPage();
            pageFlip.EnsureShadeOnTop();   // 슬롯이 새로 생겼으면 그늘이 카드 뒤로 묻힌다

            // 뒷장을 미리 깔아두는 구조에서는 여기서 끝이다. 예전처럼 0.5→1로 **펴는** 구간을 더 돌리면
            // 이미 드러나 있는 뒷장 위로 같은 페이지가 한 번 더 접혔다 펴진다 — 넘김이 두 번 보이던 원인이다.
            // 종이는 접혀 사라진 자리에서 곧바로 눕히고, 아래에서 기다리던 장이 그 자리를 잇는다.
            pageFlip.Cancel();
            HideUnderPage();

            // 자세는 이미 평평하지만 게이지·페이지 번호는 접히는 동안 걷혀 있었다 — 글자만 짧게 되돌린다.
            // 여기서 안 되돌리면 새 번호가 한 프레임에 툭 튀어나온다.
            float t_side = 0f;
            pageFlip.SetSideAlpha(0f);
            await DOTween.To(() => t_side, _v => { t_side = _v; pageFlip.SetSideAlpha(_v); }, 1f, pageFlip.Crossfade)
                .SetEase(Ease.OutQuad).SetLink(gameObject).SetId(this).ToUniTask();
        }
        finally
        {
            if (t_gen == m_flipGen)
            {
                pageFlip.Cancel();
                pageFlip.ClearTouchAnchor();
                HideUnderPage();
                m_flipping = false;
                SetFlipLocked(false);
                FlushPendingRefresh();
            }
        }
    }

    void CancelFlip()
    {
        m_flipGen++;                 // 진행 중이던 넘김의 커밋·정리를 무효화한다
        m_dragging     = false;      // 손가락이 세워둔 자세도 여기서 함께 버린다
        m_dragArmed    = false;
        m_dragReturning = false;
        m_dragProgress = 0f;
        m_dragDir      = 0;
        DOTween.Kill(this, true);    // SetId(this)를 단 넘김 트윈만. complete=true라야 대기가 취소가 아닌 완료로 풀린다
        pageFlip.Cancel();
        pageFlip.ClearTouchAnchor();
        HideUnderPage();
        m_flipping = false;
        SetFlipLocked(false);
    }

    void ClaimPageReward()
    {
        if (m_theme == null || m_theme.Pages.Count == 0) return;

        var t_page = m_theme.Pages[Mathf.Clamp(m_pageIndex, 0, m_theme.Pages.Count - 1)];

        // 팝업을 띄우기 전에 막는다 — 지급은 [획득]에서 일어나므로 여기서 걸러야 못 받을 보상이 축하받지 않는다.
        if (!AlbumRewardManager.CanClaimPage(t_page)) return;

        AlbumRewardClaimFlow.Open($"{m_theme.DisplayName} {t_page.Index + 1}페이지 완성!",
                                  t_page.Rewards,
                                  () => AlbumRewardManager.ClaimPage(t_page));
    }
}
