using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 배틀패스 화면(PassOverlay 에 부착). 풀(UIPoolManager)이 수명을 쥔다 —
/// 규약은 <see cref="MissionPanel"/> 과 같다.
///
/// <para><b>시즌·곡선·보상을 이 화면이 들고 있지 않는다.</b> 전부 서버 <c>getPass</c> 응답이고
/// 레벨 판정도 서버 값이다. 화면이 사본을 두면 밸런스 수정이 앱 배포에 묶인다.</para>
///
/// <para>게임 시작 때 미리 받은 캐시로 즉시 그리고
/// <see cref="PassManager.OnChanged"/> 가 오면 다시 그린다.</para>
/// </summary>
public partial class PassPanel : PooledUIBase
{
    // 풀 계약. 표시 데이터는 PassManager 에서 스스로 당기므로 UIData 가 필요 없다.
    public override void Initialization(UIData _data) { }

    public override void Show() => this.Open();

    public override void Hide() => this.Close();

    [Tooltip("켜고 끌 대상(딤 + 패널). 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [Header("시즌 머리")]
    [Tooltip("시즌 표시명. 시즌이 없으면 안내 문구로 바뀐다.")]
    [SerializeField] TMP_Text seasonText;

    [Tooltip("남은 기간. 비워 두면 그리지 않는다.")]
    [SerializeField] TMP_Text remainText;

    [Tooltip("현재 레벨 표시.")]
    [SerializeField] TMP_Text levelText;

    [Tooltip("경험치 표시(현재/다음 문턱).")]
    [SerializeField] TMP_Text expText;

    [Tooltip("Sliced 채움 이미지. 최대 영역 부모 안에서 너비로 진행도를 표시한다.")]
    [SerializeField] Image expFill;

    [Header("목록")]
    [Tooltip("레벨 행이 쌓일 Content(VerticalLayoutGroup).")]
    [SerializeField] Transform levelContent;

    [Tooltip("독립 패스 레벨 행 프리팹.")]
    [SerializeField] PassLevelRowView rowPrefab;

    [Tooltip("활성 시즌이 없거나 아직 조회 전일 때 켤 안내.")]
    [SerializeField] GameObject emptyNotice;

    [Header("버튼")]
    [SerializeField] Button closeButton;

    [SerializeField] Button claimAllButton;
    [SerializeField] GameObject claimAllAlertDot;

    [Tooltip("패널 밖(딤)을 눌러 닫는 판. 알파 0 Image 의 Button 에 배선한다.")]
    [SerializeField] Button dimButton;

    [Header("연출")]
    [Tooltip("panel 에는 Root/Panel 을 배선한다 — root 를 물리면 전체화면 딤까지 함께 커진다.")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("공용 ScreenDim(Full)에 요청할 암막 짙기.")]
    [Range(0f, 1f)] [SerializeField] float dimAlpha = 0.72f;

    readonly List<PassLevelRowView> m_rows = new List<PassLevelRowView>();
    Action<int> m_claimHandler;
    Action<int> m_premiumClaimHandler;

    bool m_claiming;
    bool m_scrollOnOpen;
    bool m_waitForOpeningRefresh;
    int m_openGeneration;

    // 씬 버튼 UnityEvent 가 인자 없는 이 시그니처에 바인딩된다 — 매개변수를 붙이면 배선이 끊긴다.
    public void Open()
    {
        this.m_scrollOnOpen = true;
        this.m_openGeneration++;
        this.m_waitForOpeningRefresh = false;
        this.SetVisible(true);
        this.Rebuild();

        // 선조회 실패·진행도 변경·시즌 경계만 재조회한다. 시즌 판정은 서버 응답을 따른다.
        if (PassCommands.NeedsRefresh || !PassManager.HasSeason ||
            (PassManager.Season.EndAtMs > 0L &&
             PassManager.Season.EndAtMs <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
        {
            this.m_waitForOpeningRefresh = true;
            this.RefreshOpeningAsync(this.m_openGeneration).Forget();
        }
    }

    async UniTaskVoid RefreshOpeningAsync(int _generation)
    {
        try
        {
            await UniTask.WaitUntil(() => (!PassCommands.IsRefreshing && !PassCommands.IsRepeatInFlight) || this == null || !this.isShow,
                cancellationToken: this.GetCancellationTokenOnDestroy());
            if (this == null || !this.isShow || _generation != this.m_openGeneration) return;
            await PassCommands.RefreshAsync();
        }
        finally
        {
            if (this != null && _generation == this.m_openGeneration) this.m_waitForOpeningRefresh = false;
        }
    }

    public void Close()
    {
        this.m_scrollOnOpen = false;
        this.m_openGeneration++;
        this.SetVisible(false);
    }

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

        PassManager.OnChanged += this.HandlePassChanged;
        this.BindExtraButtons();
    }

    void OnDisable()
    {
        this.m_scrollOnOpen = false;
        PassManager.OnChanged -= this.HandlePassChanged;

        // 안전망 — Close 를 거치지 않고 꺼지면 공용 딤이 남는다.
        ScreenDim.Hide(this);
        this.transition.HandleDisabled(this.ResolveTarget());
    }

    void Update()
    {
        // 남은 기간만 매 프레임 갱신한다. 행 다시 그리기는 OnChanged 가 유발한다.
        if (!this.isShow) return;
        this.RefreshRemainLabel(PassManager.Season);
        if (Time.unscaledTime >= this.m_nextExtrasRefresh)
        {
            this.m_nextExtrasRefresh = Time.unscaledTime + 1f;
            this.RefreshHeader();
        }
    }

    void HandlePassChanged() => this.Rebuild();

    void LateUpdate()
    {
        if (!this.m_scrollOnOpen || this.m_waitForOpeningRefresh || !this.isShow || !PassManager.HasSeason || this.levelContent == null) return;
        int t_index = FindOpeningRowIndex();
        if (t_index < 0 || t_index >= this.m_rows.Count || this.m_rows[t_index] == null) return;
        Transform t_targetTransform = this.m_rows[t_index].transform;
        if (this.repeatRoot != null && this.repeatRoot.activeSelf && PassManager.Exp >= PassManager.MaxRequiredExp
            && !HasUnclaimedLevel()) t_targetTransform = this.repeatRoot.transform;
        ScrollRect t_scroll = this.levelContent.GetComponentInParent<ScrollRect>();
        if (t_scroll == null || t_scroll.content == null)
        {
            this.m_scrollOnOpen = false;
            return;
        }

        // 비동기 첫 조회로 생긴 행도 레이아웃 크기가 확정된 뒤 한 번만 맞춘다.
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(t_scroll.content);
        RectTransform t_viewport = t_scroll.viewport != null ? t_scroll.viewport : (RectTransform)t_scroll.transform;
        float t_viewHeight = t_viewport.rect.height;
        if (t_viewHeight <= 0f) return;
        Bounds t_target = RectTransformUtility.CalculateRelativeRectTransformBounds(
            t_scroll.content, t_targetTransform);
        float t_scrollHeight = t_scroll.content.rect.height - t_viewHeight;
        t_scroll.StopMovement();
        t_scroll.verticalNormalizedPosition = t_scrollHeight > 0f
            ? Mathf.Clamp01((t_target.center.y - t_scroll.content.rect.yMin - t_viewHeight * 0.5f) / t_scrollHeight)
            : 1f;
        this.m_scrollOnOpen = false;
    }

    static int FindOpeningRowIndex()
    {
        int t_index = 0;
        int t_next = -1;
        foreach (PassLevelDefinition t_level in PassManager.Levels)
        {
            if (t_level == null) continue;
            if (PassManager.CanClaim(t_level) || PassManager.CanClaimPremium(t_level)) return t_index;
            if (t_next < 0 && t_level.RequiredExp > PassManager.Exp) t_next = t_index;
            t_index++;
        }
        return t_next >= 0 ? t_next : t_index - 1;
    }

    void Rebuild()
    {
        this.BuildRows();
        this.RefreshHeader();
    }

    void BuildRows()
    {
        if (this.levelContent == null || this.rowPrefab == null) return;

        if (this.rowPrefab.transform.parent == this.levelContent) this.rowPrefab.gameObject.SetActive(false);

        int t_count = 0;
        this.m_claimHandler ??= this.HandleClaim;
        this.m_premiumClaimHandler ??= this.HandlePremiumClaim;
        IReadOnlyList<PassLevelDefinition> t_levels = PassManager.Levels;
        for (int i = 0; i < t_levels.Count; i++)
        {
            PassLevelDefinition t_level = t_levels[i];
            if (t_level == null) continue;

            if (t_count == this.m_rows.Count) this.m_rows.Add(null);
            PassLevelRowView t_row = this.m_rows[t_count];
            if (t_row == null) this.m_rows[t_count] = t_row = Instantiate(this.rowPrefab, this.levelContent);
            long? t_next = i + 1 < t_levels.Count ? t_levels[i + 1]?.RequiredExp : null;
            t_row.Bind(t_level, t_next, this.m_claimHandler, this.m_premiumClaimHandler);
            if (!t_row.gameObject.activeSelf) t_row.gameObject.SetActive(true);
            t_count++;
        }

        for (int i = t_count; i < this.m_rows.Count; i++)
            if (this.m_rows[i] != null && this.m_rows[i].gameObject.activeSelf) this.m_rows[i].gameObject.SetActive(false);
        if (this.emptyNotice != null) this.emptyNotice.SetActive(t_count == 0);
        if (this.repeatRoot != null) this.repeatRoot.transform.SetAsLastSibling();
    }

    void RefreshHeader()
    {
        PassSeasonDefinition t_season = PassManager.Season;

        if (this.seasonText != null)
            this.seasonText.text = t_season != null ? t_season.DisplayName
                : PassManager.IsReady ? "진행 중인 시즌이 없다" : "불러오는 중…";

        if (this.levelText != null)
            this.levelText.text = t_season != null
                ? PassManager.CurrentLevel.ToString()
                : string.Empty;

        long t_exp = PassManager.Exp;
        long? t_next = PassManager.NextRequiredExp;
        long t_floor = FloorOf(t_exp);
        if (this.expText != null)
            this.expText.text = t_season == null ? string.Empty
                : t_next.HasValue ? $"{t_exp - t_floor:N0} / {t_next.Value - t_floor:N0}"
                : PassManager.HasRepeatReward ? $"{PassManager.RepeatProgressExp:N0} / {PassManager.Repeat.RequiredExp:N0}" : "MAX";

        if (this.expFill != null)
        {
            float t_fill = t_season == null ? 0f : FillOf(t_exp, t_next);
            if (t_season != null && !t_next.HasValue && PassManager.HasRepeatReward)
                t_fill = (float)PassManager.RepeatProgressExp / PassManager.Repeat.RequiredExp;
            this.expFill.rectTransform.anchorMax = new Vector2(t_fill, 1f);
            this.expFill.gameObject.SetActive(t_fill > 0f);
        }
        bool t_claimable = !this.m_claiming && PassManager.HasAnyClaimable;
        if (this.claimAllButton != null) this.claimAllButton.interactable = t_claimable;
        if (this.claimAllAlertDot != null) this.claimAllAlertDot.SetActive(t_claimable);

        this.RefreshRemainLabel(t_season);
        this.RefreshExtras();
    }

    // 게이지는 현재 레벨 문턱과 다음 문턱 사이 비율이다 — 0 부터 재면 뒷레벨에서 거의 안 움직인다.
    static float FillOf(long _exp, long? _next)
    {
        if (!_next.HasValue) return 1f;

        long t_floor = FloorOf(_exp);
        long t_span = _next.Value - t_floor;
        if (t_span <= 0L) return 1f;
        return Mathf.Clamp01((float)(_exp - t_floor) / t_span);
    }

    static long FloorOf(long _exp)
    {
        long t_floor = 0L;
        IReadOnlyList<PassLevelDefinition> t_levels = PassManager.Levels;
        for (int i = 0; i < t_levels.Count; i++)
        {
            PassLevelDefinition t_level = t_levels[i];
            if (t_level == null || t_level.RequiredExp > _exp) break;
            t_floor = t_level.RequiredExp;
        }

        return t_floor;
    }

    // 종료 시각의 진실원은 서버가 준 epoch ms 다. 남은 시간 표시에만 기기 시계를 쓴다 —
    // 실제 시즌 판정은 서버가 하므로 시계를 돌려도 시즌이 바뀌지 않는다.
    void RefreshRemainLabel(PassSeasonDefinition _season)
    {
        if (this.remainText == null) return;
        if (_season == null || _season.EndAtMs <= 0L)
        {
            this.remainText.text = string.Empty;
            return;
        }

        long t_remainMs = _season.EndAtMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (t_remainMs < 0L) t_remainMs = 0L;

        var t_span = TimeSpan.FromMilliseconds(t_remainMs);
        this.remainText.text = t_span.TotalDays >= 1d
            ? $"{(int)t_span.TotalDays}일 {t_span.Hours}시간 남음"
            : $"{(int)t_span.TotalHours}시간 {t_span.Minutes}분 남음";
    }

    readonly struct ClaimTarget
    {
        internal readonly int Level;
        internal readonly bool Premium;
        internal ClaimTarget(int _level, bool _premium = false) { Level = _level; Premium = _premium; }
    }

    void HandleClaim(int _level) => this.ClaimAsync(new List<ClaimTarget> { new ClaimTarget(_level) }).Forget();
    void HandlePremiumClaim(int _level) => this.ClaimAsync(new List<ClaimTarget> { new ClaimTarget(_level, true) }).Forget();

    void HandleClaimAll()
    {
        var t_levels = new List<ClaimTarget>();
        foreach (PassLevelDefinition t_level in PassManager.Levels)
        {
            if (PassManager.CanClaim(t_level)) t_levels.Add(new ClaimTarget(t_level.Level));
            if (PassManager.CanClaimPremium(t_level)) t_levels.Add(new ClaimTarget(t_level.Level, true));
        }
        this.ClaimAsync(t_levels, true).Forget();
    }

    async UniTaskVoid ClaimAsync(List<ClaimTarget> _levels, bool _includeRepeat = false)
    {
        if (this.m_claiming || (_levels.Count == 0 && (!_includeRepeat || !PassManager.CanClaimRepeat))) return;
        this.m_claiming = true;
        this.RefreshHeader();
        string t_season = PassManager.Season?.SeasonId;
        var t_rewards = new List<ClaimMissionResult>();
        bool t_continue = true;
        try
        {
            foreach (ClaimTarget t_target in _levels)
            {
                if (PassManager.Season?.SeasonId != t_season) break;
                PassLevelDefinition t_definition = null;
                foreach (PassLevelDefinition t_level in PassManager.Levels)
                    if (t_level != null && t_level.Level == t_target.Level) { t_definition = t_level; break; }
                if (!(t_target.Premium ? PassManager.CanClaimPremium(t_definition) : PassManager.CanClaim(t_definition))) continue;

                string t_selected = null;
                List<ClaimRewardItem> t_items = t_target.Premium ? t_definition.PremiumItems : t_definition.Items;
                if (t_items != null && t_items.Exists(t_item => t_item != null && t_item.RewardType == "PackChoice"))
                {
                    t_selected = await RewardPackChoice.ChooseAsync(PassManager.PackChoices);
                    if (string.IsNullOrEmpty(t_selected)) { t_continue = false; break; }
                }

                ClaimPassRewardResult t_result;
                ServerWaitOverlay.Hold(this);
                try { t_result = await PassCommands.ClaimAsync(t_target.Level, t_selected, t_target.Premium); }
                finally { ServerWaitOverlay.Release(this); }
                // 실패 이후 요청을 계속 보내지 않는다. 앞서 성공한 보상은 아래에서 표시한다.
                if (t_result == null) { t_continue = false; break; }
                t_rewards.Add(new ClaimMissionResult { Granted = t_result.Granted, Cards = t_result.Cards, Packs = t_result.Packs });
            }
            if (_includeRepeat && t_continue && PassManager.Season?.SeasonId == t_season && PassManager.CanClaimRepeat)
            {
                ServerWaitOverlay.Hold(this);
                try
                {
                    ClaimPassRepeatRewardResult t_result = await PassCommands.ClaimRepeatAsync();
                    if (t_result != null) t_rewards.Add(new ClaimMissionResult { Granted = t_result.Granted });
                }
                finally { ServerWaitOverlay.Release(this); }
            }
        }
        finally
        {
            this.m_claiming = false;
            this.RefreshHeader();
            if (t_rewards.Exists(t_reward => (t_reward.Cards?.Count ?? 0) > 0)) this.Close();
            if (t_rewards.Count > 0) MissionPanel.ShowClaimedRewards(t_rewards, "패스 보상");
        }
    }

    // 여는 순간 오버레이 자신을 켠다 — 저작본은 루트가 꺼진 채로 들어오므로, 켜 주지 않으면
    // 하위 Root 만 토글돼 화면에 아무것도 뜨지 않는다(MissionPanel 과 같은 규약).
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
