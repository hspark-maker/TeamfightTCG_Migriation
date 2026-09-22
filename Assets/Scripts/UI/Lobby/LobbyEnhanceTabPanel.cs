using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>보유 카드 선택과 샤드 투입 UI. 성장·재화 확정은 기존 서버 명령이 맡는다.</summary>
public sealed class LobbyEnhanceTabPanel : LobbyTabPanel
{
    [SerializeField] DeckEditCollectionGrid collection;
    [SerializeField] CardVisualView cardView;
    [SerializeField] CardVisualView summaryView;
    [SerializeField] GameObject selectedCardRoot;
    [SerializeField] TMP_Text selectionHint;
    [SerializeField] TMP_Text descriptionText;
    [SerializeField] Image[] stars;
    [SerializeField] Sprite filledStar;
    [SerializeField] Sprite emptyStar;
    [SerializeField] RectTransform progressFill;
    [SerializeField] TMP_Text progressText;
    [Tooltip("선택 사항. 연결하지 않으면 상태 안내 문구를 표시하지 않습니다.")]
    [SerializeField] TMP_Text statusText;
    [SerializeField] Button filterButton;
    [SerializeField] Image filterIndicator;
    [SerializeField] Button acquisitionButton;
    [SerializeField] CardAcquisitionView acquisitionView;
    [SerializeField] Button enhanceButton;
    [SerializeField] Button decreaseButton;
    [SerializeField] Button increaseButton;
    [SerializeField] TMP_Text amountText;
    [SerializeField] TMP_Text costText;
    [SerializeField] Image costIcon;
    [SerializeField] TMP_Text actionText;

    CardListFilter m_filter = new CardListFilter();
    int m_card;
    int m_amount = 10;
    int m_maxAmount;
    bool m_pending;
    bool m_resetScroll;
    LobbyTabController m_shell;
    static LobbyEnhanceTabPanel s_visible;

    public static bool IsBusy => s_visible != null
        && (s_visible.m_pending || s_visible.acquisitionView.IsOpen);

    public override void Initialize(LobbyTabServices _services) => m_shell = _services.Shell;

    protected override void OnInitializeUI()
    {
        filterButton.onClick.AddListener(OpenFilter);
        acquisitionButton.onClick.AddListener(OpenAcquisition);
        enhanceButton.onClick.AddListener(OnEnhancePressed);
        decreaseButton.onClick.AddListener(() => ChangeAmount(false));
        increaseButton.onClick.AddListener(() => ChangeAmount(true));
        acquisitionView.gameObject.SetActive(false);
    }

    protected override void OnViewShown()
    {
        s_visible = this;
        m_resetScroll = true;
        OwnershipManager.OnOwnershipChanged += Rebuild;
        CardGrowthManager.OnGrowthChanged += Rebuild;
        CurrencyManager.OnCurrencyChanged += OnCurrencyChanged;
        OutgameFeatureLock.OnChanged += RefreshActions;
        Rebuild();
        base.OnViewShown();
    }

    protected override void OnViewHidden()
    {
        if (s_visible == this) s_visible = null;
        OwnershipManager.OnOwnershipChanged -= Rebuild;
        CardGrowthManager.OnGrowthChanged -= Rebuild;
        CurrencyManager.OnCurrencyChanged -= OnCurrencyChanged;
        OutgameFeatureLock.OnChanged -= RefreshActions;
        CardFilterPopup.CloseFor(this);
        acquisitionView.Hide();
        base.OnViewHidden();
    }

    public override void RequestLeave(Action _proceed)
    {
        if (!m_pending) _proceed?.Invoke();
    }

    bool CanInteract => IsViewVisible && !m_pending
        && GuidanceCoordinator.AllowsUserAction(EOutgameTutorialAnchor.None);

    void Rebuild()
    {
        if (!IsViewVisible || m_pending) return;
        if (!CardCatalog.IsReady || !CardGrowthManager.IsReady)
        {
            m_card = 0;
            collection.Clear();
            RefreshSelection();
            SetStatusText("카드 정보를 불러오는 중입니다.");
            return;
        }
        int next = m_card;
        if (!OwnershipManager.IsOwned(next) || !m_filter.Matches(next)) next = 0;
        if (next == 0)
            foreach (int card in CardCatalog.AllIds)
                if (OwnershipManager.IsOwned(card) && m_filter.Matches(card)) { next = card; break; }
        if (next != m_card) m_amount = 10;
        m_card = next;
        float position = m_resetScroll ? 1f : collection.Scroll.verticalNormalizedPosition;
        collection.Build(null, OnCardSelected);
        collection.SetCardFilter(m_filter);
        collection.SetPickedCard(m_card);
        collection.Scroll.verticalNormalizedPosition = position;
        m_resetScroll = false;
        RefreshSelection();
    }

