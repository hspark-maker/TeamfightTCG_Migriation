using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 일일·주간 미션 화면(MissionOverlay 에 부착). 풀(UIPoolManager)이 수명을 쥔다 —
/// 규약은 <see cref="RankRewardPanel"/>·<see cref="RoulettePanel"/> 과 같다
/// (캔버스 기준 해상도 1080x1920 주의 포함).
///
/// <para><b>미션 정의를 이 화면이 들고 있지 않는다.</b> 제목·설명·목표·보상은 전부 서버가
/// <c>getMissions</c> 로 준 값이고, 완료 판정은 <see cref="MissionManager"/> 한 곳이 소유한다.
/// 화면이 자기 사본을 두면 밸런스 수정이 앱 배포에 묶이고, 보이는 목표와 거절 조건이 갈린다.</para>
///
/// <para>열 때마다 조회를 한 번 던지지만 <b>기다리지 않는다</b> — 캐시된 상태로 즉시 그리고
/// 응답이 오면 <see cref="MissionManager.OnChanged"/> 로 다시 그린다. 미션은 부가 기능이라
/// 왕복 실패가 화면을 막아서는 안 된다.</para>
/// </summary>
public class MissionPanel : ContentsPooledUI
{
    // 미션 내용은 MissionManager에서 읽고, 진입 데이터는 처음 열 탭만 지정한다.
    public override void Initialization(UIData _data)
    {
        this.InitializeUI();
        this.data = _data;
    }

    public override void Show() => this.Open();

    public override void Hide() => this.Close();

    [Header("목록")]
    [Tooltip("일일 미션 행이 쌓일 Content(VerticalLayoutGroup).")]
    [SerializeField] Transform dailyContent;

    [Tooltip("주간 미션 행이 쌓일 Content(VerticalLayoutGroup).")]
    [SerializeField] Transform weeklyContent;

    [Tooltip("탭 전환 때 켜고 끌 스크롤 래퍼(ScrollRect+RectMask2D). 비면 Content 자체를 토글한다 —\n" +
             "래퍼가 있는데 Content 를 토글하면 겹친 빈 ScrollRect 가 반대 탭의 드래그를 삼킨다.")]
    [SerializeField] GameObject dailyListRoot;

    [SerializeField] GameObject weeklyListRoot;

    [Tooltip("가이드·일일·주간이 공유하는 미션 행 프리팹 에셋.")]
    [UnityEngine.Serialization.FormerlySerializedAs("dailyRowPrefab")]
    [SerializeField] MissionRowView rowPrefab;

    [Tooltip("현재 탭의 일일·주간 달성 보상. 서버의 완료 미션 정의를 공용 뷰로 표시한다.")]
    [UnityEngine.Serialization.FormerlySerializedAs("dailyCompletionRow")]
    [SerializeField] MissionRowView completionRow;

    [Tooltip("해당 주기에 미션이 하나도 없을 때 켤 안내. 지금은 활성 미션이 팩 개봉 축뿐이라 자주 빈다.")]
    [SerializeField] GameObject dailyEmptyNotice;

    [SerializeField] GameObject weeklyEmptyNotice;

    [Header("리셋 표시")]
    [Tooltip("남은 시간. 비워 두면 그리지 않는다.")]
    [SerializeField] TMP_Text dailyResetText;

    [SerializeField] TMP_Text weeklyResetText;

    [Tooltip("탭형 저작의 단일 리셋 라벨 — 지금 보이는 탭의 주기를 그린다. 위 두 라벨과 택일이다.")]
    [SerializeField] TMP_Text resetText;

    [Header("탭")]
    [Tooltip("일일·주간 탭. 둘 다 배선하면 탭 모드 — 목록이 하나씩만 보이고 버튼이 갈아끼운다.\n" +
             "하나라도 비면 구판처럼 두 목록을 같이 그린다.")]
    [SerializeField] Button dailyTabButton;

    [SerializeField] Button weeklyTabButton;

    [Tooltip("선택된 탭에 표시할 눌림 스프라이트.")]
    [SerializeField] Sprite selectedTabSprite;

    [Tooltip("선택되지 않은 탭에 표시할 기본 스프라이트.")]
    [SerializeField] Sprite unselectedTabSprite;

