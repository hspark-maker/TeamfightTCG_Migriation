using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>영구 업적. 각 업적의 다음 미수령 단계 하나만 보여 준다.</summary>
public sealed class AchievementPanel : ContentsPooledUI
{
    [SerializeField] Transform listContent;
    [SerializeField] AchievementRowView rowPrefab;
    [SerializeField] TMP_Text statusText;
    [SerializeField] Button closeButton;
    [SerializeField] Button dimButton;
    [SerializeField] TMP_Text summaryText;
    [SerializeField] TMP_Text availableText;
    [SerializeField] Button[] categoryButtons;
    [SerializeField] Sprite selectedTabSprite;
    [SerializeField] Sprite unselectedTabSprite;

    readonly List<AchievementRowView> m_rows = new();
    readonly List<AchievementDefinition> m_visible = new();
    readonly Dictionary<string, AchievementDefinition> m_groups = new();
    bool m_refreshing;
    int m_category;

    public override void Initialization(UIData _data) { InitializeUI(); data = _data; }
    public override void Show()
    {
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission)) return;
        SetContentsVisible(true);
        Rebuild();
        RefreshAsync().Forget();
    }
    public override void Hide() => SetContentsVisible(false);

    protected override void OnInitializeUI()
    {
        closeButton?.onClick.AddListener(Hide);
        dimButton?.onClick.AddListener(Hide);
        for (int i = 0; i < (categoryButtons?.Length ?? 0); i++)
        {
            int t_category = i;
            categoryButtons[i].onClick.AddListener(() => SelectCategory(t_category));
        }
    }
    protected override void OnViewShown() => AchievementManager.OnChanged += Rebuild;
    protected override void OnViewHidden() => AchievementManager.OnChanged -= Rebuild;

    async UniTaskVoid RefreshAsync()
    {
        if (m_refreshing) return;
        m_refreshing = true;
        int t_visibility = VisibilityVersion;
        string t_userId = FirebaseAuthService.Instance.UserId;
        string t_env = ContentProfileConfig.Active?.CloudEnvId;
        if (m_visible.Count == 0) SetStatus("업적을 불러오는 중…");
        try
        {
            bool t_loaded = await AchievementCommands.RefreshAsync(_force: true);
            if (this == null || !isShow || t_visibility != VisibilityVersion ||
                t_userId != FirebaseAuthService.Instance.UserId || t_env != ContentProfileConfig.Active?.CloudEnvId) return;
            Rebuild();
            if (!t_loaded && !AchievementManager.IsReady)
                SetStatus("업적을 불러오지 못했습니다. 잠시 후 다시 열어 주세요.");
        }
        finally
        {
            m_refreshing = false;
            // 조회 중 닫았다가 다시 열었으면 새 표시 회차도 성공/실패를 안내받아야 한다.
            if (this != null && isShow && (t_visibility != VisibilityVersion ||
                t_userId != FirebaseAuthService.Instance.UserId || t_env != ContentProfileConfig.Active?.CloudEnvId))
                RefreshAsync().Forget();
        }
    }

    void Rebuild()
    {
        if (!isShow) return;
        m_groups.Clear();
        foreach (var t_definition in AchievementManager.Definitions)
        {
            if (!m_groups.TryGetValue(t_definition.GroupId, out var t_current)
                || PreferStage(t_definition, t_current))
                m_groups[t_definition.GroupId] = t_definition;
        }
        m_visible.Clear();
        foreach (var t_definition in m_groups.Values)
        {
            bool t_collection = t_definition.Event == "OpenPack" || t_definition.Event == "CompleteAlbum";
            if (m_category == 0 || (m_category == 2) == t_collection) m_visible.Add(t_definition);
        }
        m_visible.Sort(CompareDisplayOrder);
        int t_claimed = 0;
        int t_available = 0;
        foreach (var t_definition in AchievementManager.Definitions)
        {
            if (AchievementManager.IsClaimed(t_definition.Id)) t_claimed++;
            else if (AchievementManager.CanClaim(t_definition)) t_available++;
        }
        if (summaryText != null) summaryText.text = $"{t_claimed:N0} <size=65%>/ {AchievementManager.Definitions.Count:N0}</size>";
        if (availableText != null) availableText.text = t_available > 0 ? $"받을 보상 {t_available:N0}개" : "도전은 계속됩니다!";
        UpdateTabs();
        for (int i = 0; i < m_visible.Count; i++)
        {
            if (i == m_rows.Count) m_rows.Add(Instantiate(rowPrefab, listContent));
            m_rows[i].Bind(m_visible[i], HandleClaim);
            m_rows[i].gameObject.SetActive(true);
        }
        for (int i = m_visible.Count; i < m_rows.Count; i++) m_rows[i].gameObject.SetActive(false);
        SetStatus(m_visible.Count == 0 ? "표시할 업적이 없습니다." : null);
    }

    void SelectCategory(int _category)
    {
        if (m_category == _category) return;
        m_category = _category;
        Rebuild();
        if (listContent != null && listContent.GetComponentInParent<ScrollRect>() is { } t_scroll)
            t_scroll.verticalNormalizedPosition = 1f;
    }

    void UpdateTabs()
    {
        for (int i = 0; i < (categoryButtons?.Length ?? 0); i++)
            categoryButtons[i].image.sprite = i == m_category ? selectedTabSprite : unselectedTabSprite;
    }

    // 미수령 중 가장 낮은 단계, 모두 수령했다면 가장 높은 단계.
    static bool PreferStage(AchievementDefinition _candidate, AchievementDefinition _current)
    {
        bool t_candidateClaimed = AchievementManager.IsClaimed(_candidate.Id);
        bool t_currentClaimed = AchievementManager.IsClaimed(_current.Id);
        if (t_candidateClaimed != t_currentClaimed) return !t_candidateClaimed;
        return t_candidateClaimed ? _candidate.Stage > _current.Stage : _candidate.Stage < _current.Stage;
    }

    static int CompareDisplayOrder(AchievementDefinition _left, AchievementDefinition _right)
    {
        int t_claimable = AchievementManager.CanClaim(_right).CompareTo(AchievementManager.CanClaim(_left));
        if (t_claimable != 0) return t_claimable;
        int t_claimed = AchievementManager.IsClaimed(_left.Id).CompareTo(AchievementManager.IsClaimed(_right.Id));
        if (t_claimed != 0) return t_claimed;
        int t_order = _left.SortOrder.CompareTo(_right.SortOrder);
        return t_order != 0 ? t_order : string.CompareOrdinal(_left.GroupId, _right.GroupId);
    }

    void SetStatus(string _text)
    {
        if (statusText == null) return;
        statusText.text = _text ?? string.Empty;
        statusText.gameObject.SetActive(!string.IsNullOrEmpty(_text));
    }

    void HandleClaim(AchievementDefinition _definition) => ClaimAsync(_definition).Forget();

    async UniTaskVoid ClaimAsync(AchievementDefinition _definition)
    {
        if (!AchievementManager.CanClaim(_definition)) return;
        var t_session = PlayerSaveCloud.CaptureCommandSession();
        int t_visibility = VisibilityVersion;
        RewardClaimOutcome t_outcome;
        ServerWaitOverlay.Hold(this);
        try
        {
            t_outcome = await AchievementCommands.ClaimAsync(_definition.Id);
        }
        finally
        {
            // 실패 안내와 보상 팝업이 대기 화면 아래에 가려지지 않게 먼저 해제한다.
            ServerWaitOverlay.Release(this);
        }
        // 다른 계정이나 다시 열린 화면에 이전 요청의 안내를 띄우지 않는다.
        if (this == null || !isShow || t_visibility != VisibilityVersion ||
            !PlayerSaveCloud.IsCommandSessionCurrent(t_session)) return;
        if (!t_outcome.Succeeded)
        {
            UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
            {
                titleText = "보상 수령 상태를 확인하지 못했습니다.\n잠시 후 다시 확인해 주세요.",
                yesText = "확인",
                noText = "닫기",
            });
            return;
        }

        // 표시 목록도 서버가 확정한 실지급량만 사용한다. 팝업 확인은 재지급을 요청하지 않는다.
        var t_lines = new List<RewardLine>();
        foreach (var t_gain in t_outcome.Granted) t_lines.Add(new RewardLine(t_gain));
        if (t_lines.Count > 0 && RewardClaimPopup.TryGet(out var t_popup))
            t_popup.Show(_definition.Title, t_lines,
                () => UniTask.FromResult(new RewardClaimOutcome(t_outcome.Granted)),
                _onClosed: () => RewardPackPresentation.Show(t_outcome));
        else
            RewardPackPresentation.Show(t_outcome);
    }
}
