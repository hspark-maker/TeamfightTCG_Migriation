using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>후속 안내만 조정한다. 기존 온보딩은 이 조정기의 상태를 기다리지 않는다.</summary>
public sealed class GuidanceCoordinator : MonoBehaviour
{
    static GuidanceCoordinator s_instance;
    LobbyMatchLauncher m_launcher;
    bool m_initialized;
    float m_nextEvaluation;
    readonly Dictionary<EOutgameTutorialTrigger, Func<bool>> m_pendingGuides = new Dictionary<EOutgameTutorialTrigger, Func<bool>>();

    public static bool CanPresent => !ContentUnlockPresentation.IsPlaying && CanPresentContentUnlock;

    /// <summary>해금 연출 자체를 제외한 로비 무대 준비 상태.</summary>
    public static bool CanPresentContentUnlock => s_instance != null && s_instance.SafeToPresent()
        && !OutgameTutorialRunner.IsRunning && !MissionCutInView.IsPlaying;

    /// <summary>해금 소개 스텝 자신의 커서는 무대 점유로 세지 않는다.</summary>
    public static bool CanRunContentIntro => s_instance != null && s_instance.SafeToPresent(true)
        && !MissionCutInView.IsPlaying && !s_instance.AdventureMapOpen;

    bool AdventureMapOpen => m_launcher != null && m_launcher.IsAdventureMapOpen;

    public static void Install(GameObject owner)
    {
        if (owner.GetComponent<GuidanceCoordinator>() == null) owner.AddComponent<GuidanceCoordinator>();
    }

    /// <summary>자율 안내 발화의 유일한 창구. 러너는 규칙(열림·낙인·미루기·선행 기능)만 보고, 무대가 비었는지는 여기서 본다.
    /// 무대가 바쁘면 같은 트리거를 한 건만 보관하고, 진입 화면이 유지되는 동안 자동 재시도한다.
    /// 오버레이가 열려 있는 것은 바쁨이 아니다: 키워드 패널·모험 맵처럼 트리거 자체가 연 화면 위에서 시작하는 안내가 있다.</summary>
    public static void TryFire(EOutgameTutorialTrigger _trigger, Func<bool> _isCurrent = null)
    {
        if (_trigger == EOutgameTutorialTrigger.None) return;
        if (_isCurrent != null && !_isCurrent()) return;
        if (!OutgameTutorialRunner.HasPending(_trigger)) return;

        if (StageBusyForGuided || OutgameTutorialRunner.IsGuidedRunning)
        {
            if (s_instance != null) s_instance.m_pendingGuides[_trigger] = _isCurrent;
            return;
        }

        if (s_instance != null) s_instance.m_pendingGuides.Remove(_trigger);
        OutgameTutorialRunner.Fire(_trigger);
    }

    void ResumePendingGuide()
    {
        foreach (var t_request in this.m_pendingGuides)
        {
            if ((t_request.Value != null && !t_request.Value()) || !OutgameTutorialRunner.HasPending(t_request.Key))
            {
                this.m_pendingGuides.Remove(t_request.Key);
                return;
            }
            if (StageBusyForGuided || OutgameTutorialRunner.IsGuidedRunning) return;

            // Fire의 이벤트가 다른 안내를 요청할 수 있으므로 먼저 제거한다.
            this.m_pendingGuides.Remove(t_request.Key);
            OutgameTutorialRunner.Fire(t_request.Key);
            return;
        }
    }

    static bool StageBusyForGuided
        => SynergyIntroduction.IsActive || ContentUnlockPresentation.IsPlaying || OutgameTutorialGateUI.IsShowing
        || CurtainView.IsBusy || LoadingCoverView.IsCovering
        || !GameInitialization.IsReady
        || (SceneTransitionVideo.Instance != null && SceneTransitionVideo.Instance.IsPlaying)
        || (s_instance != null && s_instance.m_launcher != null && s_instance.m_launcher.IsRunning)
        || LobbyRankEffectDirector.Playing || LobbyGainEffectDirector.Playing;

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
        || OutgameTutorialGateUI.IsShowing;

    bool SafeToPresent(bool _contentIntro = false)
        => !HasPriorityActivity && (_contentIntro || !OutgameTutorialRunner.IsGuidedRunning)
        && !SynergyIntroduction.IsActive && UIPoolManager.instance != null
        && !UIPoolManager.instance.HasVisibleUIExcept();

    void Update()
    {
        if (SynergyIntroduction.IsActive)
        {
            if (!GameInitialization.IsReady || CurtainView.IsBusy || LoadingCoverView.IsCovering
                || (m_launcher != null && m_launcher.IsRunning) || OutgameTutorialRunner.IsRunning
                || OutgameTutorialRunner.IsGuidedRunning)
                SynergyIntroduction.CancelPresentation();
            return;
        }
        if (!GameInitialization.IsReady || Time.unscaledTime < m_nextEvaluation) return;
        m_nextEvaluation = Time.unscaledTime + 0.25f;
        if (!m_initialized)
        {
            m_initialized = true;
            SynergyIntroduction.Reevaluate();
            ContentUnlockManager.RequestRefresh();
        }
        this.ResumePendingGuide();
        if (!CanPresent) return;
        if (OutgameTutorialRunner.IsRunning) return;
        if (!AdventureMapOpen && ContentUnlockPresentation.IsReady
            && OutgameTutorialRunner.TryGetPendingContentIntro(out var t_trigger))
        {
            TryFire(t_trigger);
            if (OutgameTutorialRunner.IsGuidedRunning) return;
        }
        if (SynergyIntroduction.HasPending) SynergyIntroduction.TryBegin();
    }

    void OnDisable()
    {
        this.m_pendingGuides.Clear();
        SynergyIntroduction.CancelPresentation();
        if (s_instance == this) s_instance = null;
    }

    void OnEnable() => s_instance = this;
}
