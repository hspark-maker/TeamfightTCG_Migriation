using System;
using System.Globalization;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>동일한 기록 기간의 전적과 이전 업적에서 이어받은 누적 기록을 구분한다.</summary>
public sealed class PlayerStatisticsPanel : ContentsPooledUI
{
    [SerializeField] GameObject statisticsContent;
    [SerializeField] TMP_Text periodText;
    [SerializeField] TMP_Text[] battleValueTexts;
    [SerializeField] TMP_Text[] lifetimeValueTexts;
    [SerializeField] Sprite selectedTabSprite;
    [SerializeField] Sprite unselectedTabSprite;
    [SerializeField] TMP_Text statusText;
    [SerializeField] Button allButton;
    [SerializeField] Button rankedButton;
    [SerializeField] Button adventureButton;
    [SerializeField] Button retryButton;
    [SerializeField] Button closeButton;
    [SerializeField] Button dimButton;
    [SerializeField] ScrollRect scroll;

    int m_mode;
    bool m_refreshing;
    string m_userId;
    string m_env;

    public override void Initialization(UIData _data) { InitializeUI(); data = _data; }

    public override void Show()
    {
        m_userId = FirebaseAuthService.Instance.UserId;
        m_env = ContentProfileConfig.Active?.CloudEnvId;
        m_mode = 0;
        SetContentsVisible(true);
        data?.showCustomMethod?.Invoke();
        Render();
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        RefreshAsync().Forget();
    }

    public override void Hide()
    {
        if (!isShow) return;
        SetContentsVisible(false);
        if (IsCurrentAccount()) data?.onHide?.Invoke();
    }

    protected override void OnInitializeUI()
    {
        closeButton?.onClick.AddListener(Hide);
        dimButton?.onClick.AddListener(Hide);
        allButton?.onClick.AddListener(() => SelectMode(0));
        rankedButton?.onClick.AddListener(() => SelectMode(1));
        adventureButton?.onClick.AddListener(() => SelectMode(2));
        retryButton?.onClick.AddListener(() => RefreshAsync().Forget());
    }

    protected override void OnViewShown() => PlayerStatisticsManager.OnChanged += Render;
    protected override void OnViewHidden() => PlayerStatisticsManager.OnChanged -= Render;

    bool IsCurrentAccount() => m_userId == FirebaseAuthService.Instance.UserId &&
        m_env == ContentProfileConfig.Active?.CloudEnvId;

    void SelectMode(int _mode)
    {
        if (m_mode == _mode) return;
        m_mode = _mode;
        Render();
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
    }

    async UniTaskVoid RefreshAsync()
    {
        if (m_refreshing) return;
        m_refreshing = true;
        int t_visibility = VisibilityVersion;
        string t_userId = m_userId;
        string t_env = m_env;
        SetStatus("전적을 불러오는 중…", false);
        try
        {
            bool t_loaded = await PlayerStatisticsCommands.RefreshAsync(_force: true);
            if (this == null || !isShow || t_visibility != VisibilityVersion ||
                t_userId != m_userId || t_env != m_env || !IsCurrentAccount()) return;
            Render();
            if (!t_loaded)
                SetStatus(PlayerStatisticsManager.IsReady
                    ? "최신 기록을 확인하지 못했습니다."
                    : "전적을 불러오지 못했습니다.", true);
        }
        finally
        {
            m_refreshing = false;
            if (this != null && isShow && IsCurrentAccount() &&
                (t_visibility != VisibilityVersion || t_userId != m_userId || t_env != m_env))
                RefreshAsync().Forget();
        }
    }

    void Render()
    {
        if (!isShow) return;
        bool t_ready = IsCurrentAccount() && PlayerStatisticsManager.IsReady;
        statisticsContent.SetActive(t_ready);
        periodText.gameObject.SetActive(t_ready);
        ApplyTab(allButton, 0, t_ready);
        ApplyTab(rankedButton, 1, t_ready);
        ApplyTab(adventureButton, 2, t_ready);
        if (!t_ready)
        {
            SetStatus("전적을 불러오는 중…", false);
            return;
        }

        RenderSnapshot(PlayerStatisticsManager.Snapshot);
        SetStatus(null, false);
    }

    void ApplyTab(Button _button, int _mode, bool _ready)
    {
        _button.interactable = _ready;
        _button.image.sprite = m_mode == _mode ? selectedTabSprite : unselectedTabSprite;
        _button.image.color = m_mode == _mode ? Color.white : new Color(0.95f, 0.94f, 0.92f, 1f);
    }

    void RenderSnapshot(PlayerStatisticsSnapshot _snapshot)
    {
        PlayerBattleStatistics t_battle = m_mode == 1 ? _snapshot.Battle.Ranked
            : m_mode == 2 ? _snapshot.Battle.Adventure : _snapshot.Battle.All;
        PlayerLifetimeStatistics t_lifetime = _snapshot.Lifetime;
        periodText.text = _snapshot.TrackedSinceMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(_snapshot.TrackedSinceMs)
                .ToLocalTime().ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) + " 이후 전적"
            : "기록 시작 이후 전적";
        string[] t_battleValues =
        {
            Number(t_battle.Battles), Number(t_battle.Wins), Number(t_battle.Losses),
            Number(t_battle.Draws), t_battle.WinRatePercent.HasValue
                ? t_battle.WinRatePercent.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—",
            Number(t_battle.CurrentWinStreak), Number(t_battle.BestWinStreak),
            Number(t_battle.CardsDestroyed), Number(t_battle.Attacks), Number(t_battle.DamageDealt),
            Number(t_battle.Healed), Number(t_battle.SynergyTriggers),
        };
        string[] t_lifetimeValues =
        {
            Number(t_lifetime.Wins), Number(t_lifetime.CardsDestroyed), Number(t_lifetime.BestWinStreak),
            Number(t_lifetime.PacksOpened), Number(t_lifetime.AlbumsCompleted),
        };
        for (int i = 0; i < battleValueTexts.Length; i++) battleValueTexts[i].text = t_battleValues[i];
        for (int i = 0; i < lifetimeValueTexts.Length; i++) lifetimeValueTexts[i].text = t_lifetimeValues[i];
    }

    static string Number(long _value) => _value.ToString("N0", CultureInfo.InvariantCulture);

    void SetStatus(string _message, bool _retry)
    {
        statusText.text = _message ?? string.Empty;
        statusText.gameObject.SetActive(!string.IsNullOrEmpty(_message));
        retryButton.gameObject.SetActive(_retry);
    }
}
