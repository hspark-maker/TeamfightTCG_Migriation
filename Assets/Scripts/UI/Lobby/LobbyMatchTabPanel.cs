using System;
using TMPro;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Owns the match tab's play-button wiring.</summary>
public sealed class LobbyMatchTabPanel : LobbyTabPanel
{
    [SerializeField] Button playButton;

    [Header("승급전 문구")]
    [Tooltip("전투 진입 버튼의 문구 노드. 승급전 대기 상태에서 문구가 갈린다 — 지금 누를 판이 무엇인지 버튼 자신이 말하게.\n" +
             "비면 문구 축을 건너뛴다(버튼은 저작 문구 그대로 남는다).")]
    [SerializeField] TMP_Text playLabel;

    [Tooltip("승급전 대기일 때 쓸 문구. 평상시 문구는 여기가 아니라 프리팹 저작값이 정본이다 — " +
             "시작할 때 한 번 캡처해 두고 상태가 풀리면 그리로 돌아간다.")]
    [SerializeField] string promoLabelText = "승급전";

    [Header("오버레이 진입")]
    [Tooltip("랭크 보상 목록을 여는 버튼. 예전에는 UnityEvent가 OverlayHost 안의 패널을 직접 가리켰지만,\n"
           + "패널이 풀에서 세워지면 저작 시점에 대상이 없어 배선할 수 없다 → 여기서 코드로 연다.")]
    [SerializeField] Button rankRewardButton;

    [Tooltip("시즌 랭킹과 내 순위를 여는 버튼.")]
    [SerializeField] Button rankingButton;

    [Tooltip("룰렛을 여는 버튼. 콘텐츠 해금과 설정 준비 조건을 함께 따른다.")]
    [SerializeField] Button rouletteButton;

    [Tooltip("일일·주간 미션을 여는 버튼. 강제 선형 FTUE 완료 시 해금한다.")]
    [SerializeField] Button missionButton;

    [Tooltip("가이드 미션(1회성 순차)을 여는 버튼. 정의가 없거나 전부 수령했으면 버튼째 감춘다 —\n" +
             "가이드는 끝나면 다시 오지 않는 축이라 빈 화면 안내보다 사라지는 게 맞다.")]
    [SerializeField] Button guideMissionButton;

    [SerializeField] Button attendanceButton;

    [Tooltip("배틀패스를 여는 버튼. 시즌 공백기에도 눌린다 — 시즌이 없다는 것은 화면이 안내한다.")]
    [SerializeField] Button passButton;
    [SerializeField] Button achievementButton;

    [Header("모험")]
    [Tooltip("모험 맵으로 가는 버튼. 이동 자체는 LobbyRoot가 한다 — 탭 패널은 탭 이동을 모른다.")]
    [SerializeField] Button adventureButton;

    public event Action PlayRequested;

    public event Action AdventureRequested;

    // 버튼의 저작 문구. 승급전 상태가 풀리면 여기로 돌아간다 — 평상시 문구를 코드가 다시 쓰지 않게.
    string m_defaultPlayText;
    ContentUnlockPresentation m_unlockPresentation;