    [Tooltip("선택되지 않은 탭 배경에 적용할 옅은 회색.")]
    [SerializeField] Color unselectedTabColor = new Color(0.85f, 0.85f, 0.85f, 1f);

    [Tooltip("선택된 탭 전체의 확대 배율. 비선택 탭은 1배로 표시한다.")]
    [SerializeField] float selectedTabScale = 1.1f;

    [Header("버튼")]
    [SerializeField] Button closeButton;

    [Tooltip("모두 받기. 받을 수 있는 미션이 있을 때만 눌린다. 서버에 일괄 창구가 없어 미션별 왕복을 순차로 돈다 —\n" +
             "도는 동안은 ServerWaitOverlay 가 입력을 막는다.")]
    [SerializeField] Button claimAllButton;
    [SerializeField] GameObject claimAllAlertDot;

    [Tooltip("패널 밖(딤)을 눌러 닫는 판. 알파 0 Image 의 Button 에 배선한다.")]
    [SerializeField] Button dimButton;

    readonly List<MissionRowView> m_dailyRows = new List<MissionRowView>();
    readonly List<MissionRowView> m_weeklyRows = new List<MissionRowView>();
    readonly List<MissionDefinition> m_sortedDefinitions = new List<MissionDefinition>();

    // 행 목록은 최대 사용량만큼 보관한다. 빈 안내에는 현재 활성 행 수를 쓴다.
    int m_dailyRowCount;
    int m_weeklyRowCount;
    Action<string> m_claimHandler;
    Action<string> m_navigateHandler;
    MissionDefinition m_completionDefinition;

    // 탭 모드에서 지금 주간 탭인가. 열 때마다 일일로 돌아간다 — 미션의 주 무대가 일일이다.
    bool m_weeklyTab;
    bool m_claimingAll;

    const string PERIOD_DAILY = "daily";
    const string PERIOD_WEEKLY = "weekly";

