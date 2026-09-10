using UnityEngine;

/// <summary>후속 안내만 조정한다. 기존 온보딩은 이 조정기의 상태를 기다리지 않는다.</summary>
public sealed class GuidanceCoordinator : MonoBehaviour
{
    static GuidanceCoordinator s_instance;
    LobbyMatchLauncher m_launcher;
    GuideMissionNoticePopup m_notice;
    bool m_initialized;
    float m_nextEvaluation;

    public static bool CanPresent => s_instance != null && s_instance.SafeToPresent(null);

    public static void Install(GameObject owner)
    {
        if (owner.GetComponent<GuidanceCoordinator>() == null) owner.AddComponent<GuidanceCoordinator>();
    }

    void Awake()
    {
        s_instance = this;
        m_launcher = FindFirstObjectByType<LobbyMatchLauncher>();
    }

    bool HasPriorityActivity => !GameInitialization.IsReady || !isActiveAndEnabled
        || CurtainView.IsBusy || LoadingCoverView.IsCovering
        || (SceneTransitionVideo.Instance != null && SceneTransitionVideo.Instance.IsPlaying)
        || (m_launcher != null && m_launcher.IsRunning)
        || LobbyRankEffectDirector.Playing || LobbyGainEffectDirector.Playing
        || RankPromoteOverlay.IsOpen || RewardClaimPopup.IsOpen || AdventureRewardFlow.IsClaiming
        || PackOpenOverlay.IsOpen || CardDetailOverlayView.IsOpen || AlbumPageOverlayView.IsOpen
        || CardRewardOverlay.IsOpen || CardSetRewardOverlay.IsOpen || PackRewardOverlay.IsOpen
        || OutgameTutorialGateUI.IsShowing || TriggeredTutorialRunner.IsRunning
        || AdventureTutorialRunner.IsRunning;

    bool SafeToPresent(PooledUIBase except)
        => !HasPriorityActivity && !SynergyIntroduction.IsActive && UIPoolManager.instance != null
        && !UIPoolManager.instance.HasVisibleUIExcept(except);

    void Update()
    {
        // 표시 중인 팝업도 새 게이트·씬 전환에 즉시 양보한다. 수령 왕복은 팝업 자신의 대기막이 보호한다.
        if (m_notice != null && m_notice.isShow)
        {
            if (!m_notice.IsClaiming && !SafeToPresent(m_notice)) m_notice.Yield();
            return;
        }
        if (SynergyIntroduction.IsActive)
        {
            if (!GameInitialization.IsReady || CurtainView.IsBusy || LoadingCoverView.IsCovering
                || (m_launcher != null && m_launcher.IsRunning) || OutgameTutorialRunner.IsRunning
                || TriggeredTutorialRunner.IsRunning || AdventureTutorialRunner.IsRunning)
                SynergyIntroduction.CancelPresentation();
            return;
        }
        if (!GameInitialization.IsReady || Time.unscaledTime < m_nextEvaluation) return;
        m_nextEvaluation = Time.unscaledTime + 0.25f;
        GuideMissionNoticeService.ObserveAdventure();
        if (!m_initialized)
        {
            m_initialized = true;
            SynergyIntroduction.Reevaluate();
            AdventureTutorialRunner.RefreshUnlock();
        }
        if (!SafeToPresent(null)) return;
        GuideMissionNoticeService.RefreshIfNeeded();
        string id = GuideMissionNoticeService.ReadyMission();
        if (id != null)
        {
            // 계정 교체 이후 이전 팝업의 콜백이 새 계정 이력에 닿지 않게 한다.
            string owner = FirebaseAuthService.Instance.UserId;
            m_notice = UIPoolManager.instance.AddOrUpdateUI<GuideMissionNoticePopup>(new GuideMissionNoticeData
            {
                MissionId = id,
                OnCompleted = () =>
                {
                    if (owner == FirebaseAuthService.Instance.UserId) GuideMissionNoticeService.MarkShown(id);
                },
            });
            Debug.Log($"[Guidance] Present guide {id}");
            return;
        }
        if (OutgameTutorialRunner.IsRunning) return;
        if (AdventureTutorialRunner.TryBegin()) return;
        if (SynergyIntroduction.HasPending) SynergyIntroduction.TryBegin();
    }

    void OnDisable()
    {
        m_notice?.Yield();
        SynergyIntroduction.CancelPresentation();
        if (s_instance == this) s_instance = null;
    }

    void OnEnable() => s_instance = this;
}