    protected override void OnInitializeUI()
    {
        if (playButton != null) playButton.onClick.AddListener(HandlePlayRequested);
        if (rankRewardButton != null) rankRewardButton.onClick.AddListener(OpenRankRewards);
        if (rankingButton != null) rankingButton.onClick.AddListener(OpenRanking);
        if (rouletteButton != null) rouletteButton.onClick.AddListener(OpenRoulette);
        if (missionButton != null) missionButton.onClick.AddListener(OpenMissions);
        if (guideMissionButton != null) guideMissionButton.onClick.AddListener(OpenGuideMissions);
        if (passButton != null) passButton.onClick.AddListener(OpenPass);
        if (achievementButton != null) achievementButton.onClick.AddListener(OpenAchievements);
        if (adventureButton != null) adventureButton.onClick.AddListener(HandleAdventureRequested);

        if (playLabel != null) m_defaultPlayText = playLabel.text;

        // 잠김 룩은 코드로 얹는다 — 기능키↔버튼 짝이 아래 계산식 바로 옆에 있어야 둘이 갈리지 않는다.
        // PlayBtn만 프리팹 저작인 것은 그쪽 잠금 주체가 LobbyMatchLauncher라 중립 지점이 필요했기 때문이다.
        if (adventureButton != null) FeatureLockView.Attach(adventureButton.gameObject, EOutgameFeature.Adventure);
        if (missionButton != null) FeatureLockView.Attach(missionButton.gameObject, EOutgameFeature.Mission);
        if (achievementButton != null) FeatureLockView.Attach(achievementButton.gameObject, EOutgameFeature.Mission);
        if (guideMissionButton != null) FeatureLockView.Attach(guideMissionButton.gameObject, EOutgameFeature.Mission);
        if (rouletteButton != null) FeatureLockView.Attach(rouletteButton.gameObject, EOutgameFeature.Roulette);
        if (attendanceButton != null) FeatureLockView.Attach(attendanceButton.gameObject, EOutgameFeature.Mission);
        m_unlockPresentation = gameObject.AddComponent<ContentUnlockPresentation>();
        m_unlockPresentation.Bind(
            missionButton != null ? missionButton.GetComponent<FeatureLockView>() : null,
            adventureButton != null ? adventureButton.GetComponent<FeatureLockView>() : null,
            rouletteButton != null ? rouletteButton.GetComponent<FeatureLockView>() : null,
            playButton != null ? playButton.GetComponent<FeatureLockView>() : null,
            guideMissionButton != null ? guideMissionButton.GetComponent<FeatureLockView>() : null,
            attendanceButton != null ? attendanceButton.GetComponent<FeatureLockView>() : null);

        // 탭이 꺼져 있는 동안에도 신호를 받아야 한다 — 놓치면 다른 탭에 있던 사이 끝난 연출을 영영 못 따라간다.
        OutgameFeatureLock.OnChanged += ApplyFeatureLocks;
        LobbyRankEffectDirector.OnAnyFinished += RefreshPlayLabel;

        // 첫 getMissions 응답이 탭보다 늦게 도착해도 버튼이 스스로 나타나야 한다.
        MissionManager.OnChanged += RefreshGuideMissionButton;
    }

    // 첫 반영은 Awake가 아니라 여기다 — 디렉터의 Awake보다 먼저 물으면 "연출 없음"으로 읽혀 결말을 미리 말한다.
    void Start()
    {
        RefreshPlayLabel();
        ApplyFeatureLocks();
        RefreshGuideMissionButton();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (playButton != null) playButton.onClick.RemoveListener(HandlePlayRequested);
        if (rankRewardButton != null) rankRewardButton.onClick.RemoveListener(OpenRankRewards);
        if (rankingButton != null) rankingButton.onClick.RemoveListener(OpenRanking);
        if (rouletteButton != null) rouletteButton.onClick.RemoveListener(OpenRoulette);
        if (missionButton != null) missionButton.onClick.RemoveListener(OpenMissions);
        if (guideMissionButton != null) guideMissionButton.onClick.RemoveListener(OpenGuideMissions);
        if (passButton != null) passButton.onClick.RemoveListener(OpenPass);
        if (achievementButton != null) achievementButton.onClick.RemoveListener(OpenAchievements);
        if (adventureButton != null) adventureButton.onClick.RemoveListener(HandleAdventureRequested);

        OutgameFeatureLock.OnChanged -= ApplyFeatureLocks;
        LobbyRankEffectDirector.OnAnyFinished -= RefreshPlayLabel;
        MissionManager.OnChanged -= RefreshGuideMissionButton;
    }

    public override void OnEnter()
    {
        AchievementCommands.RefreshAsync(_force: true).Forget();
        RefreshPlayLabel();
        ApplyFeatureLocks();
        RefreshGuideMissionButton();
    }

    public override void OnSettled()
    {
        m_unlockPresentation?.SetVisible(true);
    }

    public override void OnLeave()
    {
        m_unlockPresentation?.SetVisible(false);
        RefreshGuideMissionButton();
    }

    /// <summary>승급전 대기면 버튼 문구를 갈고, 아니면 저작 문구로 되돌린다.
    /// 랭크 정산 연출이 도는 중에는 갈지 않는다 — 별이 차기 전에 버튼이 결과를 먼저 표시하지 않게 한다.</summary>
    void RefreshPlayLabel()
    {
        if (playLabel == null) return;

        bool t_promo = RankManager.IsPromoPending && !LobbyRankEffectDirector.Playing;

        playLabel.text = t_promo && !string.IsNullOrEmpty(promoLabelText) ? promoLabelText : m_defaultPlayText;
    }

