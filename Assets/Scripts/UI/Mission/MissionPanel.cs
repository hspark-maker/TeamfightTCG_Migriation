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
public class MissionPanel : PooledUIBase
{
    // 풀 계약. 표시 데이터는 MissionManager 에서 스스로 당기므로 UIData 가 필요 없다.
    public override void Initialization(UIData _data) { }

    public override void Show() => this.Open();

    public override void Hide() => this.Close();

    [Tooltip("켜고 끌 대상(딤 + 패널). 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

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

    [Tooltip("선택 안 된 탭 그림에 씌울 틴트. 선택 탭은 저작 색(백색 곱)으로 돌아간다.")]
    [SerializeField] Color tabDimColor = new Color(0.6f, 0.6f, 0.6f, 1f);

    [Header("버튼")]
    [SerializeField] Button closeButton;

    [Tooltip("모두 받기. 받을 수 있는 미션이 있을 때만 눌린다. 서버에 일괄 창구가 없어 미션별 왕복을 순차로 돈다 —\n" +
             "도는 동안은 ServerWaitOverlay 가 입력을 막는다.")]
    [SerializeField] Button claimAllButton;
    [SerializeField] GameObject claimAllAlertDot;

    [Tooltip("패널 밖(딤)을 눌러 닫는 판. 알파 0 Image 의 Button 에 배선한다.")]
    [SerializeField] Button dimButton;

    [Header("연출")]
    [Tooltip("panel 에는 Root/Panel 을 배선한다 — root 를 물리면 전체화면 딤까지 함께 커진다.")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("공용 ScreenDim(Full)에 요청할 암막 짙기.")]
    [Range(0f, 1f)] [SerializeField] float dimAlpha = 0.72f;

    readonly List<MissionRowView> m_dailyRows = new List<MissionRowView>();
    readonly List<MissionRowView> m_weeklyRows = new List<MissionRowView>();

    // 지금 화면에 깔린 행이 어느 정의 목록으로 만들어졌는지. 정의가 바뀌면(첫 조회 응답 도착 등)
    // 다시 깐다 — 개수만 보면 활성 미션이 교체됐을 때 옛 제목이 남는다.
    string m_builtSignature;

    // 탭 모드에서 지금 주간 탭인가. 열 때마다 일일로 돌아간다 — 미션의 주 무대가 일일이다.
    bool m_weeklyTab;
    bool m_claimingAll;

    const string PERIOD_DAILY = "daily";
    const string PERIOD_WEEKLY = "weekly";

    // 씬 버튼 UnityEvent 가 인자 없는 이 시그니처에 바인딩된다 — 매개변수를 붙이면 배선이 끊긴다.
    public void Open()
    {
        this.m_weeklyTab = false;
        this.SetVisible(true);
        this.Rebuild();

        // 정상 경로의 조회는 초기화(MissionPreloadStep)가 이미 했다 — 열 때마다 왕복하지 않는다.
        // 여기 조회는 그 왕복이 실패해 캐시가 빈 경우의 안전망뿐이다. 던져만 두므로 화면은 기다리지 않고,
        // 응답이 오면 OnChanged 가 다시 그린다.
        MissionCommands.RefreshAsync().Forget();
    }

    public void Close() => this.SetVisible(false);

    void OnEnable()
    {
        // 재활성마다 중복 등록 방지.
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

        MissionManager.OnChanged += this.HandleMissionsChanged;
    }

    void OnDisable()
    {
        MissionManager.OnChanged -= this.HandleMissionsChanged;

        // 안전망 — Close 를 거치지 않고 꺼지면 공용 딤이 남는다.
        ScreenDim.Hide(this);
        this.transition.HandleDisabled(this.ResolveTarget());
    }

    void Update()
    {
        // 카운트다운만 매 프레임 갱신한다. 행 다시 그리기는 OnChanged 가 유발한다.
        if (!this.isShow) return;
        this.RefreshResetLabels();
    }

    // 조회 응답이 정의를 갈아끼웠을 수 있으므로 서명을 보고 필요할 때만 다시 깐다.
    void HandleMissionsChanged()
    {
        if (BuildSignature() != this.m_builtSignature) this.Rebuild();
        else this.RefreshRows();
    }

    void Rebuild()
    {
        BuildSection(this.dailyContent, this.rowPrefab, this.m_dailyRows, PERIOD_DAILY, this.dailyEmptyNotice);
        BuildSection(this.weeklyContent, this.rowPrefab, this.m_weeklyRows, PERIOD_WEEKLY, this.weeklyEmptyNotice);
        this.m_builtSignature = BuildSignature();
        this.ApplyTab();
        this.RefreshClaimAllButton();
        this.RefreshResetLabels();
    }

    void SelectDailyTab() => this.SelectTab(false);

    void SelectWeeklyTab() => this.SelectTab(true);

    void SelectTab(bool _weekly)
    {
        if (this.m_weeklyTab == _weekly) return;

        this.m_weeklyTab = _weekly;
        this.ApplyTab();
        this.RefreshResetLabels();
    }

    /// <summary>탭 모드면 목록을 하나만 남긴다. 빈 안내는 BuildSection 이 개수로 켠 것을
    /// 탭 가시성으로 한 번 더 거른다 — 안내가 목록 밖 형제라 탭 전환이 스스로 끄지 못한다.</summary>
    void ApplyTab()
    {
        if (this.dailyTabButton == null || this.weeklyTabButton == null) return;

        GameObject t_daily = this.dailyListRoot != null ? this.dailyListRoot : this.dailyContent != null ? this.dailyContent.gameObject : null;
        GameObject t_weekly = this.weeklyListRoot != null ? this.weeklyListRoot : this.weeklyContent != null ? this.weeklyContent.gameObject : null;
        if (t_daily != null) t_daily.SetActive(!this.m_weeklyTab);
        if (t_weekly != null) t_weekly.SetActive(this.m_weeklyTab);

        if (this.dailyEmptyNotice != null) this.dailyEmptyNotice.SetActive(!this.m_weeklyTab && this.m_dailyRows.Count == 0);
        if (this.weeklyEmptyNotice != null) this.weeklyEmptyNotice.SetActive(this.m_weeklyTab && this.m_weeklyRows.Count == 0);

        // 선택 표시는 그림 틴트 하나다. Button 의 ColorTint 전이는 canvasRenderer 색을 곱하므로 여기와 충돌하지 않는다.
        if (this.dailyTabButton.targetGraphic != null) this.dailyTabButton.targetGraphic.color = this.m_weeklyTab ? this.tabDimColor : Color.white;
        if (this.weeklyTabButton.targetGraphic != null) this.weeklyTabButton.targetGraphic.color = this.m_weeklyTab ? Color.white : this.tabDimColor;
    }

    void BuildSection(Transform _content, MissionRowView _rowPrefab, List<MissionRowView> _rows,
                      string _period, GameObject _emptyNotice)
    {
        _rows.Clear();
        if (_content == null || _rowPrefab == null) return;

        // Destroy 는 프레임 끝에 처리되므로 먼저 비활성화한다 — 레이아웃 계산에서 빠져야 이번 프레임 배치가 맞는다.
        for (int i = _content.childCount - 1; i >= 0; i--)
        {
            GameObject t_child = _content.GetChild(i).gameObject;
            t_child.SetActive(false);
            Destroy(t_child);
        }

        IReadOnlyList<MissionDefinition> t_definitions = MissionManager.Definitions;
        for (int i = 0; i < t_definitions.Count; i++)
        {
            MissionDefinition t_definition = t_definitions[i];
            // 가이드 미션은 전용 화면(GuideMissionPanel)이 그린다 — 여기서는 정확히 해당 주기만.
            if (!string.Equals(t_definition.Period, _period, StringComparison.Ordinal)) continue;

            MissionRowView t_row = Instantiate(_rowPrefab, _content);
            t_row.gameObject.SetActive(true);
            t_row.Bind(t_definition, this.HandleClaim);
            _rows.Add(t_row);
        }

        if (_emptyNotice != null) _emptyNotice.SetActive(_rows.Count == 0);
    }

    void RefreshRows()
    {
        for (int i = 0; i < this.m_dailyRows.Count; i++)
            if (this.m_dailyRows[i] != null) this.m_dailyRows[i].Refresh();
        for (int i = 0; i < this.m_weeklyRows.Count; i++)
            if (this.m_weeklyRows[i] != null) this.m_weeklyRows[i].Refresh();
        this.RefreshClaimAllButton();
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
        finally
        {
            // 팝업보다 먼저 걷는다 — ClaimAsync 와 같은 계약.
            ServerWaitOverlay.Release(this);
            this.m_claimingAll = false;
            this.RefreshClaimAllButton();
        }
        if (t_results.Exists(t_result => (t_result.Cards?.Count ?? 0) > 0)) this.Close();
        ShowClaimedRewards(t_results);
    }

    // 정의 목록의 신원. id 와 순서가 그대로면 다시 깔 이유가 없다.
    static string BuildSignature()
    {
        IReadOnlyList<MissionDefinition> t_definitions = MissionManager.Definitions;
        if (t_definitions.Count == 0) return string.Empty;

        var t_builder = new System.Text.StringBuilder(t_definitions.Count * 24);
        for (int i = 0; i < t_definitions.Count; i++) t_builder.Append(t_definitions[i].Id).Append(MissionManager.IsClaimed(t_definitions[i].Id)).Append('|');
        return t_builder.ToString();
    }

    void HandleClaim(string _missionId)
    {
        this.ClaimAsync(_missionId).Forget();
    }

    async UniTaskVoid ClaimAsync(string _missionId)
    {
        // 왕복 동안 입력을 막는다. 딤·스피너는 임계 뒤에만 뜨므로 빠른 응답에서는 깜빡이지 않는다.
        ClaimMissionResult t_result = null;
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
        }
        if (t_result != null)
        {
            // 개봉 화면보다 높은 미션 패널을 먼저 걷는다.
            if ((t_result.Cards?.Count ?? 0) > 0) this.Close();
            ShowClaimedRewards(new[] { t_result });
        }
    }

    // GuideMissionPanel 도 같은 보상 표시 경로를 쓴다 — 미션 보상 팝업 조립의 단일 지점.
    internal static void ShowClaimedRewards(IReadOnlyList<ClaimMissionResult> _results, string _title = "미션 보상")
    {
        if (_results.Count == 0) return;

        var t_bucket = new CurrencyGainBucket();
        var t_cards = new List<OpenPackCard>();
        var t_packs = new List<ClaimRewardPack>();
        long t_passExp = 0;
        for (int i = 0; i < _results.Count; i++)
        {
            var t_result = _results[i];
            if (t_result.Granted != null)
                foreach (var t_gain in t_result.Granted)
                    if (t_gain != null && CurrencyCode.TryParse(t_gain.Currency, out var t_type))
                        t_bucket.Add(t_type, t_gain.Amount);
            if (t_result.Cards != null) t_cards.AddRange(t_result.Cards);
            if (t_result.Packs != null) t_packs.AddRange(t_result.Packs);
            t_passExp += t_result.GrantedPassExp;
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
        var t_outcome = RewardItemDisplay.ToOutcome(t_gains, t_cards, t_packs);
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
        if (RewardClaimPopup.TryGet(out var t_popup) && t_popup.RewardSlotCount > 0)
        {
            string t_title = t_passExp > 0 ? $"{_title} · 패스 경험치 +{t_passExp:N0}" : _title;
            ShowRewardPage(t_popup, t_title, t_lines, t_outcome, 0);
        }
        else
            RewardClaimPopup.ClaimWithoutPopup(() => UniTask.FromResult(t_outcome)).Forget();
    }

    static void ShowRewardPage(RewardClaimPopup _popup, string _title, List<RewardLine> _lines,
                               RewardClaimOutcome _outcome, int _offset)
    {
        int t_count = Math.Min(_popup.RewardSlotCount, _lines.Count - _offset);
        int t_next = _offset + t_count;
        bool t_hasNext = t_next < _lines.Count;
        // 카드 상세는 마지막 페이지를 확인한 뒤에만 연다.
        var t_pageOutcome = new RewardClaimOutcome(_outcome.Granted, t_hasNext ? null : _outcome.Cards,
            t_hasNext ? null : _outcome.Packs, t_hasNext ? null : _outcome.PresentationBatches);
        int t_pages = Math.Max(1, (_lines.Count + _popup.RewardSlotCount - 1) / _popup.RewardSlotCount);
        string t_title = t_pages > 1 ? $"{_title} ({_offset / _popup.RewardSlotCount + 1}/{t_pages})" : _title;
        _popup.Show(t_title, _lines.GetRange(_offset, t_count), () => UniTask.FromResult(t_pageOutcome),
            _claimOnDim: true,
            _onClosed: t_hasNext ? () => ShowRewardPage(_popup, _title, _lines, _outcome, t_next) : (Action)null);
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

    // 여는 순간 오버레이 자신을 켠다 — 저작본은 루트가 꺼진 채로 들어오므로, 켜 주지 않으면
    // 하위 Root 만 토글돼 화면에 아무것도 뜨지 않는다(RankRewardPanel 과 같은 규약).
    void SetVisible(bool _visible)
    {
        if (_visible && !this.gameObject.activeSelf) this.gameObject.SetActive(true);

        if (_visible) ScreenDim.Show(this, this.dimAlpha, true, this.transition.OpenDuration);
        else ScreenDim.Hide(this);

        this.isShow = _visible;
        this.transition.SetVisible(this.ResolveTarget(), _visible);
    }

    GameObject ResolveTarget() => this.root != null ? this.root : this.gameObject;
}