    void OnCardSelected(DeckEditCardTile _tile)
    {
        if (!CanInteract || _tile.Card == m_card) return;
        m_card = _tile.Card;
        m_amount = 10;
        collection.SetPickedCard(m_card);
        RefreshSelection();
    }

    void RefreshSelection()
    {
        bool selected = m_card > 0;
        selectedCardRoot.SetActive(selected);
        selectionHint.gameObject.SetActive(!selected);
        selectionHint.text = m_filter.IsActive ? "필터 조건을 변경해 주세요." : "보유 카드를 선택해 주세요.";
        if (selected)
        {
            cardView.Bind(m_card, true);
            summaryView.Bind(m_card, true);
            descriptionText.text = CardCatalog.RequireSpec(m_card).CardExplain;
            int starCount = GrowthStar.FromLevel(CardGrowthManager.LevelOf(m_card));
            for (int i = 0; i < stars.Length; i++)
            {
                stars[i].gameObject.SetActive(true);
                stars[i].sprite = i < starCount ? filledStar : emptyStar;
            }
            int required = CardGrowthManager.ShardRequiredOf(m_card);
            int progress = CardGrowthManager.ShardProgressOf(m_card);
            bool max = CardGrowthManager.LevelOf(m_card) >= CardGrowthManager.MaxLevel;
            progressFill.anchorMax = new Vector2(max ? 1f : required > 0 ? Mathf.Clamp01((float)progress / required) : 0f, 1f);
            progressText.text = max ? "최대 성장" : $"진화 진행 {progress:N0} / {required:N0}";
        }
        RefreshActions();
    }

    void OnCurrencyChanged(ECurrencyType _currency, long _balance) => RefreshActions();

    void RefreshActions()
    {
        GrowthStep step = default;
        bool hasStep = m_card > 0 && CardGrowthManager.IsReady
            && CardGrowthManager.TryGetNextStep(m_card, out step);
        // 낙관 차감 알림이 현재 요청의 수량·비용 표시를 바꾸지 않게 한다.
        if (!m_pending)
        {
            long available = hasStep && step.Cost > 0 ? CurrencyManager.GetBalance(step.Currency) / step.Cost : long.MaxValue;
            int remaining = hasStep ? CardGrowthManager.ShardRequiredOf(m_card) - CardGrowthManager.ShardProgressOf(m_card) : 0;
            m_maxAmount = (int)Math.Max(0, Math.Min(150, Math.Min(remaining, available)));
            m_amount = Mathf.Clamp(m_amount, 1, Mathf.Max(1, m_maxAmount));
            amountText.text = m_maxAmount > 0 ? m_amount.ToString("N0") : "0";
            costText.text = !hasStep ? "—" : step.Cost == 0 ? "무료" : (step.Cost * m_amount).ToString("N0");
            costIcon.sprite = hasStep ? CurrencyLook.IconOf(step.Currency) : null;
            costIcon.enabled = hasStep && step.Cost > 0 && costIcon.sprite != null;
        }
        bool unlocked = OutgameFeatureLock.IsUnlocked(EOutgameFeature.CardEnhance);
        bool canEnhance = CanInteract && unlocked && hasStep && m_maxAmount > 0
            && OwnershipManager.IsOwned(m_card) && CardGrowthManager.Precheck(m_card) == EEnhanceOutcome.Success;
        enhanceButton.interactable = canEnhance;
        decreaseButton.interactable = canEnhance && m_amount > 1;
        increaseButton.interactable = canEnhance && m_amount < m_maxAmount;
        filterButton.interactable = CanInteract;
        acquisitionButton.interactable = CanInteract && m_card > 0;
        actionText.text = m_pending ? "강화 중" : m_card > 0 && !hasStep && CardGrowthManager.IsReady ? "완료" : "강화";
        SetStatusText(m_pending ? "강화 결과를 기다리는 중입니다."
            : m_card <= 0 ? "" : !unlocked ? "카드 강화가 아직 열리지 않았습니다."
            : !hasStep ? "최대 성장에 도달했습니다."
            : m_maxAmount <= 0 ? "강화에 필요한 샤드가 부족합니다."
            : "샤드를 채우면 자동으로 진화합니다.");
    }

