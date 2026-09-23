using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>보유 카드 강화와 미보유 카드 제작 UI. 소유·성장·재화 확정은 서버 명령이 맡는다.</summary>
public sealed class LobbyEnhanceTabPanel : LobbyTabPanel
{
    [Serializable]
    sealed class AbilityDescriptionRow
    {
        public Image background;
        public Image icon;
        public TMP_Text title;
        public TMP_Text description;
        public GameObject lockIcon;
        public SectionUnlockFx unlockFx;
        public SectionRevealFx revealFx;
    }

    [SerializeField] KeywordIconConfig abilityKeywordConfig;
    [SerializeField] AbilityDescriptionRow keywordDescription;
    [SerializeField] AbilityDescriptionRow[] synergyDescriptions;
    [SerializeField] DeckEditCollectionGrid collection;
    [SerializeField] CardVisualView cardView;
    [SerializeField] CardVisualView summaryView;
    [SerializeField] RectTransform tutorialKeyword;
    [SerializeField] RectTransform tutorialSynergy;
    [SerializeField] GameObject selectedCardRoot;
    [SerializeField] TMP_Text selectionHint;
    [SerializeField] TMP_Text descriptionText;
    [SerializeField] Image[] stars;
    [SerializeField] Sprite filledStar;
    [SerializeField] Sprite emptyStar;
    [SerializeField] RectTransform progressFill;
    [SerializeField] RectTransform progressPreviewFill;
    [SerializeField] TMP_Text progressText;
    [SerializeField] TMP_Text hpPreviewText;
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
    [SerializeField] Toggle showUnownedToggle;
    [SerializeField] TMP_Text panelTitle;
    [SerializeField] GameObject growthProgressRoot;
    [SerializeField] GameObject growthStarsRoot;
    [SerializeField] GameObject shardAmountRoot;
    [SerializeField] TMP_Text craftMessage;
    [SerializeField] TMP_Text craftBalanceText;

    [Header("강화 연출")]
    [SerializeField] GameObject presentationRoot;
    [SerializeField] RectTransform presentationMotion;
    [SerializeField] CardVisualView presentationCard;
    [SerializeField] CardEnhanceRitualView enhanceRitual;
    [SerializeField] CardEvolveRitualView evolveRitual;
    [SerializeField] EnhanceResultPanelView resultPanel;

    CardListFilter m_filter = new CardListFilter();
    int m_card;
    int m_amount = 10;
    int m_maxAmount;
    bool m_pending;
    bool m_crafting;
    bool m_showUnowned;
    bool m_resetScroll;
    LobbyTabController m_shell;
    static LobbyEnhanceTabPanel s_visible;
    static LobbyEnhanceTabPanel s_instance;
    Sequence m_presentationMove;
    bool m_presenting;
    bool m_sourceHidden;
    Color[] m_starColors;
    static readonly Color PreviewStarColor = new Color(.62f, .78f, .79f, .8f);
    readonly List<Graphic> m_emblemBuffer = new List<Graphic>();
    readonly List<Tween> m_unlockTweens = new List<Tween>();
    CardKeyword m_unlockKeywords;
    bool m_unlockSynergy;
    UnlockIntroOverlay m_unlockIntro;

    public static event Action OnEnhanceStarted;
    public static event Action<EnhanceResult> OnEnhanceResultReady;
    public static event Action<EnhanceResult> OnEnhanceSettled;
    public static bool IsOpen => s_visible != null && s_visible.IsViewVisible;
    public static bool IsPresenting => s_visible != null && s_visible.m_pending;

    public static bool IsBusy => s_visible != null
        && (s_visible.m_pending || s_visible.acquisitionView.IsOpen);

    public override void Initialize(LobbyTabServices _services)
    {
        m_shell = _services.Shell;
        s_instance = this;
    }

    public static bool CanOpenForCard(int _card, bool _allowUnowned = false)
        => s_instance != null && s_instance.m_shell != null && s_instance.m_shell.isActiveAndEnabled
            && CardCatalog.Contains(_card) && (_allowUnowned || OwnershipManager.IsOwned(_card))
            && OutgameFeatureLock.IsUnlocked(EOutgameFeature.CardEnhance);

