using System.Collections.Generic;
using UnityEngine;

public sealed partial class GuidanceCoordinator
{
    readonly HashSet<GuideMissionFlow> m_deferredFlows = new HashSet<GuideMissionFlow>();
    GuideMissionFlow m_flow;
    LobbyTabController m_flowShell;
    bool m_flowNavigating;
    bool m_flowArrived;
    bool m_flowStarted;
    int m_flowVersion;
    int m_flowSession;
    int m_flowSelection;
    float m_flowNavigationDeadline;

    static bool HasPendingMissionFlow => s_instance != null
        && (s_instance.m_flow != null || s_instance.FindMissionFlow() != null);

    /// <summary>현재 미션 안내를 명시적으로 재개한다.</summary>
    public static void RequestCurrentMission()
    {
        if (s_instance == null) return;
        var t_flows = OutgameTutorialRunner.Data?.guide.guideFlows;
        if (t_flows == null) return;
        foreach (var t_flow in t_flows)
        {
            if (t_flow == null || !GuideMissionFlows.IsEligible(t_flow)) continue;
            s_instance.m_deferredFlows.Remove(t_flow);
            OutgameTutorialRunner.ResumeDeferred(t_flow.tutorial);
        }
    }

    /// <summary>미션 이동을 해금·온보딩 흐름으로 인계한다.</summary>
    public static bool TryRequestMission(string _missionId)
    {
        if (s_instance == null || !GuideMissionProgress.IsCurrent(_missionId)) return false;
        if (_missionId == MATCH_MISSION_ID) return s_instance.RequestMatchMission();
        foreach (var t_flow in GuideMissionFlows.All)
        {
            if (t_flow == null || t_flow.missionId != _missionId) continue;
            RequestCurrentMission();
            return PendingIntros(t_flow).Count > 0 || OutgameTutorialRunner.HasPending(t_flow.tutorial);
        }
        return false;
    }

    GuideMissionFlow FindMissionFlow()
    {
        var t_flows = OutgameTutorialRunner.Data?.guide.guideFlows;
        if (t_flows == null) return null;
        foreach (var t_flow in t_flows)
        {
            if (t_flow == null || m_deferredFlows.Contains(t_flow) || !GuideMissionFlows.IsEligible(t_flow)) continue;
            if (PendingIntros(t_flow).Count > 0 || OutgameTutorialRunner.HasPending(t_flow.tutorial)) return t_flow;
        }
        return null;
    }

    static List<EContentUnlockIntro> PendingIntros(GuideMissionFlow _flow)
    {
        var t_pending = new List<EContentUnlockIntro>();
        if (_flow.contentIntros != null)
            foreach (var t_intro in _flow.contentIntros)
                if (ContentUnlockManager.IsPending(ContentUnlockIntroDef.KeyOf(t_intro))) t_pending.Add(t_intro);
        return t_pending;
    }

