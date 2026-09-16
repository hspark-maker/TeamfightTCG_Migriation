using UnityEngine;
using UnityEngine.UI;

public sealed partial class GuidanceCoordinator
{
    const string MATCH_MISSION_ID = "guide.02";
    const float MATCH_NAVIGATION_TIMEOUT = 5f;

    bool m_matchRequested;
    bool m_matchArrived;
    bool m_matchShowing;
    int m_matchVersion;
    int m_matchSelection;
    float m_matchDeadline;
    Button m_matchButton;

    bool CanRequestMatchMission() => CanGuideMatchMission() && m_shell != null && isActiveAndEnabled;

    bool RequestMatchMission()
    {
        if (!CanRequestMatchMission()) return false;
        if (m_matchRequested) return true;

        m_matchRequested = true;
        m_matchDeadline = Time.unscaledTime + MATCH_NAVIGATION_TIMEOUT;
        int t_version = ++m_matchVersion;
        bool t_accepted = m_shell.TrySelectFeature(EOutgameFeature.LobbyMatchTab, _onArrived: () =>
        {
            if (!m_matchRequested || t_version != m_matchVersion) return;
            m_matchArrived = true;
        });
        m_matchSelection = m_shell.SelectionRequestVersion;
        if (!t_accepted) CancelMatchMission();
        return t_accepted;
    }

    bool AdvanceMatchMission()
    {
        if (!m_matchRequested) return false;
        if (!CanGuideMatchMission() || !GameInitialization.IsReady || m_shell == null
            || m_shell.SelectionRequestVersion != m_matchSelection
            || CurtainView.IsBusy || LoadingCoverView.IsCovering
            || (m_launcher != null && m_launcher.IsRunning)
            || (SceneTransitionVideo.Instance != null && SceneTransitionVideo.Instance.IsPlaying))
        {
            CancelMatchMission();
            return false;
        }

        if (m_matchShowing)
        {
            if (m_matchButton == null || !m_matchButton.isActiveAndEnabled
                || !m_matchButton.IsInteractable() || !OutgameTutorialGateUI.IsShowing
                || OutgameTutorialRunner.IsRunning || OutgameTutorialRunner.IsGuidedRunning)
                CancelMatchMission();
            return m_matchRequested;
        }

        // 다른 안내가 끝날 때까지 조정기의 기존 흐름을 계속 진행시킨다.
        if (m_flow != null || StageBusyForGuided || !SafeToPresent()
            || OutgameTutorialRunner.IsRunning || AdventureMapOpen)
        {
            m_matchDeadline = Time.unscaledTime + MATCH_NAVIGATION_TIMEOUT;
            return false;
        }
        if (!m_matchArrived)
        {
            if (Time.unscaledTime > m_matchDeadline) CancelMatchMission();
            return m_matchRequested;
        }

        if (!TutorialAnchorRegistry.TryGet(EOutgameTutorialAnchor.LobbyPlayButton,
                out RectTransform t_target, out Button t_button)
            || t_button == null || !t_button.isActiveAndEnabled || !t_button.IsInteractable())
        {
            CancelMatchMission();
            return false;
        }

        OutgameTutorialGateUI t_gate = OutgameTutorialBridge.EnsureGateForGuidance();
        if (t_gate == null)
        {
            CancelMatchMission();
            return false;
        }
        m_matchButton = t_button;
        m_matchShowing = true;
        t_gate.ShowGate(this, t_target, t_button, string.Empty, CancelMatchMission);
        return true;
    }

    static bool CanGuideMatchMission()
    {
        MissionDefinition t_current = GuideMissionProgress.Current;
        return t_current != null && t_current.Id == MATCH_MISSION_ID
            && OutgameTutorialProgress.IsCompleted && !MissionManager.IsComplete(t_current)
            && !MissionCommands.IsInFlight(t_current.Id);
    }

    void CancelMatchMission()
    {
        if (m_matchShowing && OutgameTutorialGateUI.Instance != null)
            OutgameTutorialGateUI.Instance.Clear(this);
        m_matchVersion++;
        m_matchRequested = false;
        m_matchArrived = false;
        m_matchShowing = false;
        m_matchButton = null;
    }
}