    // 씬 버튼 UnityEvent 가 인자 없는 이 시그니처에 바인딩된다 — 매개변수를 붙이면 배선이 끊긴다.
    public void Open()
    {
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission)) return;

        this.m_weeklyTab = this.data is MissionPanelData t_data && t_data.WeeklyTab;
        this.SetContentsVisible(true);
        this.Rebuild();

        // 초기화 요청을 공유하고, 상태·기간·저장 버전이 유효하면 최근 조회를 재사용한다.
        MissionCommands.RefreshAsync().Forget();
    }

    public void Close() => this.SetContentsVisible(false);

    protected override void OnInitializeUI()
    {
        // 화면 표시와 무관하게 고정 버튼을 한 번 배선한다.
        if (this.closeButton != null)
        {
            this.closeButton.onClick.RemoveAllListeners();
            this.closeButton.onClick.AddListener(this.Close);
        }
        if (this.dimButton != null)
        {
            this.dimButton.onClick.RemoveAllListeners();
            this.dimButton.onClick.AddListener(this.Close);
        }
        if (this.claimAllButton != null)
        {
            this.claimAllButton.onClick.RemoveAllListeners();
            this.claimAllButton.onClick.AddListener(this.HandleClaimAll);
        }
        // 탭 모드는 둘 다 배선됐을 때만 선다 — 한쪽만 배선된 오저작에서 리스너만 붙으면
        // ApplyTab 은 비켜서는데 m_weeklyTab 만 갈려 resetText 가 다른 주기를 그린다.
        if (this.dailyTabButton != null && this.weeklyTabButton != null)
        {
            this.dailyTabButton.onClick.RemoveAllListeners();
            this.dailyTabButton.onClick.AddListener(this.SelectDailyTab);
            this.weeklyTabButton.onClick.RemoveAllListeners();
            this.weeklyTabButton.onClick.AddListener(this.SelectWeeklyTab);
        }
    }

    protected override void OnViewShown()
    {
        MissionManager.OnChanged += this.HandleMissionsChanged;
    }

    protected override void OnViewHidden()
    {
        MissionManager.OnChanged -= this.HandleMissionsChanged;
    }

    void Update()
    {
        // 카운트다운만 매 프레임 갱신한다. 행 다시 그리기는 OnChanged 가 유발한다.
        if (!this.isShow) return;
        this.RefreshResetLabels();
    }

    void HandleMissionsChanged()
    {
        if (this.isShow) this.Rebuild();
    }

    void Rebuild()
    {
        this.RefreshCompletionRow();
        this.m_dailyRowCount = BuildSection(this.dailyContent, this.rowPrefab, this.m_dailyRows, PERIOD_DAILY, this.dailyEmptyNotice);
        this.m_weeklyRowCount = BuildSection(this.weeklyContent, this.rowPrefab, this.m_weeklyRows, PERIOD_WEEKLY, this.weeklyEmptyNotice);
        this.ApplyTab();
        this.RefreshClaimAllButton();
        this.RefreshResetLabels();
    }

    void RefreshCompletionRow()
    {
        this.m_completionDefinition = null;
        if (this.completionRow == null) return;

        string t_period = this.m_weeklyTab ? PERIOD_WEEKLY : PERIOD_DAILY;
        string t_event = this.m_weeklyTab ? "CompleteWeeklyMissions" : "CompleteDailyMissions";
        foreach (MissionDefinition t_definition in MissionManager.Definitions)
        {
            if (t_definition.Period != t_period || t_definition.Event != t_event) continue;
            this.m_completionDefinition = t_definition;
            this.m_claimHandler ??= this.HandleClaim;
            this.completionRow.Bind(t_definition, this.m_claimHandler);
            break;
        }
    }

    void SelectDailyTab() => this.SelectTab(false);

    void SelectWeeklyTab() => this.SelectTab(true);

    void SelectTab(bool _weekly)
    {
        if (this.m_weeklyTab == _weekly) return;

        this.m_weeklyTab = _weekly;
        this.Rebuild();
    }

    /// <summary>탭 모드면 목록을 하나만 남긴다. 빈 안내는 BuildSection 이 개수로 켠 것을
    /// 탭 가시성으로 한 번 더 거른다 — 안내가 목록 밖 형제라 탭 전환이 스스로 끄지 못한다.</summary>
    void ApplyTab()
    {
        if (this.completionRow != null)
            this.completionRow.gameObject.SetActive(this.m_completionDefinition != null);
        if (this.dailyTabButton == null || this.weeklyTabButton == null) return;

        Vector3 t_selectedScale = new Vector3(this.selectedTabScale, this.selectedTabScale, 1f);
        this.dailyTabButton.transform.localScale = this.m_weeklyTab ? Vector3.one : t_selectedScale;
        this.weeklyTabButton.transform.localScale = this.m_weeklyTab ? t_selectedScale : Vector3.one;

        GameObject t_daily = this.dailyListRoot != null ? this.dailyListRoot : this.dailyContent != null ? this.dailyContent.gameObject : null;
        GameObject t_weekly = this.weeklyListRoot != null ? this.weeklyListRoot : this.weeklyContent != null ? this.weeklyContent.gameObject : null;
        if (t_daily != null) t_daily.SetActive(!this.m_weeklyTab);
        if (t_weekly != null) t_weekly.SetActive(this.m_weeklyTab);

        if (this.dailyEmptyNotice != null) this.dailyEmptyNotice.SetActive(!this.m_weeklyTab && this.m_dailyRowCount == 0);
        if (this.weeklyEmptyNotice != null) this.weeklyEmptyNotice.SetActive(this.m_weeklyTab && this.m_weeklyRowCount == 0);

        if (this.dailyTabButton.targetGraphic is Image t_dailyImage)
        {
            t_dailyImage.sprite = this.m_weeklyTab ? this.unselectedTabSprite : this.selectedTabSprite;
            t_dailyImage.color = this.m_weeklyTab ? this.unselectedTabColor : Color.white;
        }
        if (this.weeklyTabButton.targetGraphic is Image t_weeklyImage)
        {
            t_weeklyImage.sprite = this.m_weeklyTab ? this.selectedTabSprite : this.unselectedTabSprite;
            t_weeklyImage.color = this.m_weeklyTab ? Color.white : this.unselectedTabColor;
        }
    }

    int BuildSection(Transform _content, MissionRowView _rowPrefab, List<MissionRowView> _rows,
                      string _period, GameObject _emptyNotice)
    {
        if (_content == null || _rowPrefab == null) return 0;

        // 씬 안의 템플릿도 원본으로 보존한다. 다른 저작 자식은 건드리지 않는다.
        if (_rowPrefab.transform.parent == _content) _rowPrefab.gameObject.SetActive(false);

        int t_count = 0;
        this.m_claimHandler ??= this.HandleClaim;
        this.m_navigateHandler ??= this.HandleNavigate;
        this.m_sortedDefinitions.Clear();
        IReadOnlyList<MissionDefinition> t_definitions = MissionManager.Definitions;
        for (int i = 0; i < t_definitions.Count; i++)
        {
            MissionDefinition t_definition = t_definitions[i];
            // 가이드 미션은 전용 화면(GuideMissionPanel)이 그린다 — 여기서는 정확히 해당 주기만.
            if (!string.Equals(t_definition.Period, _period, StringComparison.Ordinal)) continue;
            if (ReferenceEquals(t_definition, this.m_completionDefinition)) continue;
            this.m_sortedDefinitions.Add(t_definition);
        }
        this.m_sortedDefinitions.Sort(CompareDisplayOrder);

        for (int i = 0; i < this.m_sortedDefinitions.Count; i++)
        {
            MissionDefinition t_definition = this.m_sortedDefinitions[i];
            if (t_count == _rows.Count) _rows.Add(null);
            MissionRowView t_row = _rows[t_count];
            if (t_row == null) _rows[t_count] = t_row = Instantiate(_rowPrefab, _content);
            t_row.Bind(t_definition, this.m_claimHandler,
                MissionContentNavigation.HasDestination(t_definition) ? this.m_navigateHandler : null);
            if (!t_row.gameObject.activeSelf) t_row.gameObject.SetActive(true);
            t_count++;
        }

        for (int i = t_count; i < _rows.Count; i++)
            if (_rows[i] != null && _rows[i].gameObject.activeSelf) _rows[i].gameObject.SetActive(false);
        if (_emptyNotice != null) _emptyNotice.SetActive(t_count == 0);
        return t_count;
    }

    static int CompareDisplayOrder(MissionDefinition _left, MissionDefinition _right)
    {
        // 완료·미수령 → 진행 중(완료율 내림차순) → 수령 완료. 동률은 기존 저작 순서.
        bool t_leftClaimed = MissionManager.IsClaimed(_left.Id);
        bool t_rightClaimed = MissionManager.IsClaimed(_right.Id);
        int t_claimed = t_leftClaimed.CompareTo(t_rightClaimed);
        if (t_claimed != 0) return t_claimed;

        if (!t_leftClaimed)
        {
            bool t_leftComplete = MissionManager.IsComplete(_left);
            bool t_rightComplete = MissionManager.IsComplete(_right);
            int t_complete = t_rightComplete.CompareTo(t_leftComplete);
            if (t_complete != 0) return t_complete;

            if (!t_leftComplete)
            {
                double t_leftRate = (double)MissionManager.ProgressOf(_left) / _left.Target;
                double t_rightRate = (double)MissionManager.ProgressOf(_right) / _right.Target;
                int t_rate = t_rightRate.CompareTo(t_leftRate);
                if (t_rate != 0) return t_rate;
            }
        }

        int t_order = _left.SortOrder.CompareTo(_right.SortOrder);
        return t_order != 0 ? t_order : string.CompareOrdinal(_left.Id, _right.Id);
    }

    /// <summary>받을 수 있는 미션이 하나라도 있을 때만 모두 받기를 살린다.
    /// 판정은 행과 같은 MissionManager.CanClaim 하나다 — 버튼과 목록이 다른 눈으로 보면 갈린다.</summary>
    void RefreshClaimAllButton()
    {
        bool t_available = !this.m_claimingAll && MissionManager.HasAnyRegularClaimable;
        if (this.claimAllButton != null) this.claimAllButton.interactable = t_available;
        if (this.claimAllAlertDot != null) this.claimAllAlertDot.SetActive(t_available);
    }

    void HandleClaimAll() => this.ClaimAllAsync().Forget();

    void HandleNavigate(string _missionId)
    {
        if (this.m_claimingAll || !this.isShow) return;
        MissionContentNavigation.TryNavigate(MissionManager.Find(_missionId), this.Close);
    }

    async UniTaskVoid ClaimAllAsync()
    {
        if (this.m_claimingAll) return;
        var t_ids = new List<string>();
        IReadOnlyList<MissionDefinition> t_definitions = MissionManager.Definitions;
        for (int i = 0; i < t_definitions.Count; i++)
            if ((t_definitions[i].Period == PERIOD_DAILY || t_definitions[i].Period == PERIOD_WEEKLY)
                && MissionManager.CanClaim(t_definitions[i])) t_ids.Add(t_definitions[i].Id);
        if (t_ids.Count == 0) return;

        this.m_claimingAll = true;
        this.RefreshClaimAllButton();

        // 서버에 일괄 수령 창구가 없어 순차 요청하고, 성공한 보상만 모아 한 번 표시한다.
        var t_results = new List<ClaimMissionResult>();
        var releaseDisplay = CurrencyHud.HoldRewardDisplays();
        ServerWaitOverlay.Hold(this);
        try
        {
            for (int i = 0; i < t_ids.Count; i++)
            {
                // 한 건이 거절돼도 나머지는 계속 간다 — 채택은 응답 봉투가 중앙에서 하고, 실패 줄은 화면에 남는다.
                ClaimMissionResult t_result = await MissionCommands.ClaimAsync(t_ids[i]);
                if (t_result != null) t_results.Add(t_result);
            }
        }
        catch
        {
            releaseDisplay();
            throw;
        }
        finally
        {
            // 팝업보다 먼저 걷는다 — ClaimAsync 와 같은 계약.
            ServerWaitOverlay.Release(this);
            this.m_claimingAll = false;
            this.RefreshClaimAllButton();
        }
        if (t_results.Exists(t_result => (t_result.Cards?.Count ?? 0) > 0)) this.Close();
        ShowClaimedRewards(t_results, _onClosed: releaseDisplay);
    }

    void HandleClaim(string _missionId)
    {
        this.ClaimAsync(_missionId).Forget();
    }

    async UniTaskVoid ClaimAsync(string _missionId)
    {
        // 왕복 동안 입력을 막는다. 딤·스피너는 임계 뒤에만 뜨므로 빠른 응답에서는 깜빡이지 않는다.
        ClaimMissionResult t_result = null;
        var releaseDisplay = CurrencyHud.HoldRewardDisplays();
        ServerWaitOverlay.Hold(this);
        try
        {
            // 진행도·낙인은 낙관 갱신하지 않는다 — 응답 봉투를 ServerSaveCommands 가 중앙에서 채택하고,
            // 그 채택이 OnChanged 를 태워 화면이 갱신된다. 실패하면 화면은 그대로 남는다.
            t_result = await MissionCommands.ClaimAsync(_missionId);
        }
        finally
        {
            // **팝업보다 먼저 걷는다.** 순서를 뒤집으면 안내가 대기 딤에 묻힌다
            // (PackPurchaseFlow 와 같은 계약 — ServerWaitOverlay 는 자기 캔버스가 없다).
            ServerWaitOverlay.Release(this);
            if (t_result == null) releaseDisplay();
        }
        if (t_result != null)
        {
            // 개봉 화면보다 높은 미션 패널을 먼저 걷는다.
            if ((t_result.Cards?.Count ?? 0) > 0) this.Close();
            ShowClaimedRewards(new[] { t_result }, _onClosed: releaseDisplay);
        }
    }

    // GuideMissionPanel 도 같은 보상 표시 경로를 쓴다 — 미션 보상 팝업 조립의 단일 지점.
    internal static void ShowClaimedRewards(IReadOnlyList<ClaimMissionResult> _results, string _title = "미션 보상",
        Action _onClosed = null, bool _skipDirectCardPresentation = false)
    {
        if (_results.Count == 0)
        {
            _onClosed?.Invoke();
            return;
        }

        var t_bucket = new CurrencyGainBucket();
        var t_cards = new List<OpenPackCard>();
        var t_packs = new List<ClaimRewardPack>();
        var t_cosmetics = new List<GrantedCosmetic>();
        var t_titles = new List<GrantedTitle>();
        long t_passExp = 0;
        long t_accountExp = 0;
        long t_totalExp = 0;
        int t_previousLevel = int.MaxValue;
        int t_level = 0;
        for (int i = 0; i < _results.Count; i++)
        {
            var t_result = _results[i];
            if (t_result.Granted != null)
                foreach (var t_gain in t_result.Granted)
                    if (t_gain != null && CurrencyCode.TryParse(t_gain.Currency, out var t_type))
                        t_bucket.Add(t_type, t_gain.Amount);
            if (t_result.Cards != null) t_cards.AddRange(t_result.Cards);
            if (t_result.Packs != null) t_packs.AddRange(t_result.Packs);
            if (t_result.Cosmetics != null) t_cosmetics.AddRange(t_result.Cosmetics);
            if (t_result.Titles != null) t_titles.AddRange(t_result.Titles);
            t_passExp += t_result.GrantedPassExp;
            t_accountExp += t_result.AccountExperience?.GrantedExp ?? t_result.GrantedAccountExp;
            t_totalExp = Math.Max(t_totalExp, t_result.AccountExperience?.TotalExp ?? AccountLevelManager.Exp);
            if (t_result.AccountExperience?.IsLevelUp == true)
            {
                t_previousLevel = Math.Min(t_previousLevel, t_result.AccountExperience.PreviousLevel);
                t_level = Math.Max(t_level, t_result.AccountExperience.Level);
            }
        }

        var t_gains = new List<CurrencyGain>();
        var t_lines = new List<RewardLine>();
        for (int i = 0; i < (int)ECurrencyType.Count; i++)
        {
            var t_type = (ECurrencyType)i;
            if (t_bucket[t_type] <= 0) continue;
            var t_gain = new CurrencyGain(t_type, t_bucket[t_type]);
            t_gains.Add(t_gain);
            t_lines.Add(new RewardLine(t_gain));
        }

        // 이미 지급된 응답이다. 팝업 확인에서는 서버 수령을 다시 호출하지 않는다.
        var t_outcome = RewardItemDisplay.ToOutcome(t_gains, t_cards, t_packs, t_cosmetics, t_titles);
        var t_packCounts = new Dictionary<string, long>();
        foreach (var t_pack in t_outcome.Packs)
            t_packCounts[t_pack.PackId] = t_packCounts.TryGetValue(t_pack.PackId, out long t_count) ? t_count + 1 : 1;
        foreach (var t_pack in t_packCounts)
            t_lines.Add(new RewardLine(new AlbumRewardDef { rewardType = ERewardType.Pack,
                rewardId = t_pack.Key, amount = t_pack.Value }));
        var t_cardCounts = new Dictionary<int, long>();
        foreach (var t_card in t_outcome.Cards)
            t_cardCounts[t_card.CardId] = t_cardCounts.TryGetValue(t_card.CardId, out long t_count) ? t_count + 1 : 1;
        foreach (var t_card in t_cardCounts)
            t_lines.Add(new RewardLine(new AlbumRewardDef { rewardType = ERewardType.Card,
                rewardId = t_card.Key.ToString(), amount = t_card.Value }));
        if (_skipDirectCardPresentation)
        {
            var t_packBatches = new List<RewardPresentationBatch>();
            foreach (var t_batch in t_outcome.PresentationBatches)
                if (t_batch.IsPack) t_packBatches.Add(t_batch);
            t_outcome = new RewardClaimOutcome(t_outcome.Granted, _packs: t_outcome.Packs,
                _presentationBatches: t_packBatches, _cosmetics: t_outcome.Cosmetics, _titles: t_outcome.Titles);
        }
        if (t_lines.Count == 0 && t_passExp <= 0 && t_accountExp <= 0)
        {
            RewardPackPresentation.Show(t_outcome);
            _onClosed?.Invoke();
            return;
        }
        if (RewardClaimPopup.TryGet(out var t_popup) && t_popup.RewardSlotCount > 0)
        {
            string t_title = _title;
            if (t_level > t_previousLevel) t_title += $" · 레벨업 Lv.{t_previousLevel} → {t_level}";
            if (t_passExp > 0) t_title += $"\n패스 경험치 +{t_passExp:N0}";
            ShowRewardPage(t_popup, t_title, t_lines, t_outcome, 0, _onClosed, t_accountExp, t_totalExp);
        }
        else
            ShowRewardWithoutPopup(t_outcome, _onClosed).Forget();
    }

    internal static void ShowAccountExperienceRewards(AccountRewardHandoff.Entry _entry)
    {
        ShowClaimedRewards(new[] { new ClaimMissionResult
        {
            Granted = _entry.Granted,
            Cards = _entry.Cards,
            Packs = _entry.Packs,
            Cosmetics = _entry.Cosmetics,
            Titles = _entry.Titles,
            AccountExperience = _entry.Experience,
        } }, "전투 보상");
    }

    static void ShowRewardPage(RewardClaimPopup _popup, string _title, List<RewardLine> _lines,
                               RewardClaimOutcome _outcome, int _offset, Action _onClosed,
                               long _accountExp = 0, long _totalExp = 0)
    {
        int t_count = Math.Min(_popup.RewardSlotCount, _lines.Count - _offset);
        int t_next = _offset + t_count;
        bool t_hasNext = t_next < _lines.Count;
        // 카드 상세는 마지막 페이지를 확인한 뒤에만 연다.
        var t_pageOutcome = new RewardClaimOutcome(_outcome.Granted, t_hasNext ? null : _outcome.Cards,
            t_hasNext ? null : _outcome.Packs, t_hasNext ? null : _outcome.PresentationBatches,
            _outcome.ShowCardsIndividually, t_hasNext ? null : _outcome.Cosmetics, t_hasNext ? null : _outcome.Titles);
        int t_pages = Math.Max(1, (_lines.Count + _popup.RewardSlotCount - 1) / _popup.RewardSlotCount);
        string t_page = t_pages > 1 ? $" ({_offset / _popup.RewardSlotCount + 1}/{t_pages})" : string.Empty;
        int t_break = _title.IndexOf('\n');
        // 공용 팝업 제목은 900×90, 기본 60pt다. 상세 경험치를 같은 크기로 두 줄 쓰면 영역을 넘는다.
        string t_title = t_break >= 0
            ? $"<size=36>{_title.Substring(0, t_break)}{t_page}</size>\n<size=28>{_title.Substring(t_break + 1)}</size>"
            : $"<size=36>{_title}{t_page}</size>";
        _popup.Show(t_title, _lines.GetRange(_offset, t_count), () => UniTask.FromResult(t_pageOutcome),
            _claimOnDim: true,
            _onClosed: t_hasNext ? () => ShowRewardPage(_popup, _title, _lines, _outcome, t_next, _onClosed) : _onClosed,
            _accountExp: _accountExp, _accountTotalExp: _totalExp);
    }

    static async UniTask ShowRewardWithoutPopup(RewardClaimOutcome _outcome, Action _onClosed)
    {
        try
        {
            await RewardClaimPopup.ClaimWithoutPopup(() => UniTask.FromResult(_outcome));
        }
        finally
        {
            _onClosed?.Invoke();
        }
    }

    // 리셋 시각의 진실원은 서버가 준 epoch ms 다. 남은 시간 표시에만 기기 시계를 쓴다 —
    // 실제 리셋 판정은 서버가 하므로 시계를 돌려도 미션이 앞당겨 열리지는 않는다.
    void RefreshResetLabels()
    {
        long t_nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        SetResetLabel(this.dailyResetText, MissionManager.DailyResetAtMs, t_nowMs);
        SetResetLabel(this.weeklyResetText, MissionManager.WeeklyResetAtMs, t_nowMs);
        SetResetLabel(this.resetText, this.m_weeklyTab ? MissionManager.WeeklyResetAtMs : MissionManager.DailyResetAtMs, t_nowMs);
    }

    static void SetResetLabel(TMP_Text _label, long _resetAtMs, long _nowMs)
    {
        if (_label == null) return;
        if (_resetAtMs <= 0)
        {
            // 아직 한 번도 조회하지 못한 상태. "0시간 남음"으로 거짓말하지 않는다.
            _label.text = string.Empty;
            return;
        }

        long t_remainMs = _resetAtMs - _nowMs;
        if (t_remainMs < 0) t_remainMs = 0;

        var t_span = TimeSpan.FromMilliseconds(t_remainMs);
        _label.text = t_span.TotalDays >= 1d
            ? $"{(int)t_span.TotalDays}일 {t_span.Hours}시간 남음"
            : $"{(int)t_span.TotalHours}시간 {t_span.Minutes}분 남음";
    }
}

public sealed class MissionPanelData : UIData
{
    public bool WeeklyTab;
}
