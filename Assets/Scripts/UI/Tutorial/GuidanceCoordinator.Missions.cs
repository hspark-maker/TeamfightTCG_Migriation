using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed partial class GuidanceCoordinator
{
    GuideMissionFlow m_flow;
    bool m_flowStarted;
    bool m_flowPreparing;
    bool m_flowLocked;
    bool m_flowDeferred;
    bool m_retryRequested;
    bool m_applicationPaused;
    int m_flowVersion;
    int m_flowSession;
    int m_internalNavigation;
    string m_lastFlowFailure;
    CancellationTokenSource m_flowCancellation;

    public static bool IsInputLocked => s_instance != null && s_instance.m_flowLocked;
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
        s_instance.m_retryRequested = true;
        s_instance.m_lastFlowFailure = null;
        foreach (var t_flow in GuideMissionFlows.All)
            if (t_flow != null && (GuideMissionFlows.IsEligible(t_flow) || GuideResume.IsFor(t_flow.tutorial)))
                OutgameTutorialRunner.ResumeDeferred(t_flow.tutorial);
    }

    public static bool TryRequestMission(string _missionId)
    {
        if (s_instance == null || !GuideMissionProgress.IsCurrent(_missionId)) return false;
        if (IsInputLocked) return true;
        if (GuideResume.HasPending) { RequestCurrentMission(); return true; }
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
        if (GuideResume.HasPending)
        {
            foreach (var t_flow in GuideMissionFlows.All)
                if (t_flow != null && GuideResume.IsFor(t_flow.tutorial)) return t_flow;
            GuideResume.Clear();
        }
        foreach (var t_flow in GuideMissionFlows.All)
        {
            if (t_flow == null || !GuideMissionFlows.IsEligible(t_flow)) continue;
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
            if (t_flow.tutorial != EOutgameTutorialTrigger.None) GuideResume.Begin(t_flow);
            if (!await GuideResume.SaveConfirmedAsync(_ct)) throw new InvalidOperationException("안내 진행을 저장하지 못했습니다.");
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
                }
            }
            if (GuideResume.IsFor(t_flow.tutorial)) GuideResume.Record.IntroductionSeen = true;
            DataSaveManager.Save();
            if (!await GuideResume.SaveConfirmedAsync(_ct)) throw new InvalidOperationException("해금 안내 결과를 저장하지 못했습니다.");
            EnsureFlow(_version, _ct);
            if (t_flow.tutorial == EOutgameTutorialTrigger.None) { CancelMissionFlow(false); return; }
            if (!PrepareFlowTarget(t_flow))
                throw new InvalidOperationException("강화 가능한 카드나 샤드가 부족합니다. 준비되면 안내를 이어갑니다.");
            if (t_flow.tutorial == EOutgameTutorialTrigger.CollectionTabFirstEnter
                || t_flow.tutorial == EOutgameTutorialTrigger.SynergyGrowthIntroduction)
                GuideResume.SetTarget(OutgameTutorialGuide.TargetCardId, OutgameTutorialGuide.TargetLevel);
            await RestoreFlowSurfaceAsync(t_flow, _ct);
            EnsureFlow(_version, _ct);
            if (!await GuideResume.SaveConfirmedAsync(_ct)) throw new InvalidOperationException("안내 재개 위치를 저장하지 못했습니다.");
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
        return OutgameTutorialRunner.TryGetGuidedChapter(_flow.tutorial, out _, out var t_chapter)
            && OutgameTutorialGuide.PrepareEnhanceCard(t_chapter);
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

    void CancelMissionFlow(bool _defer)
    {
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
        using (InternalNavigation())
        {
            OutgameTutorialRunner.AbortGuided();
            ContentUnlockPresentation.CancelCurrent();
            if (t_ownedAdventure && _defer) m_launcher?.CancelGuidedAdventureEntry();
            if (t_ownedGrowth && _defer)
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
            noText = "나중에",
        });
    }
}
