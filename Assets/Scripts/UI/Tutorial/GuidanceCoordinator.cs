using UnityEngine;

/// <summary>활성 가이드 미션의 해금 연출·이동·온보딩 순서를 조정한다.</summary>
public sealed partial class GuidanceCoordinator : MonoBehaviour
{
    static GuidanceCoordinator s_instance;
    LobbyMatchLauncher m_launcher;
    LobbyTabController m_shell;
    bool m_initialized;
    float m_nextEvaluation;

    public static bool CanPresent => !ContentUnlockPresentation.IsPlaying
        && !HasPendingMissionFlow && CanPresentContentUnlock;

    /// <summary>해금 연출 자체를 제외한 로비 무대 준비 상태.</summary>
    public static bool CanPresentContentUnlock => s_instance != null && s_instance.SafeToPresent()
#if UNITY_EDITOR
        && !OnboardingPlayTest.IsActive && !OnboardingPlayTest.IsPreparing
#endif
        && !OutgameTutorialRunner.IsRunning;

    /// <summary>해금 소개 스텝과 소개 화면 자신은 무대 점유로 세지 않는다.</summary>
    public static bool CanRunContentIntro
    {
        get
        {
            if (s_instance == null || s_instance.AdventureMapOpen) return false;
            ContentUnlockIntroView intro = null;
            if (UIPoolManager.instance != null) UIPoolManager.instance.TryGetUI(out intro);
            return s_instance.SafeToPresent(true, intro);
        }
    }

    bool AdventureMapOpen => m_launcher != null && m_launcher.IsAdventureMapOpen;

    /// <summary>로비 탭 종류와 무관하게 다른 화면·연출이 없을 때 알림에서 화면 이동을 허용한다.</summary>
    public static bool CanNavigateFromLobby(PooledUIBase _notification)
        => !IsInputLocked && s_instance != null && s_instance.m_shell != null && s_instance.m_shell.CanSwipe
        && s_instance.m_shell.CurrentPanel != null && s_instance.m_shell.CurrentPanel.IsViewVisible
        && !s_instance.AdventureMapOpen && !ContentUnlockPresentation.IsPlaying
        && !OutgameTutorialRunner.IsRunning && s_instance.SafeToPresent(false, _notification);

    /// <summary>매치 탭이 제자리에 있고 로비 진입 조건을 만족할 때 화면 이동을 허용한다.</summary>
    public static bool CanNavigateFromMatchTab(PooledUIBase _notification)
        => CanNavigateFromLobby(_notification)
        && s_instance.m_shell.IsCurrentAnchorSelected(EOutgameTutorialAnchor.LobbyMatchTab);

    public static void Install(GameObject owner)
    {
        if (owner.GetComponent<GuidanceCoordinator>() == null) owner.AddComponent<GuidanceCoordinator>();
    }

    static bool StageBusyForGuided
        => ContentUnlockPresentation.IsPlaying || HasForeignGate
        || CardFilterPopup.IsOpen || CollectionFilterResults.IsOpen
        || UnlockIntroOverlay.IsOpen
        || CardDetailOverlayView.IsRitualPlaying || CardDetailOverlayView.IsUnlockFxPlaying
        || RankPromoteOverlay.IsOpen || RewardClaimPopup.IsOpen || AdventureRewardFlow.IsClaiming
        || PackOpenOverlay.IsOpen || CardRewardOverlay.IsOpen || CardSetRewardOverlay.IsOpen || PackRewardOverlay.IsOpen
        || CurtainView.IsBusy || LoadingCoverView.IsCovering
        || !GameInitialization.IsReady
        || (SceneTransitionVideo.Instance != null && SceneTransitionVideo.Instance.IsPlaying)
        || (s_instance != null && s_instance.m_launcher != null && s_instance.m_launcher.IsRunning)
        || LobbyRankEffectDirector.Playing || LobbyGainEffectDirector.Playing;

    void Awake()
    {
        s_instance = this;
        m_launcher = FindFirstObjectByType<LobbyMatchLauncher>();
        m_shell = GetComponent<LobbyTabController>();
    }

    bool HasPriorityActivity => !GameInitialization.IsReady || !isActiveAndEnabled
        || CurtainView.IsBusy || LoadingCoverView.IsCovering
        || (SceneTransitionVideo.Instance != null && SceneTransitionVideo.Instance.IsPlaying)
        || (m_launcher != null && m_launcher.IsRunning)
        || LobbyRankEffectDirector.Playing || LobbyGainEffectDirector.Playing
        || RankPromoteOverlay.IsOpen || RewardClaimPopup.IsOpen || AdventureRewardFlow.IsClaiming
        || PackOpenOverlay.IsOpen || CardDetailOverlayView.IsOpen || AlbumPageOverlayView.IsOpen
        || CardRewardOverlay.IsOpen || CardSetRewardOverlay.IsOpen || PackRewardOverlay.IsOpen
        || HasForeignGate || UnlockIntroOverlay.IsOpen;

    static bool HasForeignGate => OutgameTutorialGateUI.IsShowing
        && !(s_instance != null && s_instance.m_flowPreparing
            && OutgameTutorialGateUI.Instance != null && OutgameTutorialGateUI.Instance.IsTransitionOnly);

    bool SafeToPresent(bool _contentIntro = false, PooledUIBase _except = null)
        => !HasPriorityActivity && (_contentIntro || !OutgameTutorialRunner.IsGuidedRunning)
        && UIPoolManager.instance != null
        && !HasBlockingPopup(_except: _except);

    static bool HasBlockingPopup(PooledUIBase _except = null)
        => UIPoolManager.instance != null
            && UIPoolManager.instance.HasVisibleUIExcept(_except, ignoreMissionCutIn: true);

    void Update()
    {
#if UNITY_EDITOR
        if (OnboardingPlayTest.IsActive || OnboardingPlayTest.IsPreparing) return;
#endif
        if (AdvanceMatchMission()) return;
        if (!GameInitialization.IsReady || Time.unscaledTime < m_nextEvaluation) return;
        m_nextEvaluation = Time.unscaledTime + 0.25f;
        if (!m_initialized)
        {
            m_initialized = true;
            ContentUnlockManager.RequestRefresh();
        }
        AdvanceMissionFlow();
    }

    void OnDisable()
    {
        m_pauseVersion++;
        UnsubscribeFlowRecovery();
        CancelMatchMission();
        CancelMissionFlow(false);
        if (s_instance == this) s_instance = null;
    }

    void OnEnable()
    {
        s_instance = this;
        SubscribeFlowRecovery();
    }
}