    void SetStatusText(string _text)
    {
        if (statusText != null) statusText.text = _text;
    }

    void ChangeAmount(bool _increase)
    {
        if (!CanInteract || m_maxAmount <= 0) return;
        int next = _increase ? (m_amount < 5 ? m_amount + 1 : m_amount < 10 ? 10 : m_amount + 10)
            : (m_amount <= 5 ? m_amount - 1 : m_amount <= 10 ? 5 : ((m_amount - 1) / 10) * 10);
        m_amount = Mathf.Clamp(next, 1, m_maxAmount);
        RefreshActions();
    }

    void OpenFilter()
    {
        if (!CanInteract) return;
        CardFilterPopup.Open(m_filter, false, null, filter =>
        {
            if (this == null || !CanInteract) return;
            m_filter = filter.Clone();
            filterIndicator.color = m_filter.IsActive ? new Color(1f, .75f, .2f) : Color.white;
            Rebuild();
            collection.Scroll.verticalNormalizedPosition = 1f;
        }, this, OwnershipManager.OwnedIds);
    }

    void OpenAcquisition()
    {
        if (CanInteract && m_card > 0) acquisitionView.Show(m_card, NavigateToPack, null);
    }

    void NavigateToPack(string _packId)
    {
        if (!CanInteract || !GuidanceCoordinator.AllowsUserNavigation(EOutgameTutorialAnchor.LobbyPackTab)
            || !OutgameFeatureLock.IsUnlocked(EOutgameFeature.PackCarousel)
            || OutgameTutorialRunner.TryGetForcedPack(out _, out _) || PackPurchaseFlow.IsPurchasing) return;
        bool available = false;
        foreach (var source in CardAcquisitionSources.Resolve(m_card))
            if (source.PackId == _packId && source.Available) { available = true; break; }
        var shell = m_shell;
        if (!available || shell == null || !shell.TrySelectFeature(EOutgameFeature.LobbyPackTab, _afterSelect: () =>
        {
            var packs = shell.CurrentPanel?.GetComponentInChildren<PackShowcaseController>(true);
            if (packs != null && packs.TrySelectPack(_packId)) acquisitionView.Hide();
            else acquisitionView.ShowNavigationUnavailable();
        })) acquisitionView.ShowNavigationUnavailable();
    }

    void OnEnhancePressed()
    {
        if (!CanInteract) return;
        RefreshActions();
        if (!enhanceButton.interactable) return;
        m_pending = true; // 첫 await와 지갑 예약 알림보다 먼저 중복 입력을 막는다.
        RefreshActions();
        EnhanceAsync(m_card, m_amount, VisibilityVersion).Forget();
    }

    async UniTaskVoid EnhanceAsync(int _card, int _amount, int _version)
    {
        int fromLevel = CardGrowthManager.LevelOf(_card);
        EnhanceResult result;
        try
        {
            ServerWaitOverlay.Hold(this);
            result = await CardGrowthManager.TryEnhanceAsync(_card, _amount);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            result = new EnhanceResult(EEnhanceOutcome.NotReady, CardGrowthManager.LevelOf(_card));
        }
        finally
        {
            ServerWaitOverlay.Release(this);
            m_pending = false;
        }
        if (this == null || !IsViewVisible) return;
        Rebuild();
        if (VisibilityVersion != _version || m_card != _card) return;
        switch (result.Outcome)
        {
            case EEnhanceOutcome.Success:
                cardView.FlashGrowth();
                summaryView.FlashGrowth();
                SetStatusText(result.Level > fromLevel ? "진화했습니다!" : $"샤드 {result.AppliedShards:N0}개를 적용했습니다.");
                break;
            case EEnhanceOutcome.NotAffordable: SetStatusText("샤드가 부족합니다. 수량을 확인해 주세요."); break;
            case EEnhanceOutcome.MaxLevel: SetStatusText("최대 성장에 도달했습니다."); break;
            default: SetStatusText("강화를 완료하지 못했습니다. 잠시 후 다시 시도해 주세요."); break;
        }
    }
}