    /// <summary>잠긴 기능의 버튼을 죽인다. 잠김 룩(FeatureLockView)과 달리 차단은 이 패널이 소유한다 —
    /// 두 축이 같은 컴포넌트에 있으면 어느 쪽이 이겼는지가 호출 순서에 달린다.</summary>
    void ApplyFeatureLocks()
    {
        bool t_missionsUnlocked = OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission);
        if (missionButton != null) missionButton.interactable = t_missionsUnlocked;
        if (achievementButton != null) achievementButton.interactable = t_missionsUnlocked;
        if (guideMissionButton != null) guideMissionButton.interactable = t_missionsUnlocked;
        RefreshGuideMissionButton();

        if (adventureButton != null)
            adventureButton.interactable = OutgameFeatureLock.IsUnlocked(EOutgameFeature.Adventure);

        if (rouletteButton != null)
        {
            rouletteButton.gameObject.SetActive(RouletteManager.IsAvailable);
            rouletteButton.interactable = OutgameFeatureLock.IsUnlocked(EOutgameFeature.Roulette);
        }
    }

    /// <summary>랭크 보상 목록. 풀이 없으면(초기화 미초기화) 조용히 지나가지 않고 드러낸다.</summary>
    public void OpenRankRewards() => OpenPooled<RankRewardPanel>();

    public void OpenRanking() => OpenPooled<RankingBoardPanel>();

    /// <summary>룰렛. 버튼을 감추는 것만으로는 부족하다 — 진입을 실제로 막는 주체는 여기다
    /// (감추기는 표현이고, 다른 경로로 이 메서드를 부를 수 있다).</summary>
    public void OpenRoulette()
    {
        if (!RouletteManager.IsAvailable || !OutgameFeatureLock.IsUnlocked(EOutgameFeature.Roulette)) return;

        OpenPooled<RoulettePanel>();
    }

    /// <summary>강제 선형 FTUE 완료 후 일일·주간 미션을 연다.</summary>
    public void OpenMissions()
    {
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission)) return;
        OpenPooled<MissionPanel>();
    }

    /// <summary>가이드 미션. 버튼을 감추는 것만으로는 부족하다 — 진입을 실제로 막는 주체는 여기다
    /// (감추기는 표현이고, 다른 경로로 이 메서드를 부를 수 있다).</summary>
    public void OpenGuideMissions()
    {
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission)) return;
        if (!AnyGuideMissionOpen()) return;

        OpenPooled<GuideMissionPanel>();
    }

    /// <summary>배틀패스. 잠금 게이트가 없다 — 활성 시즌이 없으면
    /// 화면이 그 사실을 그린다(빈 목록으로 두지 않는다).</summary>
    public void OpenPass() => OpenPooled<PassPanel>();

    public void OpenAchievements()
    {
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission)) return;
        OpenPooled<AchievementPanel>();
    }

    void OpenPooled<T>() where T : PooledUIBase
    {
        if (UIPoolManager.Instance == null)
        {
            Debug.LogError($"[LobbyMatchTabPanel] There is no UIPoolManager, so {typeof(T).Name} cannot be opened — check the initialization (InitializationRunner).");
            return;
        }

        UIPoolManager.Instance.RequestUI<T>(this);
    }

    public void SetPlayInteractable(bool _interactable)
    {
        if (playButton != null) playButton.interactable = _interactable;
    }

    /// <summary>미수령 가이드 미션이 남았을 때만 버튼을 보인다. 판정은 패널·행과 같은
    /// MissionManager 낙인 하나다 — 버튼과 화면이 다른 눈으로 보면 갈린다.</summary>
    void RefreshGuideMissionButton()
    {
        if (guideMissionButton != null) guideMissionButton.gameObject.SetActive(AnyGuideMissionOpen());
    }

    static bool AnyGuideMissionOpen()
    {
        System.Collections.Generic.IReadOnlyList<MissionDefinition> t_definitions = MissionManager.Definitions;
        for (int i = 0; i < t_definitions.Count; i++)
            if (t_definitions[i].Period == "guide" && !MissionManager.IsClaimed(t_definitions[i].Id)) return true;
        return false;
    }

    void HandlePlayRequested() => PlayRequested?.Invoke();

    void HandleAdventureRequested() => AdventureRequested?.Invoke();
}
