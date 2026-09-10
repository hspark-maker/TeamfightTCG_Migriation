using System;
using UnityEngine;

/// <summary>미션 달성 팝업과 온보딩 안내의 무대 소유권을 조정한다.</summary>
public sealed class GuidanceCoordinator : MonoBehaviour
{
    static GuidanceCoordinator s_instance;
    LobbyMatchLauncher m_launcher;
    GuideMissionNoticePopup m_notice;
    bool m_initialized;
    float m_nextEvaluation;
    string m_noticeUser;

    public static bool IsMissionNoticeShowing { get; private set; }
    public static event Action OnMissionNoticePresentationChanged;

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
        || (m_launcher != null && (m_launcher.IsRunning || m_launcher.IsAdventurePresentationBusy))
        || LobbyRankEffectDirector.Playing || LobbyGainEffectDirector.Playing
        || RankPromoteOverlay.IsOpen || RewardClaimPopup.IsOpen || AdventureRewardFlow.IsClaiming
        || PackOpenOverlay.IsOpen || CardDetailOverlayView.IsGrowthPresentationBusy
        || CardDetailOverlayView.IsUnlockFxPlaying || AlbumInsertSession.IsRunning
        || CardRewardOverlay.IsOpen || CardSetRewardOverlay.IsOpen || PackRewardOverlay.IsOpen;

    bool SafeToPresent(PooledUIBase except)
        => SafeToPresentMission(except) && !OutgameTutorialGateUI.IsShowing
        && !CardDetailOverlayView.IsOpen && !AlbumPageOverlayView.IsOpen
        && !TriggeredTutorialRunner.IsRunning && !AdventureTutorialRunner.IsRunning;

    bool SafeToPresentMission(PooledUIBase except)
        => !HasPriorityActivity && !SynergyIntroduction.IsActive && UIPoolManager.instance != null
        && !UIPoolManager.instance.HasVisibleUIExcept(except);

    static void SetMissionNoticeShowing(bool showing)
    {
        if (IsMissionNoticeShowing == showing) return;
        IsMissionNoticeShowing = showing;
        OnMissionNoticePresentationChanged?.Invoke();
    }

    void Update()
    {
        // 온보딩 게이트는 팝업에 양보한다. 실제 연출·씬 전환은 팝업보다 우선한다.
        if (m_notice != null && m_notice.isShow)
        {
            if (!m_notice.IsClaiming && (m_noticeUser != FirebaseAuthService.Instance.UserId || !SafeToPresentMission(m_notice)))
                m_notice.Yield();
            return;
        }
        // 수령 팝업과 카드·팩 보상 연출까지 끝나야 튜토리얼을 다시 세운다.
        if (IsMissionNoticeShowing)
        {
            if (RewardClaimPopup.IsOpen || LobbyGainEffectDirector.Playing || PackOpenOverlay.IsOpen
                || CardRewardOverlay.IsOpen || CardSetRewardOverlay.IsOpen || PackRewardOverlay.IsOpen
                || (UIPoolManager.instance != null && UIPoolManager.instance.HasVisibleUIExcept())) return;
            SetMissionNoticeShowing(false);
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
        if (!SafeToPresentMission(null)) return;
        GuideMissionNoticeService.RefreshIfNeeded();
        string id = GuideMissionNoticeService.ReadyMission();
        if (id != null)
        {
            // 계정 교체 이후 이전 팝업의 콜백이 새 계정 이력에 닿지 않게 한다.
            string owner = FirebaseAuthService.Instance.UserId;
            m_noticeUser = owner;
            SetMissionNoticeShowing(true);
            m_notice = UIPoolManager.instance.AddOrUpdateUI<GuideMissionNoticePopup>(new GuideMissionNoticeData
            {
                MissionId = id,
                OnCompleted = () =>
                {
                    if (owner == FirebaseAuthService.Instance.UserId) GuideMissionNoticeService.MarkShown(id);
                },
            });
            if (m_notice == null || !m_notice.isShow) SetMissionNoticeShowing(false);
            Debug.Log($"[Guidance] Present guide {id}");
            return;
        }
        if (OutgameTutorialRunner.IsRunning) return;
        if (!SafeToPresent(null)) return;
        if (SynergyIntroduction.HasPending) SynergyIntroduction.TryBegin();
    }

    void OnDisable()
    {
        m_notice?.Yield();
        SetMissionNoticeShowing(false);
        SynergyIntroduction.CancelPresentation();
        if (s_instance == this) s_instance = null;
    }

    void OnEnable() => s_instance = this;
}
