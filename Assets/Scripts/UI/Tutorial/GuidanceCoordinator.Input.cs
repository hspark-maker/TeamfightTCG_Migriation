using UnityEngine;
using UnityEngine.UI;

public sealed partial class GuidanceCoordinator
{
    readonly GuidanceInputPolicy m_input = new GuidanceInputPolicy();
    readonly object m_forcedOwner = new object();
    readonly object m_presentationOwner = new object();
    GuidanceInputPolicy.Lease m_forcedInput;
    GuidanceInputPolicy.Lease m_presentationInput;
    GameObject m_inputShield;
    int m_inputSession = -1;

    public static EGuidanceInputMode InputMode
    {
        get
        {
            if (s_instance == null) return EGuidanceInputMode.Free;
            s_instance.RefreshInputPolicy();
            return s_instance.m_input.Mode;
        }
    }

    public static bool AllowsInput(EGuidanceInputAction action,
        EOutgameTutorialAnchor anchor = EOutgameTutorialAnchor.None)
    {
        if (IsInternalNavigation) return true;
        if (s_instance == null) return true;
        s_instance.RefreshInputPolicy();
        return s_instance.m_input.Allows(action, anchor);
    }

    // 다음 안내가 화면을 준비하기 전에 이전 안내의 입력막이 사라지지 않게 예약한다.
    public static void ReserveNextMissionInput()
    {
        if (s_instance == null || s_instance.m_flowDeferred || OutgameTutorialRunner.IsRunning) return;
        var flow = s_instance.FindMissionFlow();
        if (flow == null) return;
        s_instance.m_resumeFlow = flow;
        s_instance.m_retryRequested = true;
        s_instance.m_requestedMissionId = GuideMissionProgress.Current?.Id;
        s_instance.HoldMissionInput();
    }

    void HoldMissionInput()
    {
        RefreshInputPolicy();
        if (m_flowInput == null || !m_flowInput.IsCurrent) m_flowInput = m_input.Acquire(this);
        m_flowInput.Set(EGuidanceInputMode.Transition);
    }

    void ReleaseMissionInput()
    {
        m_flowInput?.Dispose();
        m_flowInput = null;
    }

    void RefreshInputPolicy()
    {
        if (m_inputSession != ContentUnlockManager.SessionVersion)
        {
            m_input.Clear();
            m_inputSession = ContentUnlockManager.SessionVersion;
            m_forcedInput = m_presentationInput = m_flowInput = null;
        }
        if (m_flowInput == null && !m_flowDeferred && !m_applicationPaused
            && !OutgameTutorialRunner.IsRunning && !OutgameTutorialRunner.IsGuidedRunning
            && !StageBusyForGuided && SafeToPresent())
        {
            var pending = FindMissionFlow();
            if (pending != null)
            {
                m_resumeFlow = pending;
                m_retryRequested = true;
                m_requestedMissionId = GuideMissionProgress.Current?.Id;
                m_flowInput = m_input.Acquire(this);
            }
        }
        bool hasStep = OutgameTutorialGuide.TryGetCurrentStep(out var step);
        bool forced = OutgameTutorialRunner.IsRunning && hasStep
            && (!OutgameFeatureLock.IsFtueFreeNavigation
                || step.Completion == EOutgameTutorialCompletion.RankEffect
                || step.Completion == EOutgameTutorialCompletion.ContentUnlockIntro);
        bool defeatWaiting = OutgameTutorialRunner.IsDefeatEnhanceInterlude && !OutgameTutorialRunner.IsGuidedRunning;
        bool standaloneGuide = OutgameTutorialRunner.IsGuidedRunning && m_flowInput == null;
        if (forced || defeatWaiting || standaloneGuide)
        {
            if (m_forcedInput == null || !m_forcedInput.IsCurrent) m_forcedInput = m_input.Acquire(m_forcedOwner);
            SetStepInput(m_forcedInput, hasStep ? step : null, defeatWaiting);
        }
        else { m_forcedInput?.Dispose(); m_forcedInput = null; }

        if (m_flowInput != null && m_flowInput.IsCurrent)
            SetStepInput(m_flowInput, hasStep ? step : null,
                m_flowPreparing || m_applicationPaused || m_flow == null || !m_flowStarted);

        bool presentation = IsLobbyPresentationBlockingNavigation || OnboardingSession.IsBusy
            || (OnboardingSession.IsActive && OnboardingSession.Phase == EOnboardingPhase.Failed);
        if (presentation)
        {
            if (m_presentationInput == null || !m_presentationInput.IsCurrent)
                m_presentationInput = m_input.Acquire(m_presentationOwner);
        }
        else { m_presentationInput?.Dispose(); m_presentationInput = null; }
    }

    static void SetStepInput(GuidanceInputPolicy.Lease lease, TutorialStepDef step, bool preparing)
    {
        bool transition = preparing || step == null
            || step.Completion == EOutgameTutorialCompletion.RankEffect
            || step.Completion == EOutgameTutorialCompletion.ContentUnlockIntro
            || OnboardingSession.Phase != EOnboardingPhase.Waiting
            || (OutgameTutorialGateUI.Instance != null && OutgameTutorialGateUI.Instance.IsTransitionOnly);
        lease.Set(transition ? EGuidanceInputMode.Transition : EGuidanceInputMode.Target,
            step?.Anchor ?? EOutgameTutorialAnchor.None, step?.Completion ?? default);
    }

    void LateUpdate()
    {
        bool block = InputMode == EGuidanceInputMode.Transition;
        if (block && m_inputShield == null)
        {
            m_inputShield = new GameObject("GuidanceInputShield", typeof(RectTransform), typeof(Canvas),
                typeof(GraphicRaycaster), typeof(Image));
            m_inputShield.transform.SetParent(transform, false);
            var rect = (RectTransform)m_inputShield.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var canvas = m_inputShield.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            UiSortingOrder.Stamp(canvas, UiSortingOrder.GuidanceInputShield);
            var image = m_inputShield.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
        }
        if (m_inputShield != null) m_inputShield.SetActive(block);
    }
}
