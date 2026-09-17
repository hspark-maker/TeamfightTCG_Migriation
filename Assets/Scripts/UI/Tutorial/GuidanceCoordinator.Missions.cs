using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed partial class GuidanceCoordinator
{
    public enum EMissionGuideAction { None, Start, Resume }

    sealed class GuidePreparationException : Exception
    {
        public GuidePreparationException()
            : base("강화 가능한 카드나 샤드가 부족합니다. 준비되면 안내를 이어갈 수 있어요.") { }
    }

    GuideMissionFlow m_flow;
    GuideMissionFlow m_unconfirmedIntroFlow;
    bool m_flowStarted;
    bool m_flowPreparing;
    bool m_flowLocked;
    bool m_flowDeferred;
    bool m_retryRequested;
    string m_requestedMissionId;
    bool m_applicationPaused;
    int m_flowVersion;
    int m_flowSession;
    int m_internalNavigation;
    string m_lastFlowFailure;
    CancellationTokenSource m_flowCancellation;

    public static bool IsInputLocked => GuideMissionTrackerView.IsShowingNextMission
        || (s_instance != null && s_instance.m_flowLocked);
    public static bool IsRestoring => s_instance != null && s_instance.m_flowPreparing;
    public static bool IsInternalNavigation => s_instance != null && s_instance.m_internalNavigation > 0;
    public static bool IsCurrentTabAnchor(EOutgameTutorialAnchor _anchor)
        => s_instance != null && s_instance.m_shell != null && s_instance.m_shell.IsCurrentAnchorSelected(_anchor);

    static bool HasPendingMissionFlow => s_instance != null
        && (s_instance.m_flow != null || s_instance.FindMissionFlow() != null);

    /// <summary>현재 안내가 허용한 사용자 조작만 통과시킨다.</summary>
    public static bool AllowsUserAction(EOutgameTutorialAnchor _anchor)
        => !IsInputLocked || IsInternalNavigation
            || (!IsRestoring && (OutgameTutorialGateUI.Instance == null || !OutgameTutorialGateUI.Instance.IsTransitionOnly)
                && _anchor != EOutgameTutorialAnchor.None
                && OutgameTutorialGuide.TryGetCurrentStep(out var t_step) && t_step.Anchor == _anchor);

    /// <summary>안내가 허용한 이동만 통과시킨다.</summary>
    public static bool AllowsUserNavigation(EOutgameTutorialAnchor _anchor)
        => IsInternalNavigation || (!IsContentIntroBlockingNavigation
            && !OnboardingSession.IsBusy && AllowsUserAction(_anchor));

    /// <summary>소개 무대 준비 후부터 아이콘 도착까지 이탈을 막는다. 다른 탭에 있으면 무대로 복귀할 수 있다.</summary>
    public static bool IsContentIntroBlockingNavigation => ContentUnlockPresentation.IsPlaying
        || (ContentUnlockPresentation.IsReady && OnboardingSession.IsActive
            && OutgameTutorialGuide.TryGetCurrentStep(out var t_step)
            && t_step.Completion == EOutgameTutorialCompletion.ContentUnlockIntro);

    /// <summary>조정기와 스텝 실행기가 수행하는 화면 이동의 수명이다.</summary>
    public static IDisposable InternalNavigation() => new NavigationScope(s_instance);

    sealed class NavigationScope : IDisposable
    {
        GuidanceCoordinator m_owner;
        public NavigationScope(GuidanceCoordinator _owner)
        {
            m_owner = _owner;
            if (m_owner != null) m_owner.m_internalNavigation++;
        }
        public void Dispose()
        {
            if (m_owner != null) m_owner.m_internalNavigation--;
            m_owner = null;
        }
    }

    /// <summary>미완료 안내를 보존하고 일반 이동을 되돌린다.</summary>
    public static void DeferCurrentGuide(string _reason)
    {
        if (s_instance == null) return;
        s_instance.CancelMissionFlow(true);
        s_instance.ShowFlowFailure(_reason);
    }

    public static void RequestCurrentMission()
    {
        if (s_instance == null) return;
        if (s_instance.m_flowSession != ContentUnlockManager.SessionVersion)
        {
            s_instance.CancelMissionFlow(false);
            s_instance.m_unconfirmedIntroFlow = null;
            s_instance.m_flowSession = ContentUnlockManager.SessionVersion;
        }
        s_instance.m_retryRequested = true;
        s_instance.m_requestedMissionId = GuideMissionProgress.Current?.Id;
        s_instance.m_lastFlowFailure = null;
        foreach (var t_flow in GuideMissionFlows.All)
            if (t_flow != null && GuideMissionFlows.IsEligible(t_flow))
                OutgameTutorialRunner.ResumeDeferred(t_flow.tutorial);
    }

    public static bool TryRequestMission(string _missionId)
    {
        if (s_instance == null || !GuideMissionProgress.IsCurrent(_missionId)) return false;
        if (IsInputLocked) return true;
        if (GetMissionGuideAction(_missionId) == EMissionGuideAction.None) return false;
        if (_missionId == MATCH_MISSION_ID) return s_instance.RequestMatchMission();
        RequestCurrentMission();
        return true;
    }

    /// <summary>미루기 상태를 바꾸지 않고 현재 미션의 안내 시작·재개 여부를 조회한다.</summary>
    public static EMissionGuideAction GetMissionGuideAction(string _missionId)
    {
        if (s_instance == null || !GuideMissionProgress.IsCurrent(_missionId)) return EMissionGuideAction.None;
        if (_missionId == MATCH_MISSION_ID)
            return s_instance.CanRequestMatchMission() ? EMissionGuideAction.Start : EMissionGuideAction.None;
        foreach (var t_flow in GuideMissionFlows.All)
        {
            if (t_flow == null || t_flow.missionId != _missionId) continue;
            if (GuideResume.IsFor(t_flow.tutorial)) return EMissionGuideAction.Resume;
            return PendingIntros(t_flow).Count > 0 || OutgameTutorialRunner.HasPending(t_flow.tutorial, _includeDeferred: true)
                ? EMissionGuideAction.Start : EMissionGuideAction.None;
        }
        return EMissionGuideAction.None;
    }

    GuideMissionFlow FindMissionFlow()
    {
        // 마지막 소개가 끝나 pending이 비어도, 실패한 완료 저장은 재시도할 수 있어야 한다.
        if (m_retryRequested && m_unconfirmedIntroFlow != null) return m_unconfirmedIntroFlow;
        if (m_retryRequested && GuideMissionProgress.IsCurrent(m_requestedMissionId) && GuideResume.HasPending)
        {
            foreach (var t_flow in GuideMissionFlows.All)
                if (t_flow != null && t_flow.missionId == m_requestedMissionId
                    && GuideResume.IsFor(t_flow.tutorial)) return t_flow;
        }
        foreach (var t_flow in GuideMissionFlows.All)
        {
            if (t_flow == null || !GuideMissionFlows.IsEligible(t_flow)) continue;
            // 플레이로 현재 미션에 도달하면 클릭 없이 시작한다. 중단한 안내의 재시도는 별도로 처리한다.
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
            m_unconfirmedIntroFlow = null;
            m_flowDeferred = false;
            m_flowSession = ContentUnlockManager.SessionVersion;
        }
        if (m_applicationPaused || m_flowPreparing) return m_flow != null;
        if (m_flowStarted)
        {
            if (OutgameTutorialRunner.IsGuidedRunning) return true;
            if (OutgameTutorialProgress.IsTriggerDone(m_flow.tutorial)) CompleteFlowAsync(m_flowVersion).Forget();
            else CancelMissionFlow(true);
            return true;
        }
        if (m_flowDeferred && !m_retryRequested) return false;
        if (m_retryRequested && m_unconfirmedIntroFlow == null
            && !GuideMissionProgress.IsCurrent(m_requestedMissionId))
        {
            m_retryRequested = false;
            m_requestedMissionId = null;
        }
        if (OutgameTutorialRunner.IsRunning || OutgameTutorialRunner.IsGuidedRunning) return false;
        var t_flow = FindMissionFlow();
        if (t_flow == null) return false;
        PooledUIBase t_surface = t_flow.tutorial == EOutgameTutorialTrigger.SynergyBattleIntroduction
            ? DeckEditController.OpenEditor : null;
        if (StageBusyForGuided || !SafeToPresent(_except: t_surface)) return false;
        m_retryRequested = false;
        m_flowDeferred = false;
        m_flow = t_flow;
        m_flowLocked = true;
        m_flowPreparing = true;
        int t_version = ++m_flowVersion;
        m_flowCancellation = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        BeginFlowAsync(t_version, m_flowCancellation.Token).Forget();
        return true;
    }

    async UniTask BeginFlowAsync(int _version, CancellationToken _ct)
    {
        try
        {
            ShowTransition();
            var t_flow = m_flow;
            await OnboardingCommands.RecoverPendingAsync(_ct);
            EnsureFlow(_version, _ct);
            if (m_unconfirmedIntroFlow != null)
            {
                if (!await GuideResume.SaveConfirmedAsync(_ct))
                    throw new InvalidOperationException("해금 안내 완료를 저장하지 못했습니다. 재시도하면 저장부터 이어갑니다.");
                EnsureFlow(_version, _ct);
                m_unconfirmedIntroFlow = null;
                if (!GuideMissionFlows.IsEligible(t_flow)) { CancelMissionFlow(false); return; }
            }
            if (t_flow.tutorial != EOutgameTutorialTrigger.None
                && !OutgameTutorialProgress.IsTriggerDone(t_flow.tutorial)) GuideResume.Begin(t_flow);
            EnsureFlow(_version, _ct);
            var t_intros = PendingIntros(t_flow);
            if (t_intros.Count > 0)
            {
                if (!ContentUnlockPresentation.IsReady) await SelectFlowTabAsync(EOutgameFeature.LobbyMatchTab, _ct);
                foreach (var t_intro in t_intros)
                {
                    EnsureFlow(_version, _ct);
                    ClearTransition();
                    var t_done = new UniTaskCompletionSource<bool>();
                    if (!ContentUnlockPresentation.TryPresent(new[] { t_intro },
                            () => t_done.TrySetResult(true), () => t_done.TrySetResult(false)))
                        throw new InvalidOperationException("해금 안내 화면을 준비하지 못했습니다.");
                    if (!await t_done.Task.AttachExternalCancellation(_ct))
                        throw new InvalidOperationException("해금 안내가 중단되었습니다.");
                    EnsureFlow(_version, _ct);
                    ShowTransition();
                    ContentUnlockManager.MarkPresented(ContentUnlockIntroDef.KeyOf(t_intro));
                    m_unconfirmedIntroFlow = t_flow;
                    if (!await GuideResume.SaveConfirmedAsync(_ct))
                        throw new InvalidOperationException("해금 안내 완료를 저장하지 못했습니다. 재시도하면 저장부터 이어갑니다.");
                    EnsureFlow(_version, _ct);
                    m_unconfirmedIntroFlow = null;
                }
            }
            if (GuideResume.IsFor(t_flow.tutorial)) GuideResume.Record.IntroductionSeen = true;
            DataSaveManager.Save();
            EnsureFlow(_version, _ct);
            if (t_flow.tutorial == EOutgameTutorialTrigger.None
                || OutgameTutorialProgress.IsTriggerDone(t_flow.tutorial)) { CancelMissionFlow(false); return; }
            if (!PrepareFlowTarget(t_flow))
                throw new GuidePreparationException();
            if (t_flow.tutorial == EOutgameTutorialTrigger.CollectionTabFirstEnter
                || t_flow.tutorial == EOutgameTutorialTrigger.SynergyGrowthIntroduction)
                GuideResume.SetTarget(OutgameTutorialGuide.TargetCardId, OutgameTutorialGuide.TargetLevel);
            await RestoreFlowSurfaceAsync(t_flow, _ct);
            EnsureFlow(_version, _ct);
            EnsureFlow(_version, _ct);
            m_flowPreparing = false;
            ClearTransition();
            OutgameTutorialRunner.ResumeDeferred(t_flow.tutorial);
            using (InternalNavigation()) OutgameTutorialRunner.Fire(t_flow.tutorial);
            m_flowStarted = OutgameTutorialRunner.IsGuidedRunning;
            if (!m_flowStarted)
            {
                if (OutgameTutorialProgress.IsTriggerDone(t_flow.tutorial)) await CompleteFlowAsync(_version);
                else throw new InvalidOperationException("온보딩을 시작하지 못했습니다.");
            }
            m_lastFlowFailure = null;
        }
        catch (OperationCanceledException) { }
        catch (GuidePreparationException t_exception)
        {
            if (_version != m_flowVersion) return;
            CancelMissionFlow(true);
            ShowPreparationFailure(t_exception.Message);
        }
        catch (Exception t_exception)
        {
            if (_version == m_flowVersion) DeferCurrentGuide(t_exception.Message);
        }
    }

    static bool PrepareFlowTarget(GuideMissionFlow _flow)
    {
        if (_flow.tutorial != EOutgameTutorialTrigger.CollectionTabFirstEnter
            && _flow.tutorial != EOutgameTutorialTrigger.SynergyGrowthIntroduction) return true;
        if (GuideResume.Record != null && (GuideResume.Record.CardId > 0 || GuideResume.Record.GoalReached))
        {
            if (OutgameTutorialGuide.RestoreResumeCard())
            {
                if (_flow.tutorial == EOutgameTutorialTrigger.CollectionTabFirstEnter
                    && !OutgameTutorialGuide.IsGrowthGoalReached
                    && OutgameTutorialRunner.TryGetGuidedChapter(_flow.tutorial, out _, out var t_savedChapter))
                    OutgameTutorialGuide.PrepareEnhanceCard(t_savedChapter);
                return OutgameTutorialGuide.IsGrowthGoalReached || OutgameTutorialGuide.CanContinueEnhance();
            }
            GuideResume.SetTarget(0, 0);
        }
        if (_flow.tutorial == EOutgameTutorialTrigger.SynergyGrowthIntroduction)
        {
            OutgameTutorialGuide.PrepareSynergyGrowth();
            return OutgameTutorialGuide.IsGrowthGoalReached || OutgameTutorialGuide.CanContinueEnhance();
        }
        if (!OutgameTutorialRunner.TryGetGuidedChapter(_flow.tutorial, out _, out var t_chapter))
            throw new InvalidOperationException("강화 안내 데이터를 찾지 못했습니다.");
        return OutgameTutorialGuide.PrepareEnhanceCard(t_chapter);
    }

    async UniTask CompleteFlowAsync(int _version)
    {
        m_flowPreparing = true;
        ShowTransition();
        try
        {
            if (!await GuideResume.SaveConfirmedAsync(m_flowCancellation.Token))
                throw new InvalidOperationException("안내 완료 기록을 저장하지 못했습니다.");
            if (_version == m_flowVersion) CancelMissionFlow(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception t_exception)
        {
            if (_version == m_flowVersion) DeferCurrentGuide(t_exception.Message);
        }
    }

    void EnsureFlow(int _version, CancellationToken _ct)
    {
        _ct.ThrowIfCancellationRequested();
        if (_version != m_flowVersion || m_flow == null) throw new OperationCanceledException(_ct);
    }

    void ShowTransition() => OutgameTutorialBridge.EnsureGateForGuidance()?.ShowTransitionGate(this);
    void ClearTransition() => OutgameTutorialGateUI.Instance?.Clear(this);

    void CancelMissionFlow(bool _defer, bool _closeSurface = true)
    {
        bool t_ownedGuidance = m_flow != null || OutgameTutorialRunner.IsGuidedRunning;
        var t_trigger = m_flow != null ? m_flow.tutorial : OutgameTutorialRunner.GuidedTrigger;
        bool t_ownedAdventure = m_flow != null && m_flow.tutorial == EOutgameTutorialTrigger.AdventureUnlocked;
        bool t_ownedGrowth = m_flow != null && (m_flow.tutorial == EOutgameTutorialTrigger.CollectionTabFirstEnter
            || m_flow.tutorial == EOutgameTutorialTrigger.SynergyGrowthIntroduction);
        m_flowVersion++;
        m_flowCancellation?.Cancel();
        m_flowCancellation?.Dispose();
        m_flowCancellation = null;
        m_flow = null;
        m_flowPreparing = false;
        m_flowStarted = false;
        m_flowLocked = false;
        m_flowDeferred = _defer;
        m_retryRequested = false;
        ClearTransition();
        m_requestedMissionId = null;
        using (InternalNavigation())
        {
            if (t_ownedGuidance) OnboardingSession.Suspend();
            OutgameTutorialRunner.AbortGuided(_defer ? t_trigger : EOutgameTutorialTrigger.None);
            ContentUnlockPresentation.CancelCurrent();
            if (t_ownedAdventure && _defer && _closeSurface) m_launcher?.CancelGuidedAdventureEntry();
            if (t_ownedGrowth && _defer && _closeSurface)
            {
                CardDetailOverlayView.Close();
                AlbumPageOverlayView.CloseOpen();
            }
        }
    }

    void ShowPreparationFailure(string _reason)
    {
        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = _reason,
            yesText = "재시도",
            yesAction = RequestCurrentMission,
            noText = "나중에",
            noAction = ReturnToMatchAfterPreparationFailure,
        });
    }

    void ReturnToMatchAfterPreparationFailure()
    {
        using (InternalNavigation()) m_shell?.TrySelectFeature(EOutgameFeature.LobbyMatchTab);
    }

    void ShowFlowFailure(string _reason)
    {
        if (string.IsNullOrEmpty(_reason) || m_lastFlowFailure == _reason) return;
        m_lastFlowFailure = _reason;
        Debug.LogWarning($"[GuidanceCoordinator] {_reason}");
        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = _reason,
            yesText = "재시도",
            yesAction = RequestCurrentMission,
            noText = "종료",
            noAction = QuitAfterFlowFailure,
        });
    }
    static void QuitAfterFlowFailure()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

}