    public static bool TryOpenForCard(int _card, Action _onOpened = null, bool _allowUnowned = false)
    {
        var panel = s_instance;
        if (!CanOpenForCard(_card, _allowUnowned)) return false;
        if (panel.m_pending)
        {
            if (!panel.IsViewVisible || panel.m_card != _card) return false;
            _onOpened?.Invoke();
            return true;
        }
        return panel.m_shell.TrySelectFeature(EOutgameFeature.CardEnhance, _afterSelect: () =>
        {
            panel.m_filter = new CardListFilter();
            panel.filterIndicator.color = Color.white;
            panel.m_card = _card;
            panel.m_amount = 10;
            if (!OwnershipManager.IsOwned(_card))
            {
                panel.m_showUnowned = true;
                panel.showUnownedToggle.SetIsOnWithoutNotify(true);
            }
            panel.Rebuild();
            if (!OwnershipManager.IsOwned(_card)) panel.RefreshCraftCatalogAsync().Forget();
            _onOpened?.Invoke();
        });
    }

    protected override void OnInitializeUI()
    {
        m_starColors = new Color[stars.Length];
        for (int i = 0; i < stars.Length; i++) m_starColors[i] = stars[i].color;
        filterButton.onClick.AddListener(OpenFilter);
        acquisitionButton.onClick.AddListener(OpenAcquisition);
        enhanceButton.onClick.AddListener(OnEnhancePressed);
        decreaseButton.onClick.AddListener(() => ChangeAmount(false));
        increaseButton.onClick.AddListener(() => ChangeAmount(true));
        showUnownedToggle.onValueChanged.AddListener(OnShowUnownedChanged);
        m_showUnowned = showUnownedToggle.isOn;
        acquisitionView.gameObject.SetActive(false);
        if (presentationRoot != null) presentationRoot.SetActive(false);
        resultPanel?.HideImmediate();
    }

    protected override void OnViewShown()
    {
        s_visible = this;
        RegisterGrowthAnchors();
        m_resetScroll = true;
        OwnershipManager.OnOwnershipChanged += Rebuild;
        CardGrowthManager.OnGrowthChanged += Rebuild;
        CurrencyManager.OnCurrencyChanged += OnCurrencyChanged;
        OutgameFeatureLock.OnChanged += RefreshActions;
        CardCraftCommands.OnChanged += RefreshActions;
        Rebuild();
        if (m_showUnowned) RefreshCraftCatalogAsync().Forget();
        base.OnViewShown();
    }

