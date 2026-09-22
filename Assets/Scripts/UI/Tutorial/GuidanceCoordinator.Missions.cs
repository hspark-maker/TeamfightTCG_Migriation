using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed partial class GuidanceCoordinator
{
    public enum EMissionGuideAction { None, Start, Resume }

    GuideMissionFlow m_flow;
    GuideMissionFlow m_unconfirmedIntroFlow;
    bool m_flowStarted;
    bool m_flowPreparing;
    GuidanceInputPolicy.Lease m_flowInput;
    GuideMissionFlow m_resumeFlow;
    bool m_flowDeferred;
    bool m_retryRequested;
    string m_requestedMissionId;
    bool m_applicationPaused;
    int m_flowVersion;
    int m_flowSession;
    int m_internalNavigation;
    string m_lastFlowFailure;
    CancellationTokenSource m_flowCancellation;

    public static bool IsInputLocked => InputMode != EGuidanceInputMode.Free;
    public static bool IsRestoring => s_instance != null && s_instance.m_flowPreparing;
    public static bool IsInternalNavigation => s_instance != null && s_instance.m_internalNavigation > 0;
    public static bool IsCurrentTabAnchor(EOutgameTutorialAnchor _anchor)
        => s_instance != null && s_instance.m_shell != null && s_instance.m_shell.IsCurrentAnchorSelected(_anchor);

    internal static bool IsLobbyTabAnchor(EOutgameTutorialAnchor _anchor)
        => _anchor == EOutgameTutorialAnchor.LobbyPackTab || _anchor == EOutgameTutorialAnchor.LobbyDeckTab
            || _anchor == EOutgameTutorialAnchor.LobbyCollectionTab || _anchor == EOutgameTutorialAnchor.LobbyMatchTab;

    static bool HasPendingMissionFlow => s_instance != null
        && (s_instance.m_flow != null || s_instance.FindMissionFlow() != null);

    /// <summary>현재 안내가 허용한 사용자 조작만 통과시킨다.</summary>
    public static bool AllowsUserAction(EOutgameTutorialAnchor _anchor)
        => AllowsInput(EGuidanceInputAction.Activate, _anchor);

    public static bool AllowsUserNavigation(EOutgameTutorialAnchor _anchor)
        => AllowsInput(EGuidanceInputAction.Navigate, _anchor);

    /// <summary>소개 무대 준비 후부터 아이콘 도착까지 이탈을 막는다. 다른 탭에 있으면 무대로 복귀할 수 있다.</summary>
    public static bool IsContentIntroBlockingNavigation => ContentUnlockPresentation.IsPlaying
        || (ContentUnlockPresentation.IsReady && OnboardingSession.IsActive
            && OutgameTutorialGuide.TryGetCurrentStep(out var t_step)
            && t_step.Completion == EOutgameTutorialCompletion.ContentUnlockIntro);

    /// <summary>승급·보상에서 해금 소개와 아이콘 도착까지, 실제 연출 수명으로 탭 입력을 막는다.</summary>
    public static bool IsLobbyPresentationBlockingNavigation => IsContentIntroBlockingNavigation
        || ContentUnlockIntroView.IsOpen || UnlockIntroOverlay.IsOpen
        || RankPromoteOverlay.IsOpen || LobbyRankEffectDirector.Playing
        || GuideMissionTrackerView.IsShowingNextMission;

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
        s_instance.m_resumeFlow = s_instance.m_flow ?? s_instance.m_resumeFlow;
        s_instance.CancelMissionFlow(true);
        s_instance.HoldMissionInput();
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
            if (t_flow == null || t_flow.activation != EGuideFlowActivation.Mission || t_flow.missionId != _missionId) continue;
            if (GuideResume.IsFor(t_flow.tutorial)) return EMissionGuideAction.Resume;
            return PendingIntros(t_flow).Count > 0 || OutgameTutorialRunner.HasPending(t_flow.tutorial, _includeDeferred: true)
                ? EMissionGuideAction.Start : EMissionGuideAction.None;
        }
        return EMissionGuideAction.None;
    }

    GuideMissionFlow FindMissionFlow()
    {
        // FTUE 도중 예약한 강화는 미션 도달과 무관하다. 완료 저장 후 복귀 중 종료한 경우도 여기서 잇는다.
        if (OutgameTutorialRunner.IsDefeatEnhanceInterlude
            && GuideMissionFlows.TryGet(EOutgameTutorialTrigger.CollectionTabFirstEnter, out var t_defeatFlow))
            return t_defeatFlow;
        if (m_retryRequested && m_resumeFlow != null) return m_resumeFlow;
        if (GuideResume.HasPending && !m_flowDeferred)
            foreach (var savedFlow in GuideMissionFlows.All)
                if (savedFlow != null && GuideResume.IsFor(savedFlow.tutorial)) return savedFlow;
        // 안내 시작은 매치 탭에서만 허용하고, 시작 후 목적지 이동은 기존 흐름을 따른다.
        if (!IsCurrentTabAnchor(EOutgameTutorialAnchor.LobbyMatchTab) || AdventureMapOpen) return null;
        // 마지막 소개가 끝나 pending이 비어도, 실패한 완료 저장은 재시도할 수 있어야 한다.
        if (m_retryRequested && m_unconfirmedIntroFlow != null) return m_unconfirmedIntroFlow;
        if (m_retryRequested && GuideMissionProgress.IsCurrent(m_requestedMissionId) && GuideResume.HasPending)
        {
            foreach (var t_flow in GuideMissionFlows.All)
                if (t_flow != null && t_flow.missionId == m_requestedMissionId
                    && GuideResume.IsFor(t_flow.tutorial)) return t_flow;
        }
        foreach (var t_flow in GuideMissionFlows.InPresentationOrder())
        {
            if (t_flow == null || !GuideMissionFlows.IsEligible(t_flow)) continue;
            // 해금 소개를 마쳤어도 미완료 챕터는 별도로 이어간다.
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
            else DeferCurrentGuide("안내가 중단되었습니다. 다시 시도해 주세요.");
            return true;
        }
        if (m_flowDeferred && !m_retryRequested) return false;
        if (m_retryRequested && m_unconfirmedIntroFlow == null && m_resumeFlow == null
            && !GuideMissionProgress.IsCurrent(m_requestedMissionId))
        {
            m_retryRequested = false;
            m_requestedMissionId = null;
        }
        if (OutgameTutorialRunner.IsRunning || OutgameTutorialRunner.IsGuidedRunning) return false;
        var t_flow = FindMissionFlow();
        if (t_flow == null) return false;
        PooledUIBase t_surface = t_flow.tutorial == EOutgameTutorialTrigger.SynergyBattleIntroduction
            || (m_retryRequested && m_shell != null && m_shell.CurrentPanel is DeckTabController)
            ? DeckEditController.OpenEditor : null;
        if (StageBusyForGuided || !SafeToPresent(_except: t_surface)) return false;
        m_retryRequested = false;
        m_flowDeferred = false;
        m_flow = t_flow;
        m_flowInput = m_input.Acquire(this);
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
                || OutgameTutorialProgress.IsTriggerDone(t_flow.tutorial))
            {
                if (OutgameTutorialRunner.IsDefeatEnhanceInterlude) await CompleteFlowAsync(_version);
                else CancelMissionFlow(false);
                return;
            }
            if (!PrepareFlowTarget(t_flow))
                throw new InvalidOperationException("안내 대상 카드를 준비하지 못했습니다.");
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
            // 마지막 안내의 완료 저장이 브리지 토큰을 쥐고 있다. 그 처리가 끝나야 취소·FTUE 재개가 유실되지 않는다.
            await UniTask.WaitUntil(() => !OnboardingSession.IsBusy, cancellationToken: m_flowCancellation.Token);
            EnsureFlow(_version, m_flowCancellation.Token);
            if (!await GuideResume.SaveConfirmedAsync(m_flowCancellation.Token))
                throw new InvalidOperationException("안내 완료 기록을 저장하지 못했습니다.");
            if (_version != m_flowVersion) return;
            bool t_resumeFtue = OutgameTutorialRunner.IsDefeatEnhanceInterlude;
            if (t_resumeFtue)
            {
                using (InternalNavigation())
                {
                    CardDetailOverlayView.Close();
                    AlbumPageOverlayView.CloseOpen();
                }
                await SelectFlowTabAsync(EOutgameFeature.LobbyMatchTab, m_flowCancellation.Token);
                EnsureFlow(_version, m_flowCancellation.Token);
            }
            else if (m_flow.tutorial == EOutgameTutorialTrigger.CollectionTabFirstEnter)
            {
                DataSaveManager.Data.Tutorial.DefeatEnhancePending = false;
                OutgameTutorialProgress.Save();
            }
            CancelMissionFlow(false);
            if (t_resumeFtue) OutgameTutorialRunner.ResumeAfterDefeatEnhance();
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
        if (_defer) m_resumeFlow = m_flow ?? m_resumeFlow;
        else m_resumeFlow = null;
        m_flowVersion++;
        m_flowCancellation?.Cancel();
        m_flowCancellation?.Dispose();
        m_flowCancellation = null;
        m_flow = null;
        m_flowPreparing = false;
        m_flowStarted = false;
        if (!_defer) ReleaseMissionInput();
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