    bool AdvanceMissionFlow()
    {
        if (m_flowSession != ContentUnlockManager.SessionVersion)
        {
            CancelMissionFlow(false);
            m_deferredFlows.Clear();
            m_flowSession = ContentUnlockManager.SessionVersion;
        }
        if (m_flow != null && !GuideMissionFlows.IsEligible(m_flow)) CancelMissionFlow(true);
        if (m_flow == null)
        {
            if (OutgameTutorialRunner.IsRunning || OutgameTutorialRunner.IsGuidedRunning) return false;
            m_flow = FindMissionFlow();
            if (m_flow == null) return false;
        }
        if (m_flowStarted)
        {
            if (OutgameTutorialRunner.IsGuidedRunning) return true;
            CancelMissionFlow(!OutgameTutorialProgress.IsTriggerDone(m_flow.tutorial));
            return true;
        }
        if (m_flowNavigating)
        {
            if (HasBlockingPopup()) m_flowNavigationDeadline = Time.unscaledTime + 5f;
            if (m_flowShell == null || m_flowShell.SelectionRequestVersion != m_flowSelection
                || Time.unscaledTime > m_flowNavigationDeadline) CancelMissionFlow(true);
            return true;
        }
        if (StageBusyForGuided || OutgameTutorialRunner.IsGuidedRunning || HasBlockingPopup()) return true;
        var t_intros = PendingIntros(m_flow);
        if (t_intros.Count > 0)
        {
            if (!SafeToPresent()) return true;
            if (AdventureMapOpen) return true;
            if (!ContentUnlockPresentation.IsReady)
            {
                NavigateFlow(EOutgameFeature.LobbyMatchTab, false);
                return true;
            }
            if (t_intros.Count > 1) t_intros.RemoveRange(1, t_intros.Count - 1);
            int t_version = m_flowVersion;
            ContentUnlockPresentation.TryPresent(t_intros, () =>
            {
                if (m_flow == null || t_version != m_flowVersion) return;
                foreach (var t_intro in t_intros) ContentUnlockManager.MarkPresented(ContentUnlockIntroDef.KeyOf(t_intro));
            }, () => { if (t_version == m_flowVersion) CancelMissionFlow(true); });
            return true;
        }
        if (m_flow.tutorial == EOutgameTutorialTrigger.None)
        {
            CancelMissionFlow(false);
            return true;
        }
        if (!OutgameTutorialRunner.HasPending(m_flow.tutorial))
        {
            CancelMissionFlow(false);
            return true;
        }
        if (!m_flowArrived)
        {
            if (!SafeToPresent()) return true;
            NavigateFlow(m_flow.destination, true);
            return true;
        }
        m_flowStarted = StartGuide(m_flow.tutorial);
        if (!m_flowStarted) CancelMissionFlow(true);
        return true;
    }

    void NavigateFlow(EOutgameFeature _destination, bool _forTutorial)
    {
        if (_destination == EOutgameFeature.None) { m_flowArrived = true; return; }
        if (m_flowShell == null) m_flowShell = GetComponent<LobbyTabController>();
        if (m_flowShell == null) { CancelMissionFlow(true); return; }
        int t_version = m_flowVersion;
        m_flowNavigating = true;
        m_flowNavigationDeadline = Time.unscaledTime + 5f;
        bool t_accepted = m_flowShell.TrySelectFeature(
            _destination == EOutgameFeature.Adventure ? EOutgameFeature.LobbyMatchTab : _destination,
            _onArrived: () =>
            {
                if (m_flow == null || t_version != m_flowVersion) return;
                m_flowNavigating = false;
                if (!GuideMissionFlows.IsEligible(m_flow)) { CancelMissionFlow(true); return; }
                if (_destination == EOutgameFeature.Adventure
                    && (m_launcher == null || !m_launcher.TryOpenAdventureMapAt(-1)))
                { CancelMissionFlow(true); return; }
                m_flowArrived = _forTutorial;
            });
        m_flowSelection = m_flowShell.SelectionRequestVersion;
        if (!t_accepted) CancelMissionFlow(true);
    }

    static bool StartGuide(EOutgameTutorialTrigger _trigger)
    {
        if (!OutgameTutorialRunner.HasPending(_trigger)) return false;
        if (_trigger == EOutgameTutorialTrigger.CollectionTabFirstEnter
            && (!OutgameTutorialRunner.TryGetGuidedChapter(_trigger, out _, out var t_chapter)
                || !OutgameTutorialGuide.PrepareEnhanceCard(t_chapter))) return false;
        OutgameTutorialRunner.Fire(_trigger);
        return OutgameTutorialRunner.IsGuidedRunning;
    }

    void CancelMissionFlow(bool _defer)
    {
        var t_flow = m_flow;
        m_flowVersion++;
        m_flow = null;
        m_flowNavigating = false;
        m_flowArrived = false;
        m_flowStarted = false;
        if (t_flow == null) return;
        if (_defer) m_deferredFlows.Add(t_flow);
        if (OutgameTutorialRunner.IsGuidedRunning && OutgameTutorialRunner.GuidedTrigger == t_flow.tutorial)
            OutgameTutorialRunner.AbortGuided(t_flow.tutorial);
        ContentUnlockPresentation.CancelCurrent();
    }
}
