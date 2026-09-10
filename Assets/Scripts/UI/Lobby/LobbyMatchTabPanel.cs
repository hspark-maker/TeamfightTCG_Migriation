using System;
using TMPro;
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

    [SerializeField] Button keywordGrowthButton;

    [Tooltip("룰렛을 여는 버튼. 잠김 룩(FeatureLockView)은 붙이지 않는다 — 룰렛 해금은 온보딩 축이라 아직 없다.")]
    [SerializeField] Button rouletteButton;

    [Tooltip("일일·주간 미션을 여는 버튼. 잠금 축이 없어 항상 눌린다 — 목록이 비는 주기는 화면이 스스로 안내한다.")]
    [SerializeField] Button missionButton;

    [Tooltip("가이드 미션(1회성 순차)을 여는 버튼. 정의가 없거나 전부 수령했으면 버튼째 감춘다 —\n" +
             "가이드는 끝나면 다시 오지 않는 축이라 빈 화면 안내보다 사라지는 게 맞다.")]
    [SerializeField] Button guideMissionButton;

    [Tooltip("배틀패스를 여는 버튼. 시즌 공백기에도 눌린다 — 시즌이 없다는 것은 화면이 안내한다.")]
    [SerializeField] Button passButton;

    [Tooltip("랭크 배지(RankInfo 안). 누르면 지금 등급의 승급 오버레이를 다시 본다 — 열람이라 랭크 값도 디렉터 상태도 건드리지 않는다.\n" +
             "버튼 전이는 None으로 저작한다: 배지는 랭크 자체를 그리는 그림이라 눌림·비활성 틴트가 상태 오독을 부른다.\n" +
             "언랭크 차단도 interactable이 아니라 핸들러가 한다 — 갱신 시점을 따로 둘 필요 없이 누른 순간 판정한다.")]
    [SerializeField] Button rankBadgeButton;

    [Header("모험")]
    [Tooltip("모험 맵으로 가는 버튼. 이동 자체는 LobbyRoot가 한다 — 탭 패널은 탭 이동을 모른다.")]
    [SerializeField] Button adventureButton;

    public event Action PlayRequested;

    public event Action AdventureRequested;

    // 버튼의 저작 문구. 승급전 상태가 풀리면 여기로 돌아간다 — 평상시 문구를 코드가 다시 쓰지 않게.
    string m_defaultPlayText;

    void Awake()
    {
        if (playButton != null) playButton.onClick.AddListener(HandlePlayRequested);
        if (rankRewardButton != null) rankRewardButton.onClick.AddListener(OpenRankRewards);
        if (rankingButton != null) rankingButton.onClick.AddListener(OpenRanking);
        if (keywordGrowthButton != null) keywordGrowthButton.onClick.AddListener(OpenKeywordGrowth);
        if (rouletteButton != null) rouletteButton.onClick.AddListener(OpenRoulette);
        if (missionButton != null) missionButton.onClick.AddListener(OpenMissions);
        if (guideMissionButton != null) guideMissionButton.onClick.AddListener(OpenGuideMissions);
        if (passButton != null) passButton.onClick.AddListener(OpenPass);
        if (rankBadgeButton != null) rankBadgeButton.onClick.AddListener(ReplayRankPromote);
        if (adventureButton != null) adventureButton.onClick.AddListener(HandleAdventureRequested);

        if (playLabel != null) m_defaultPlayText = playLabel.text;

        // 잠김 룩은 코드로 얹는다 — 기능키↔버튼 짝이 아래 계산식 바로 옆에 있어야 둘이 갈리지 않는다.
        // PlayBtn만 프리팹 저작인 것은 그쪽 잠금 주체가 LobbyMatchLauncher라 중립 지점이 필요했기 때문이다.
        if (keywordGrowthButton != null) FeatureLockView.Attach(keywordGrowthButton.gameObject, EOutgameFeature.KeywordGrowth);
        if (adventureButton != null) FeatureLockView.Attach(adventureButton.gameObject, EOutgameFeature.Adventure);

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

    void OnDestroy()
    {
        if (playButton != null) playButton.onClick.RemoveListener(HandlePlayRequested);
        if (rankRewardButton != null) rankRewardButton.onClick.RemoveListener(OpenRankRewards);
        if (rankingButton != null) rankingButton.onClick.RemoveListener(OpenRanking);
        if (keywordGrowthButton != null) keywordGrowthButton.onClick.RemoveListener(OpenKeywordGrowth);
        if (rouletteButton != null) rouletteButton.onClick.RemoveListener(OpenRoulette);
        if (missionButton != null) missionButton.onClick.RemoveListener(OpenMissions);
        if (guideMissionButton != null) guideMissionButton.onClick.RemoveListener(OpenGuideMissions);
        if (passButton != null) passButton.onClick.RemoveListener(OpenPass);
        if (rankBadgeButton != null) rankBadgeButton.onClick.RemoveListener(ReplayRankPromote);
        if (adventureButton != null) adventureButton.onClick.RemoveListener(HandleAdventureRequested);

        OutgameFeatureLock.OnChanged -= ApplyFeatureLocks;
        LobbyRankEffectDirector.OnAnyFinished -= RefreshPlayLabel;
        MissionManager.OnChanged -= RefreshGuideMissionButton;
    }

    public override void OnEnter()
    {
        RefreshPlayLabel();
        ApplyFeatureLocks();
        RefreshGuideMissionButton();
    }

    /// <summary>승급전 대기면 버튼 문구를 갈고, 아니면 저작 문구로 되돌린다.
    /// 랭크 정산 연출이 도는 중에는 갈지 않는다 — 별이 차고 배지에 광선이 붙는 결말을 버튼이 먼저 말해버린다.</summary>
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
        if (keywordGrowthButton != null)
            keywordGrowthButton.interactable = OutgameFeatureLock.IsUnlocked(EOutgameFeature.KeywordGrowth);

        if (adventureButton != null)
            adventureButton.interactable = OutgameFeatureLock.IsUnlocked(EOutgameFeature.Adventure);

        // 룰렛만 기능잠금 축이 아니라 "설정과 소스가 섰는가"로 갈린다. 저작 결함이나 출시 빌드에서
        // 버튼이 통째로 사라지는 것이 안전장치라, 잠김 룩을 씌우지 않고 감춘다.
        if (rouletteButton != null)
            rouletteButton.gameObject.SetActive(RouletteManager.IsAvailable);
    }

    /// <summary>랭크 보상 목록. 풀이 없으면(초기화 미초기화) 조용히 지나가지 않고 드러낸다.</summary>
    public void OpenRankRewards() => OpenPooled<RankRewardPanel>();

    public void OpenRanking() => OpenPooled<RankingBoardPanel>();

    public void OpenKeywordGrowth()
    {
        // 버튼을 죽여 두는 것만으로는 부족하다 — 잠김 표시는 표현 레이어 몫이고, 진입을 실제로 막는 주체는 여기다.
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.KeywordGrowth)) return;

        OpenPooled<KeywordGrowthPanel>();
    }

    /// <summary>룰렛. 버튼을 감추는 것만으로는 부족하다 — 진입을 실제로 막는 주체는 여기다
    /// (감추기는 표현이고, 다른 경로로 이 메서드를 부를 수 있다).</summary>
    public void OpenRoulette()
    {
        if (!RouletteManager.IsAvailable) return;

        OpenPooled<RoulettePanel>();
    }

    /// <summary>지금 등급의 승급 연출을 다시 본다(열람). 랭크 값을 바꾸지 않고 디렉터도 거치지 않는다 —
    /// OnAnyFinished를 기다리는 쪽(탭 문구·온보딩 브리지)이 열람을 정산으로 오인하면 안 된다.</summary>
    public void ReplayRankPromote()
    {
        // 언랭크도 CurrentGrade가 Bronze를 돌려주므로 등급으로는 갈리지 않는다(PackUnlockRules와 같은 규율).
        if (!RankManager.IsRanked) return;

        // 온보딩 진행 여부로는 막지 않는다. 안내가 화면을 잡고 있는 동안에는 게이트 blocker가 이미 클릭을 먹고,
        // IsRunning은 "시퀀스 미완주"라서 그것으로 막으면 온보딩을 끝내지 않은 계정은 배지가 영영 무반응이 된다.

        // 정산 연출 중에는 막는다. Show가 앞 안무를 죽이며 디렉터의 덮임 통지를 앞당겨 발화시키고,
        // 그쪽 대기가 열람 탭 한 번에 풀린다.
        if (LobbyRankEffectDirector.Playing || RankPromoteOverlay.IsOpen) return;

        if (!RankManager.TryGetTier(RankManager.TierIndex, out RankTier t_tier)) return;
        if (!RankPromoteOverlay.TryGet(out RankPromoteOverlay t_overlay)) return;

        // 시작 배지 없이 도달 연출만 — 열람은 "갈렸다"가 아니라 "이랬다"라 옛 배지 파열 두 박이 없다.
        // 콜백 둘 다 null이 안전하다(Show와 OnTapped 모두 ?. 로 소비한다).
        t_overlay.Show(RankTier.None, t_tier, EPromoteKind.FirstEntry, null, null, _browse: true);
    }

    /// <summary>일일·주간 미션. 잠금 게이트가 없다 — 미션은 부가 기능이고, 목록이 비어도
    /// 화면이 스스로 안내한다(활성 미션이 현재 팩 개봉 축뿐이라 실제로 비는 주기가 있다).</summary>
    public void OpenMissions() => OpenPooled<MissionPanel>();

    /// <summary>가이드 미션. 버튼을 감추는 것만으로는 부족하다 — 진입을 실제로 막는 주체는 여기다
    /// (감추기는 표현이고, 다른 경로로 이 메서드를 부를 수 있다).</summary>
    public void OpenGuideMissions()
    {
        if (!AnyGuideMissionOpen()) return;

        OpenPooled<GuideMissionPanel>();
    }

    /// <summary>배틀패스. 미션과 같은 이유로 잠금 게이트가 없다 — 활성 시즌이 없으면
    /// 화면이 그 사실을 그린다(빈 목록으로 두지 않는다).</summary>
    public void OpenPass() => OpenPooled<PassPanel>();

    static void OpenPooled<T>() where T : PooledUIBase
    {
        if (UIPoolManager.Instance == null)
        {
            Debug.LogError($"[LobbyMatchTabPanel] There is no UIPoolManager, so {typeof(T).Name} cannot be opened — check the initialization (InitializationRunner).");
            return;
        }

        UIPoolManager.Instance.AddOrUpdateUI<T>();
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
