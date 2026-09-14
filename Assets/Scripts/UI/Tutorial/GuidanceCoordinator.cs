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
    static bool s_missionIntroRequested;
    Func<bool> m_pendingOwnedIntroduction;
    readonly Dictionary<EOutgameTutorialTrigger, Func<bool>> m_pendingGuides = new Dictionary<EOutgameTutorialTrigger, Func<bool>>();

    public static bool CanPresent => !ContentUnlockPresentation.IsPlaying
        && !OutgameTutorialRunner.TryGetPendingContentIntro(out _) && CanPresentContentUnlock;

    /// <summary>해금 연출 자체를 제외한 로비 무대 준비 상태.</summary>
    public static bool CanPresentContentUnlock => s_instance != null && s_instance.SafeToPresent()
        && !OutgameTutorialRunner.IsRunning;

    /// <summary>해금 소개 스텝 자신의 커서는 무대 점유로 세지 않는다.</summary>
    public static bool CanRunContentIntro => s_instance != null && s_instance.SafeToPresent(true)
        && !s_instance.AdventureMapOpen;

    bool AdventureMapOpen => m_launcher != null && m_launcher.IsAdventureMapOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ObserveGraduation()
    {
        s_missionIntroRequested = false;
        OutgameTutorialRunner.OnSequenceCompleted -= OnSequenceCompleted;
        OutgameTutorialRunner.OnSequenceCompleted += OnSequenceCompleted;
    }

    static void OnSequenceCompleted() => s_missionIntroRequested = true;

    public static void Install(GameObject owner)
    {
        if (owner.GetComponent<GuidanceCoordinator>() == null) owner.AddComponent<GuidanceCoordinator>();
    }

    /// <summary>명시한 미션 이동에서 첫 강화 안내만 다시 요청한다.</summary>
    public static void RequestMissionEnhance(LobbyTabController _shell)
    {
        if (_shell == null) return;
        int t_version = _shell.SwipeVersion;
        OutgameTutorialRunner.ResumeDeferred(EOutgameTutorialTrigger.CollectionTabFirstEnter);
        TryFire(EOutgameTutorialTrigger.CollectionTabFirstEnter,
            () => _shell != null && _shell.isActiveAndEnabled && _shell.SwipeVersion == t_version);
        if (s_instance != null && !OutgameTutorialRunner.IsGuidedRunning
            && !s_instance.m_pendingGuides.ContainsKey(EOutgameTutorialTrigger.CollectionTabFirstEnter)
            && CardDetailOverlayView.HasPendingIntroductionForOwnedCard())
            s_instance.m_pendingOwnedIntroduction = () => _shell != null && _shell.isActiveAndEnabled
                && _shell.SwipeVersion == t_version;
    }

    /// <summary>기존 계정은 미션을 직접 열었을 때 소개를 요청한다.</summary>
    public static void RequestMissionIntroduction(Func<bool> _isCurrent = null)
    {
        OutgameTutorialRunner.ResumeDeferred(EOutgameTutorialTrigger.GuideMissionIntroduction);
        TryFire(EOutgameTutorialTrigger.GuideMissionIntroduction, _isCurrent);
        SynergyIntroduction.RequestRelevantAction();
    }

    /// <summary>자율 안내 발화의 유일한 창구. 러너는 규칙(열림·낙인·미루기·선행 기능)만 보고, 무대가 비었는지는 여기서 본다.
    /// 무대가 바쁘면 같은 트리거를 한 건만 보관하고, 진입 화면이 유지되는 동안 자동 재시도한다.
    /// 오버레이가 열려 있는 것은 바쁨이 아니다: 키워드 패널·모험 맵처럼 트리거 자체가 연 화면 위에서 시작하는 안내가 있다.</summary>
    public static void TryFire(EOutgameTutorialTrigger _trigger, Func<bool> _isCurrent = null)
    {
        if (_trigger == EOutgameTutorialTrigger.None) return;
        if (_isCurrent != null && !_isCurrent()) return;
        if (!OutgameTutorialRunner.HasPending(_trigger)) return;

        if (StageBusyForGuided || HasEarlierContentIntro(_trigger)
            || HasBlockingPopup(_trigger) || OutgameTutorialRunner.IsGuidedRunning)
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
            if (StageBusyForGuided || HasEarlierContentIntro(t_request.Key)
                || HasBlockingPopup(t_request.Key) || OutgameTutorialRunner.IsGuidedRunning) return;

            // Fire의 이벤트가 다른 안내를 요청할 수 있으므로 먼저 제거한다.
            this.m_pendingGuides.Remove(t_request.Key);
            OutgameTutorialRunner.Fire(t_request.Key);
            if (t_request.Key == EOutgameTutorialTrigger.CollectionTabFirstEnter && !OutgameTutorialRunner.IsGuidedRunning
                && CardDetailOverlayView.HasPendingIntroductionForOwnedCard())
                m_pendingOwnedIntroduction = t_request.Value ?? (() => true);
            return;
        }
    }

    void ResumeOwnedIntroduction()
    {
        if (m_pendingOwnedIntroduction == null) return;
        if (!m_pendingOwnedIntroduction()) { m_pendingOwnedIntroduction = null; return; }
        if (StageBusyForGuided || HasEarlierContentIntro() || HasBlockingPopup()
            || OutgameTutorialRunner.IsGuidedRunning || CardDetailOverlayView.IsOpen) return;
        m_pendingOwnedIntroduction = null;
        CardDetailOverlayView.TryOpenPendingIntroduction();
    }

    static bool StageBusyForGuided
        => SynergyIntroduction.IsActive || ContentUnlockPresentation.IsPlaying || OutgameTutorialGateUI.IsShowing
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
    }

    bool HasPriorityActivity => !GameInitialization.IsReady || !isActiveAndEnabled
        || CurtainView.IsBusy || LoadingCoverView.IsCovering
        || (SceneTransitionVideo.Instance != null && SceneTransitionVideo.Instance.IsPlaying)
        || (m_launcher != null && m_launcher.IsRunning)
        || LobbyRankEffectDirector.Playing || LobbyGainEffectDirector.Playing
        || RankPromoteOverlay.IsOpen || RewardClaimPopup.IsOpen || AdventureRewardFlow.IsClaiming
        || PackOpenOverlay.IsOpen || CardDetailOverlayView.IsOpen || AlbumPageOverlayView.IsOpen
        || CardRewardOverlay.IsOpen || CardSetRewardOverlay.IsOpen || PackRewardOverlay.IsOpen
        || OutgameTutorialGateUI.IsShowing || UnlockIntroOverlay.IsOpen;

    bool SafeToPresent(bool _contentIntro = false)
        => !HasPriorityActivity && (_contentIntro || !OutgameTutorialRunner.IsGuidedRunning)
        && !SynergyIntroduction.IsActive && UIPoolManager.instance != null
        && !HasBlockingPopup();

    static bool HasEarlierContentIntro(EOutgameTutorialTrigger _trigger = EOutgameTutorialTrigger.None)
        => OutgameTutorialRunner.TryGetPendingContentIntro(out var t_pending) && t_pending != _trigger;

    static bool HasBlockingPopup(EOutgameTutorialTrigger _trigger = EOutgameTutorialTrigger.None)
    {
        Type t_target = _trigger == EOutgameTutorialTrigger.GuideMissionIntroduction ? typeof(GuideMissionPanel)
            : _trigger == EOutgameTutorialTrigger.KeywordGrowthFirstOpen ? typeof(KeywordGrowthPanel) : null;
        return UIPoolManager.instance != null
            && UIPoolManager.instance.HasVisibleUIExcept(ignoreMissionCutIn: true, exceptType: t_target);
    }

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
        // 아직 재생을 시작하지 않은 해금 소개도 후속 온보딩보다 먼저 처리한다.
        if (OutgameTutorialRunner.TryGetPendingContentIntro(out var t_trigger))
        {
            if (!AdventureMapOpen && ContentUnlockPresentation.IsReady && CanPresentContentUnlock)
                TryFire(t_trigger);
            return;
        }
        this.ResumePendingGuide();
        this.ResumeOwnedIntroduction();
        if (!CanPresent) return;
        if (OutgameTutorialRunner.IsRunning) return;
        if (s_missionIntroRequested && OutgameTutorialProgress.IsTriggerDone(EOutgameTutorialTrigger.GuideMissionIntroduction))
            s_missionIntroRequested = false;
        if (s_missionIntroRequested && OutgameTutorialRunner.HasPending(EOutgameTutorialTrigger.GuideMissionIntroduction))
        {
            s_missionIntroRequested = false;
            TryFire(EOutgameTutorialTrigger.GuideMissionIntroduction);
            if (OutgameTutorialRunner.IsGuidedRunning) return;
        }
        if (SynergyIntroduction.HasPending) SynergyIntroduction.TryBegin();
    }

    void OnDisable()
    {
        this.m_pendingGuides.Clear();
        this.m_pendingOwnedIntroduction = null;
        SynergyIntroduction.CancelPresentation();
        if (s_instance == this) s_instance = null;
    }

    void OnEnable() => s_instance = this;
}