    protected override void OnViewHidden()
    {
        CancelUnlockPresentation();
        CancelPresentation();
        TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.LobbyEnhanceButton, enhanceButton.transform as RectTransform);
        TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.LobbyEnhanceShardIcon, costIcon.rectTransform);
        TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.LobbyEnhanceShardAmount, amountText.rectTransform);
        TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.LobbyEnhanceCardView, cardView.transform as RectTransform);
        TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.LobbyEnhanceKeywordDescription);
        TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.LobbyEnhanceSynergyDescription);
        if (s_visible == this) s_visible = null;
        OwnershipManager.OnOwnershipChanged -= Rebuild;
        CardGrowthManager.OnGrowthChanged -= Rebuild;
        CurrencyManager.OnCurrencyChanged -= OnCurrencyChanged;
        OutgameFeatureLock.OnChanged -= RefreshActions;
        CardCraftCommands.OnChanged -= RefreshActions;
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

    bool CanEnhance => IsViewVisible && !m_pending
        && GuidanceCoordinator.AllowsUserAction(EOutgameTutorialAnchor.LobbyEnhanceButton);

    void RegisterGrowthAnchors()
    {
        TutorialAnchorRegistry.Register(EOutgameTutorialAnchor.LobbyEnhanceButton, enhanceButton.transform as RectTransform, enhanceButton);
        TutorialAnchorRegistry.Register(EOutgameTutorialAnchor.LobbyEnhanceShardIcon, costIcon.rectTransform, null);
        TutorialAnchorRegistry.Register(EOutgameTutorialAnchor.LobbyEnhanceShardAmount, amountText.rectTransform, null);
        TutorialAnchorRegistry.Register(EOutgameTutorialAnchor.LobbyEnhanceCardView, cardView.transform as RectTransform, null);
        TutorialAnchorRegistry.Register(EOutgameTutorialAnchor.LobbyEnhanceKeywordDescription, GrowthDescriptionAnchor(tutorialKeyword), null);
        TutorialAnchorRegistry.Register(EOutgameTutorialAnchor.LobbyEnhanceSynergyDescription, GrowthDescriptionAnchor(tutorialSynergy), null);
    }

    RectTransform GrowthDescriptionAnchor(RectTransform _icon)
        => _icon != null && _icon.gameObject.activeInHierarchy ? _icon : summaryView.transform as RectTransform;

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
        if (!CardCatalog.Contains(next) || (!m_showUnowned && !OwnershipManager.IsOwned(next)) || !m_filter.Matches(next)) next = 0;
        if (next == 0)
            foreach (int card in CardCatalog.AllIds)
                if ((m_showUnowned || OwnershipManager.IsOwned(card)) && m_filter.Matches(card)) { next = card; break; }
        if (next != m_card) m_amount = 10;
        m_card = next;
        float position = m_resetScroll ? 1f : collection.Scroll.verticalNormalizedPosition;
        collection.Build(null, OnCardSelected, m_showUnowned);
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
        selectionHint.text = m_filter.IsActive ? "필터 조건을 변경해 주세요." : m_showUnowned ? "카드를 선택해 주세요." : "보유 카드를 선택해 주세요.";
        if (selected)
        {
            cardView.Bind(m_card, OwnershipManager.IsOwned(m_card));
            if (m_sourceHidden) cardView.gameObject.SetActive(false);
            summaryView.Bind(m_card, true);
            descriptionText.text = CardCatalog.RequireSpec(m_card).CardExplain;
            RefreshAbilityDescriptions(CardGrowthManager.GrowthOf(m_card));
            if (m_pending) RefreshGrowthPreview(0);
        }
        RefreshActions();
        RegisterGrowthAnchors();
    }

    void OnCurrencyChanged(ECurrencyType _currency, long _balance) => RefreshActions();

    void RefreshActions()
    {
        bool crafting = m_crafting || (m_card > 0 && !OwnershipManager.IsOwned(m_card));
        panelTitle.text = crafting ? "카드 제작" : "카드 강화";
        growthProgressRoot.SetActive(!crafting);
        growthStarsRoot.SetActive(!crafting);
        shardAmountRoot.SetActive(!crafting);
        craftMessage.gameObject.SetActive(crafting);
        craftBalanceText.gameObject.SetActive(crafting);
        showUnownedToggle.interactable = CanInteract;
        if (crafting)
        {
            bool available = CardCraftCommands.TryGetRecipe(m_card, out CardCraftRecipe recipe) && recipe.Available;
            RefreshCraftActions(CardCraftCommands.IsReady, CardCraftCommands.IsRefreshing, available, recipe);
            return;
        }
        GrowthStep step = default;
        bool hasStep = m_card > 0 && CardGrowthManager.IsReady
            && CardGrowthManager.TryGetNextStep(m_card, out step);
        // 낙관 차감 알림이 현재 요청의 수량·비용 표시를 바꾸지 않게 한다.
        if (!m_pending)
        {
            long available = hasStep && step.Cost > 0 ? CurrencyManager.GetBalance(step.Currency) / step.Cost : long.MaxValue;
            int remaining = hasStep ? CardGrowthManager.ShardRequiredOf(m_card) - CardGrowthManager.ShardProgressOf(m_card) : 0;
            m_maxAmount = (int)Math.Max(0, Math.Min(150, Math.Min(remaining, available)));
            m_amount = OutgameTutorialGuide.HasFreeShot(EOutgameTutorialAction.WaitEnhance)
                ? Mathf.Max(1, m_maxAmount) : Mathf.Clamp(m_amount, 1, Mathf.Max(1, m_maxAmount));
            amountText.text = m_maxAmount > 0 ? m_amount.ToString("N0") : "0";
            costText.text = !hasStep ? "—" : step.Cost == 0 ? "무료" : (step.Cost * m_amount).ToString("N0");
            costIcon.sprite = hasStep ? CurrencyLook.IconOf(step.Currency) : null;
            costIcon.enabled = hasStep && costIcon.sprite != null;
        }
        bool unlocked = OutgameFeatureLock.IsUnlocked(EOutgameFeature.CardEnhance);
        bool canEnhance = CanEnhance && unlocked && hasStep && m_maxAmount > 0
            && OwnershipManager.IsOwned(m_card) && CardGrowthManager.Precheck(m_card) == EEnhanceOutcome.Success;
        enhanceButton.interactable = canEnhance;
        bool canSelectAmount = canEnhance && !OutgameTutorialGuide.HasFreeShot(EOutgameTutorialAction.WaitEnhance);
        decreaseButton.interactable = canSelectAmount && m_amount > 1;
        increaseButton.interactable = canSelectAmount && m_amount < m_maxAmount;
        filterButton.interactable = CanInteract;
        acquisitionButton.interactable = CanInteract && m_card > 0;
        actionText.text = m_pending ? "강화 중" : m_card > 0 && !hasStep && CardGrowthManager.IsReady ? "완료" : "강화";
        if (!m_pending) RefreshGrowthPreview(hasStep && unlocked && m_maxAmount > 0 ? m_amount : 0);
        SetStatusText(m_pending ? "강화 결과를 기다리는 중입니다."
            : m_card <= 0 ? "" : !unlocked ? "카드 강화가 아직 열리지 않았습니다."
            : !hasStep ? "최대 성장에 도달했습니다."
            : m_maxAmount <= 0 ? "강화에 필요한 샤드가 부족합니다."
            : "샤드를 채우면 자동으로 진화합니다.");
    }

    void RefreshGrowthPreview(int _amount)
    {
        if (m_card <= 0 || !CardGrowthManager.IsReady) return;
        CardGrowth current = CardGrowthManager.GrowthOf(m_card);
        CardGrowth preview = CardGrowthManager.PreviewGrowthAfterEnhance(m_card, _amount);
        int currentStars = GrowthStar.FromLevel(current.Level);
        int previewStars = GrowthStar.FromLevel(preview.Level);
        for (int i = 0; i < stars.Length; i++)
        {
            stars[i].gameObject.SetActive(true);
            stars[i].sprite = i < previewStars ? filledStar : emptyStar;
            stars[i].color = i >= currentStars && i < previewStars ? PreviewStarColor : m_starColors[i];
        }

        int hp = DeckPower.MaxHpOf(m_card);
        int gain = preview.HpBonus - current.HpBonus;
        hpPreviewText.text = gain > 0
            ? $"{hp:N0} <color=#73B84D>→ {hp + gain:N0}\n<size=75%>(+{gain:N0})</size></color>"
            : hp.ToString("N0");

        int required = CardGrowthManager.ShardRequiredOf(m_card);
        int progress = CardGrowthManager.ShardProgressOf(m_card);
        bool max = current.Level >= CardGrowthManager.MaxLevel;
        bool evolves = preview.Level > current.Level;
        int afterProgress = evolves ? required : Math.Min(required, progress + Math.Max(0, _amount));
        progressFill.anchorMax = new Vector2(max ? 1f : required > 0 ? (float)progress / required : 0f, 1f);
        progressPreviewFill.gameObject.SetActive(!max && afterProgress > progress);
        progressPreviewFill.anchorMax = new Vector2(required > 0 ? (float)afterProgress / required : 0f, 1f);
        progressText.text = max ? "최대 성장" : afterProgress > progress
            ? $"진화 진행 {progress:N0} <color=#D8B56C>→ {afterProgress:N0}</color> / {required:N0}"
            : $"진화 진행 {progress:N0} / {required:N0}";
        if (!m_pending && evolves) actionText.text = "진화";
        RefreshAbilityDescriptions(preview);
    }

    void RefreshAbilityDescriptions(CardGrowth _preview)
    {
        bool owned = OwnershipManager.IsOwned(m_card);
        CardKeyword all = CardVisualRules.InfoKeywordsWithLocked(m_card);
        CardKeyword locked = CardVisualRules.LockedKeywords(m_card);
        var names = new List<string>();
        var explanations = new List<string>();
        Sprite icon = null;
        foreach (CardKeyword keyword in Enum.GetValues(typeof(CardKeyword)))
        {
            if (keyword == CardKeyword.None || (all & keyword) == 0
                || !abilityKeywordConfig.TryGetEntry(keyword, out var entry)) continue;
            if (icon == null) icon = entry.icon;
            names.Add(entry.displayName);
            explanations.Add(entry.explain);
        }
        bool hasKeywords = names.Count > 0;
        BindAbilityRow(keywordDescription, hasKeywords ? icon : abilityKeywordConfig.DefaultIcon,
            hasKeywords ? string.Join(" · ", names) : "일반",
            hasKeywords ? string.Join("\n", explanations) : "키워드 없음",
            hasKeywords && (!owned || locked != CardKeyword.None || m_unlockKeywords != CardKeyword.None),
            hasKeywords && owned && (locked & _preview.UnlockedKeywords) != CardKeyword.None);

        var synergies = CardCatalog.RequireSynergies(m_card);
        bool synergyLocked = !owned || !CardGrowthManager.GrowthOf(m_card).SynergyUnlocked;
        int used = 0;
        var seen = new HashSet<SynergyData>();
        foreach (var synergy in synergies)
        {
            if (synergy == null || !seen.Add(synergy)) continue;
            if (used >= synergyDescriptions.Length) break;
            string requirement = SynergyText.Requirement(synergy);
            string title = string.IsNullOrEmpty(requirement) ? SynergyText.Name(synergy)
                : $"{SynergyText.Name(synergy)}  {requirement}";
            string effect = SynergyText.Effect(synergy);
            BindAbilityRow(synergyDescriptions[used++], synergy.activeIcon, title,
                string.IsNullOrEmpty(effect) ? SynergyText.Body(synergy) : effect,
                synergyLocked || m_unlockSynergy, owned && synergyLocked && _preview.SynergyUnlocked);
        }
        for (; used < synergyDescriptions.Length; used++)
            BindAbilityRow(synergyDescriptions[used], null, null, null, false, false);
    }

    static void BindAbilityRow(AbilityDescriptionRow _row, Sprite _icon, string _title,
        string _description, bool _locked, bool _willUnlock)
    {
        _row.background.gameObject.SetActive(_title != null);
        if (_title == null) return;
        _row.background.color = _willUnlock ? new Color(.84f, .92f, .93f)
            : _locked ? new Color(.88f, .86f, .83f) : new Color(1f, .97f, .91f);
        _row.icon.sprite = _icon;
        _row.icon.enabled = _icon != null;
        _row.icon.color = _locked && !_willUnlock ? new Color(.6f, .6f, .6f) : Color.white;
        _row.title.text = _title;
        _row.description.text = _description;
        _row.lockIcon.SetActive(_locked);
    }

    void SetStatusText(string _text)
    {
        if (statusText != null) statusText.text = _text;
    }

    void ChangeAmount(bool _increase)
    {
        if (!CanInteract || !OwnershipManager.IsOwned(m_card) || m_maxAmount <= 0 || OutgameTutorialGuide.HasFreeShot(EOutgameTutorialAction.WaitEnhance)) return;
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
        }, this, m_showUnowned ? (IEnumerable<int>)CardCatalog.AllIds : OwnershipManager.OwnedIds);
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
        if (!CanEnhance) return;
        RefreshActions();
        if (!enhanceButton.interactable) return;
        if (!OwnershipManager.IsOwned(m_card))
        {
            if (!CardCraftCommands.IsReady)
            {
                RefreshCraftCatalogAsync(true).Forget();
                return;
            }
            m_pending = true;
            m_crafting = true;
            if (presentationCard != null) presentationCard.Bind(m_card, false);
            RefreshActions();
            CraftAsync(m_card, VisibilityVersion).Forget();
            return;
        }
        m_pending = true; // 첫 await와 지갑 예약 알림보다 먼저 중복 입력을 막는다.
        RefreshGrowthPreview(0);
        // 서버가 성장 캐시를 바꾸기 전에 연출 카드에 이전 모습을 남긴다.
        if (presentationCard != null) presentationCard.Bind(m_card, true);
        RefreshActions();
        EnhanceAsync(m_card, m_amount, VisibilityVersion).Forget();
    }

    void OnShowUnownedChanged(bool _show)
    {
        if (!CanInteract)
        {
            showUnownedToggle.SetIsOnWithoutNotify(m_showUnowned);
            return;
        }
        m_showUnowned = _show;
        m_resetScroll = true;
        Rebuild();
        if (_show) RefreshCraftCatalogAsync().Forget();
    }

    async UniTask RefreshCraftCatalogAsync(bool _force = false)
    {
        await CardCraftCommands.RefreshAsync(_force);
        if (this != null && IsViewVisible) RefreshActions();
    }

    void RefreshCraftActions(bool ready, bool loading, bool available, CardCraftRecipe recipe)
    {
        bool affordable = available && CurrencyManager.CanAfford(recipe.Currency, recipe.Cost);
        bool unlocked = OutgameFeatureLock.IsUnlocked(EOutgameFeature.CardEnhance);
        var currency = available ? recipe.Currency : ECurrencyType.CardDust;
        craftBalanceText.text = $"{CurrencyLook.NameOf(currency)}\n보유 {CurrencyManager.GetBalance(currency):N0}";
        costIcon.sprite = available ? CurrencyLook.IconOf(currency) : null;
        costIcon.enabled = costIcon.sprite != null;
        costText.text = available ? recipe.Cost.ToString("N0") : "—";
        actionText.text = m_pending ? "제작 중" : loading ? "불러오는 중" : !ready ? "다시 불러오기" : "제작";
        enhanceButton.interactable = CanEnhance && unlocked && !loading && !CardCraftCommands.IsCrafting
            && (!ready || (available && affordable));
        decreaseButton.interactable = false;
        increaseButton.interactable = false;
        filterButton.interactable = CanInteract;
        acquisitionButton.interactable = CanInteract && m_card > 0;
        craftMessage.text = m_pending ? "카드를 제작하는 중입니다."
            : !unlocked ? "카드 강화가 아직 열리지 않았습니다."
            : loading ? "제작 정보를 불러오는 중입니다."
            : !ready ? "제작 정보를 불러오지 못했습니다."
            : !available ? "이 카드는 제작할 수 없습니다."
            : !affordable ? "카드 가루가 부족합니다."
            : "제작하면 이 카드를 획득합니다.";
        SetStatusText(string.Empty);
    }

    async UniTaskVoid CraftAsync(int _card, int _version)
    {
        CardCraftOutcome result = default;
        PlayerSaveCloud.CommandSession session = default;
        try
        {
            session = PlayerSaveCloud.CaptureCommandSession();
            try
            {
                ServerWaitOverlay.Hold(this);
                result = await CardCraftCommands.CraftAsync(_card);
            }
            finally { ServerWaitOverlay.Release(this); }

            if (result.Success && IsCurrentRequest(_card, _version)
                && PlayerSaveCloud.IsCommandSessionCurrent(session))
            {
                // 제작은 성장 결과 이벤트 없이 강화의 확대·공개·복귀 연출만 재사용한다.
                int level = CardGrowthManager.LevelOf(_card);
                await PlayPresentationAsync(_card, _version,
                    new EnhanceResult(EEnhanceOutcome.Success, level), level, DeckPower.MaxHpOf(_card));
            }
        }
        catch (Exception exception) { Debug.LogException(exception); }
        finally
        {
            if (this != null)
            {
                CancelPresentation();
                m_pending = false;
                m_crafting = false;
                if (IsViewVisible) Rebuild();
            }
        }
        if (!IsCurrentRequest(_card, _version) || !PlayerSaveCloud.IsCommandSessionCurrent(session)) return;
        if (result.Outcome == ECardCraftOutcome.NotReady && !PlayerSaveCloud.CanRunServerCommand) return;
        if (result.Success)
        {
            cardView.FlashGrowth();
            summaryView.FlashGrowth();
            SetStatusText("카드를 제작했습니다!");
        }
        else if (result.Outcome == ECardCraftOutcome.NetworkFailed)
            NetworkFailurePopup.Show("카드 제작 결과를 확인하지 못했습니다.");
        else if (result.Outcome != ECardCraftOutcome.AlreadyOwned || !OwnershipManager.IsOwned(_card))
        {
            string message = result.Outcome == ECardCraftOutcome.NotAffordable ? "카드 가루가 부족합니다."
                : result.Outcome == ECardCraftOutcome.NotCraftable ? "이 카드는 제작할 수 없습니다."
                : result.Outcome == ECardCraftOutcome.AlreadyOwned ? "이미 보유한 카드입니다. 게임에 다시 접속해 보유 정보를 갱신해 주세요."
                : "카드를 제작하지 못했습니다. 잠시 후 다시 시도해 주세요.";
            UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
                { titleText = message, yesText = "확인", noText = "닫기" });
        }
    }

    async UniTaskVoid EnhanceAsync(int _card, int _amount, int _version)
    {
        int fromLevel = CardGrowthManager.LevelOf(_card);
        int fromHp = DeckPower.MaxHpOf(_card);
        EnhanceResult result = new EnhanceResult(EEnhanceOutcome.NotReady, fromLevel);
        bool played = false;
        try
        {
            try
            {
                ServerWaitOverlay.Hold(this);
                result = await CardGrowthManager.TryEnhanceAsync(_card, _amount);
            }
            finally { ServerWaitOverlay.Release(this); }

            played = result.Outcome == EEnhanceOutcome.Success || result.Outcome == EEnhanceOutcome.Failed;
            if (!IsCurrentRequest(_card, _version)) return;
            if (played)
            {
                if (result.Outcome == EEnhanceOutcome.Success)
                {
                    CardGrowth before = CardGrowthManager.GrowthAtLevel(_card, fromLevel);
                    CardGrowth after = CardGrowthManager.GrowthAtLevel(_card, result.Level);
                    m_unlockKeywords = after.UnlockedKeywords & ~before.UnlockedKeywords
                        & CardVisualRules.InfoKeywordsWithLocked(_card);
                    m_unlockSynergy = !before.SynergyUnlocked && after.SynergyUnlocked;
                }
                OnEnhanceStarted?.Invoke();
                await PlayPresentationAsync(_card, _version, result, fromLevel, fromHp);
                if (IsCurrentRequest(_card, _version))
                {
                    CancelPresentation();
                    await PlayUnlockPresentationAsync(_card, _version);
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            if (this != null)
            {
                CancelUnlockPresentation();
                CancelPresentation();
                m_pending = false;
                if (IsViewVisible) Rebuild();
                if (IsCurrentRequest(_card, _version)) ShowEnhanceStatus(result, fromLevel);
            }
            // 안내는 실제 적용 결과를 받는다. 화면이 닫혀도 성공한 강화를 유실하지 않는다.
            if (played)
            {
                OnEnhanceResultReady?.Invoke(result);
                OnEnhanceSettled?.Invoke(result);
            }
        }
    }

    bool IsCurrentRequest(int _card, int _version)
        => this != null && IsViewVisible && VisibilityVersion == _version && m_card == _card;

    async UniTask PlayUnlockPresentationAsync(int _card, int _version)
    {
        CardKeyword keywords = m_unlockKeywords;
        bool synergy = m_unlockSynergy;
        if (keywords == CardKeyword.None && !synergy) return;

        RefreshSelection(); // 진화한 외형 아래 잠김 판은 해금 연출까지 유지한다.
        var rows = new List<AbilityDescriptionRow>();
        if (keywords != CardKeyword.None) rows.Add(keywordDescription);
        if (synergy)
            foreach (var row in synergyDescriptions)
                if (row.background.gameObject.activeInHierarchy) rows.Add(row);

        foreach (var row in rows)
        {
            Tween tween = row.unlockFx != null ? row.unlockFx.Play() : null;
            if (tween != null) m_unlockTweens.Add(tween);
            else row.lockIcon.SetActive(false);
        }
        while (IsCurrentRequest(_card, _version) && m_unlockTweens.Exists(t => t.IsActive() && t.IsPlaying()))
            await UniTask.Yield();
        if (!IsCurrentRequest(_card, _version)) return;

        m_unlockTweens.Clear();
        m_unlockKeywords = CardKeyword.None;
        m_unlockSynergy = false;
        RefreshAbilityDescriptions(CardGrowthManager.GrowthOf(_card));
        foreach (var row in rows)
        {
            Tween tween = row.revealFx != null ? row.revealFx.Play() : null;
            if (tween != null) m_unlockTweens.Add(tween);
        }
        while (IsCurrentRequest(_card, _version) && m_unlockTweens.Exists(t => t.IsActive() && t.IsPlaying()))
            await UniTask.Yield();
        if (!IsCurrentRequest(_card, _version)) return;
        m_unlockTweens.Clear();

        var intros = new List<UnlockIntro>();
        foreach (CardKeyword keyword in Enum.GetValues(typeof(CardKeyword)))
            if (keyword != CardKeyword.None && (keywords & keyword) != 0
                && UnlockIntro.TryForKeyword(abilityKeywordConfig, keyword, out var intro)) intros.Add(intro);
        if (synergy)
            foreach (var data in CardCatalog.RequireSynergies(_card))
                if (UnlockIntro.TryForSynergy(data, out var intro))
                {
                    intros.Add(intro);
                    break; // 상세창과 동일하게 시너지 개념은 한 번 안내한다.
                }

        if (intros.Count == 0 || !UnlockIntroOverlay.TryGet(out var overlay)) return;
        bool finished = false;
        m_unlockIntro = overlay;
        overlay.Show(intros, _card, _ => { m_unlockIntro = null; finished = true; });
        while (!finished && IsCurrentRequest(_card, _version)) await UniTask.Yield();
    }

    void CancelUnlockPresentation()
    {
        var intro = m_unlockIntro;
        m_unlockIntro = null;
        intro?.Cancel();
        foreach (var tween in m_unlockTweens) tween.Kill();
        m_unlockTweens.Clear();
        m_unlockKeywords = CardKeyword.None;
        m_unlockSynergy = false;
    }

    async UniTask PlayPresentationAsync(int _card, int _version, EnhanceResult _result, int _fromLevel, int _fromHp)
    {
        if (presentationRoot == null || presentationMotion == null || presentationCard == null || enhanceRitual == null)
        {
            Debug.LogError("[LobbyEnhanceTabPanel] Enhancement presentation is not wired.");
            return;
        }
        m_presenting = true;
        UiSortingOrder.LiftNested(presentationRoot, UiSortingOrder.CardGrowthPresentation);
        presentationRoot.SetActive(true);
        presentationRoot.transform.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();

        var source = (RectTransform)cardView.transform;
        var parent = (RectTransform)presentationMotion.parent;
        var corners = new Vector3[4];
        source.GetWorldCorners(corners);
        Vector3 from = parent.InverseTransformPoint((corners[0] + corners[2]) * .5f);
        float sourceWidth = Vector3.Distance(parent.InverseTransformPoint(corners[0]), parent.InverseTransformPoint(corners[3]));
        float fromScale = sourceWidth / presentationMotion.rect.width;
        float toScale = Mathf.Max(fromScale, Mathf.Min(parent.rect.width * .58f / presentationMotion.rect.width,
            parent.rect.height * .65f / presentationMotion.rect.height));
        presentationMotion.localPosition = from;
        presentationMotion.localScale = Vector3.one * fromScale;
        cardView.gameObject.SetActive(false);
        m_sourceHidden = true;

        m_presentationMove = DOTween.Sequence().SetLink(gameObject)
            .Join(presentationMotion.DOLocalMove(parent.rect.center, .25f).SetEase(Ease.OutCubic))
            .Join(presentationMotion.DOScale(toScale, .25f).SetEase(Ease.OutCubic));
        _ = m_presentationMove.Play();
        await m_presentationMove.ToUniTask();
        m_presentationMove = null;
        if (!IsCurrentRequest(_card, _version) || !m_presenting) return;

        bool evolved = _result.Outcome == EEnhanceOutcome.Success && _result.Level > _fromLevel;
        CardGrowthRitualView ritual = evolved && evolveRitual != null ? evolveRitual : enhanceRitual;
        if (evolved && evolveRitual != null)
        {
            presentationCard.CollectPendingKeywordFrames(_card, true, m_emblemBuffer);
            evolveRitual.SetEmblems(m_emblemBuffer);
        }
        bool finished = false;
        bool showResult = evolved && resultPanel != null;
        ritual.Play(_result.Outcome, showResult, () =>
        {
            if (!IsCurrentRequest(_card, _version) || !m_presenting) return;
            presentationCard.Bind(_card, true);
            RefreshSelection();
            if (_result.Outcome == EEnhanceOutcome.Success) presentationCard.FlashGrowth();
        }, () =>
        {
            if (!showResult || !IsCurrentRequest(_card, _version) || !m_presenting) return;
            var unlocked = new List<string>();
            foreach (CardKeyword keyword in Enum.GetValues(typeof(CardKeyword)))
                if (keyword != CardKeyword.None && (m_unlockKeywords & keyword) != 0
                    && abilityKeywordConfig.TryGetEntry(keyword, out var entry))
                    unlocked.Add($"{entry.displayName} 해금");
            if (m_unlockSynergy && CardCatalog.RequireSynergies(_card).Count > 0) unlocked.Add("시너지 해금");
            var line = new EnhanceResultLine(_result.Outcome, _fromHp, DeckPower.MaxHpOf(_card),
                _fromLevel, _result.Level, false, string.Empty,
                _unlockText: string.Join("\n", unlocked), _titleText: "진화 성공!");
            resultPanel.Show(line, () =>
            {
                if (IsCurrentRequest(_card, _version) && m_presenting) ritual.PlayReturn();
            }, null, _autoReturn: true, _centeredCard: true);
        }, () => finished = true);
        while (!finished && IsCurrentRequest(_card, _version) && m_presenting) await UniTask.Yield();
        if (!IsCurrentRequest(_card, _version) || !m_presenting) return;

        presentationCard.RestoreGrowthFlash();
        m_presentationMove = DOTween.Sequence().SetLink(gameObject)
            .Join(presentationMotion.DOLocalMove(from, .22f).SetEase(Ease.InOutCubic))
            .Join(presentationMotion.DOScale(fromScale, .22f).SetEase(Ease.InOutCubic));
        _ = m_presentationMove.Play();
        await m_presentationMove.ToUniTask();
        m_presentationMove = null;
    }

    void CancelPresentation()
    {
        m_presenting = false; // CancelImmediate의 공개 콜백이 숨겨진 화면을 다시 그리지 않게 먼저 내린다.
        resultPanel?.HideImmediate();
        var move = m_presentationMove;
        m_presentationMove = null;
        move?.Kill();
        enhanceRitual?.CancelImmediate();
        evolveRitual?.CancelImmediate();
        presentationCard?.RestoreGrowthFlash();
        if (presentationRoot != null) presentationRoot.SetActive(false);
        if (m_sourceHidden && cardView != null) cardView.gameObject.SetActive(true);
        m_sourceHidden = false;
    }

    void ShowEnhanceStatus(EnhanceResult result, int fromLevel)
    {
        switch (result.Outcome)
        {
            case EEnhanceOutcome.Success:
                summaryView.FlashGrowth();
                SetStatusText(result.Level > fromLevel ? "진화했습니다!" : $"샤드 {result.AppliedShards:N0}개를 적용했습니다.");
                break;
            case EEnhanceOutcome.NotAffordable: SetStatusText("샤드가 부족합니다. 수량을 확인해 주세요."); break;
            case EEnhanceOutcome.MaxLevel: SetStatusText("최대 성장에 도달했습니다."); break;
            default: SetStatusText("강화를 완료하지 못했습니다. 잠시 후 다시 시도해 주세요."); break;
        }
    }
}
