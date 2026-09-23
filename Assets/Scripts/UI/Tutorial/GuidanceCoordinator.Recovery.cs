using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed partial class GuidanceCoordinator
{
    int m_pauseVersion;
    public static bool CanCloseCardDetail => AllowsInput(EGuidanceInputAction.ReturnCardDetail);
    public static bool CanCloseAlbum => AllowsInput(EGuidanceInputAction.ReturnAlbum);

    /// <summary>현재 안내의 화면만 한 번 복구한다. 실패 처리는 호출자가 맡는다.</summary>
    public static async UniTask<bool> TryRestoreCurrentSurfaceAsync(CancellationToken _ct)
    {
        var t_owner = s_instance;
        if (t_owner == null || t_owner.m_flow == null || t_owner.m_flowPreparing) return false;
        int t_version = t_owner.m_flowVersion;
        t_owner.m_flowPreparing = true;
        try
        {
            await t_owner.RestoreFlowSurfaceAsync(t_owner.m_flow, _ct);
            t_owner.EnsureFlow(t_version, _ct);
            return true;
        }
        finally
        {
            if (t_owner != null && t_owner.m_flowVersion == t_version) t_owner.m_flowPreparing = false;
        }
    }

    internal static async UniTask<bool> TryRestoreForcedSurfaceAsync(TutorialStepDef _step, CancellationToken _ct)
    {
        if (_step == null) return false;
        if (UsesEnhancePanel(_step))
        {
            await PrepareGrowthSurfaceAsync(_step, _ct);
            return true;
        }
        EOutgameFeature t_tab;
        switch (_step.Anchor)
        {
            case EOutgameTutorialAnchor.LobbyPlayButton:
            case EOutgameTutorialAnchor.LobbyMatchTab:
                t_tab = EOutgameFeature.LobbyMatchTab; break;
            case EOutgameTutorialAnchor.PackBuyButton:
            case EOutgameTutorialAnchor.LobbyPackTab:
                t_tab = EOutgameFeature.LobbyPackTab; break;
            case EOutgameTutorialAnchor.DeckEditCollectionCard:
            case EOutgameTutorialAnchor.DeckEditSaveButton:
            case EOutgameTutorialAnchor.LobbyDeckTab:
                t_tab = EOutgameFeature.LobbyDeckTab; break;
            case EOutgameTutorialAnchor.AlbumThemeCell:
            case EOutgameTutorialAnchor.LobbyCollectionTab:
                t_tab = EOutgameFeature.LobbyCollectionTab; break;
            default: return false;
        }
        if (s_instance == null) throw new InvalidOperationException("안내 화면을 찾을 수 없습니다.");
        // 커서를 과거 탭 스텝으로 되감으면 설명·진입 효과까지 재실행된다.
        // 현재 스텝의 화면만 복원하고 완료된 단계는 다시 실행하지 않는다.
        await s_instance.SelectFlowTabAsync(t_tab, _ct);
        _ct.ThrowIfCancellationRequested();
        using (InternalNavigation())
        {
            if (t_tab == EOutgameFeature.LobbyDeckTab && DeckEditController.OpenEditor == null
                && s_instance.m_shell.CurrentPanel is DeckTabController t_deck)
                t_deck.OpenEditor(DeckSaveManager.SelectedSlot);
            if (_step.Anchor == EOutgameTutorialAnchor.AlbumThemeCell
                && !AlbumInsertSession.IsRunning && s_instance.m_shell.CurrentPanel is AlbumTabController t_album)
                t_album.PageOverlay?.Close();
        }
        return true;
    }

    internal static async UniTask PrepareFirstRankSurfaceAsync(CancellationToken token)
    {
        if (s_instance == null) throw new InvalidOperationException("랭크 안내 화면을 찾을 수 없습니다.");
        await s_instance.SelectFlowTabAsync(EOutgameFeature.LobbyMatchTab, token);
    }

    static bool UsesEnhancePanel(TutorialStepDef _step)
        => _step != null && (_step.Action == EOutgameTutorialAction.WaitEnhance
            || _step.Anchor == EOutgameTutorialAnchor.LobbyEnhanceButton
            || _step.Anchor == EOutgameTutorialAnchor.LobbyEnhanceShardIcon
            || _step.Anchor == EOutgameTutorialAnchor.LobbyEnhanceShardAmount
            || _step.Anchor == EOutgameTutorialAnchor.LobbyEnhanceShardControls
            || _step.Anchor == EOutgameTutorialAnchor.LobbyEnhanceCardView
            || _step.Anchor == EOutgameTutorialAnchor.LobbyEnhanceKeywordDescription
            || _step.Anchor == EOutgameTutorialAnchor.LobbyEnhanceSynergyDescription);

    // 일반 스텝 진입과 저장 위치 복구가 같은 강화 화면을 준비한다.
    internal static async UniTask PrepareGrowthSurfaceAsync(TutorialStepDef _step, CancellationToken _ct)
    {
        if (!UsesEnhancePanel(_step)) return;
        await OpenGrowthSurfaceAsync(_ct);
    }

    static async UniTask OpenGrowthSurfaceAsync(CancellationToken _ct)
    {
        while (LobbyEnhanceTabPanel.IsPresenting) await UniTask.Yield(_ct);
        _ct.ThrowIfCancellationRequested();
        if (s_instance == null) throw new InvalidOperationException("강화 안내 화면을 찾을 수 없습니다.");
        using (InternalNavigation())
        {
            CardDetailOverlayView.Close();
            AlbumPageOverlayView.CloseOpen();
        }
        await s_instance.SelectFlowTabAsync(EOutgameFeature.CardEnhance, _ct);
        int t_card = OutgameTutorialGuide.TargetCardId;
        // 이미 목표를 달성했으나 대상 카드가 없는 저장 기록도 강화 화면에서 설명을 이어간다.
        if (t_card <= 0) return;
        using (InternalNavigation())
            if (!LobbyEnhanceTabPanel.TryOpenForCard(t_card))
                throw new InvalidOperationException("안내할 카드를 강화 화면에 표시하지 못했습니다.");
    }

    async UniTask SelectFlowTabAsync(EOutgameFeature _feature, CancellationToken _ct)
    {
        _ct.ThrowIfCancellationRequested();
        if (m_shell == null) throw new InvalidOperationException("로비 화면을 찾을 수 없습니다.");
        bool t_arrived = false;
        using (InternalNavigation())
            if (!m_shell.TrySelectFeature(_feature, _onArrived: () => t_arrived = true))
                throw new InvalidOperationException("안내할 화면으로 이동하지 못했습니다.");
        int t_selection = m_shell.SelectionRequestVersion;
        float t_deadline = Time.realtimeSinceStartup + 5f;
        while (!t_arrived)
        {
            _ct.ThrowIfCancellationRequested();
            if (Time.realtimeSinceStartup >= t_deadline || m_shell.SelectionRequestVersion != t_selection)
                throw new InvalidOperationException("안내 화면 이동이 중단되었습니다.");
            await UniTask.Yield(_ct);
        }
    }

    async UniTask RestoreFlowSurfaceAsync(GuideMissionFlow _flow, CancellationToken _ct)
    {
        if (!OutgameTutorialRunner.TryGetGuidedChapter(_flow.tutorial, out _, out var t_chapter))
            throw new InvalidOperationException("안내 데이터를 찾을 수 없습니다.");
        int t_index = 0;
        int t_savedId = GuideResume.Record?.StepId ?? 0;
        for (int t_i = 0; t_i < t_chapter.StepCount; t_i++)
            if (t_chapter.TryGetStep(t_i, out var t_candidate) && t_candidate.StepId == t_savedId)
            { t_index = t_i; break; }
        if (!t_chapter.TryGetStep(t_index, out var t_step))
            throw new InvalidOperationException("안내 재개 위치를 찾을 수 없습니다.");

        bool t_growth = _flow.tutorial == EOutgameTutorialTrigger.CollectionTabFirstEnter
            || _flow.tutorial == EOutgameTutorialTrigger.SynergyGrowthIntroduction;
        if (t_growth)
        {
            int t_enhance = -1;
            for (int t_i = 0; t_i < t_chapter.StepCount; t_i++)
                if (t_chapter.TryGetStep(t_i, out var t_candidate)
                    && t_candidate.Action == EOutgameTutorialAction.WaitEnhance) { t_enhance = t_i; break; }
            if (OutgameTutorialGuide.IsGrowthGoalReached && t_enhance >= 0 && t_index <= t_enhance)
            {
                GuideResume.MarkGoalReached();
                t_index = t_enhance + 1;
                t_chapter.TryGetStep(t_index, out t_step);
            }
            if (t_step == null) throw new InvalidOperationException("성장 안내의 설명 단계가 없습니다.");
            await OpenGrowthSurfaceAsync(_ct);
        }
        else if (_flow.tutorial == EOutgameTutorialTrigger.AdventureUnlocked)
        {
            await SelectFlowTabAsync(EOutgameFeature.LobbyMatchTab, _ct);
            if (t_index > 0)
            {
                // 대치 화면은 상대 확정이 선행돼야 하므로 정점 선택 구간부터 다시 잇는다.
                for (int t_i = 0; t_i < t_chapter.StepCount; t_i++)
                    if (t_chapter.TryGetStep(t_i, out var t_candidate)
                        && t_candidate.Anchor == EOutgameTutorialAnchor.AdventureNode)
                    { t_step = t_candidate; break; }
                using (InternalNavigation())
                    if (m_launcher == null || !m_launcher.TryOpenAdventureMapAt(-1))
                        throw new InvalidOperationException("모험 지도를 열지 못했습니다.");
            }
        }
        else if (_flow.tutorial == EOutgameTutorialTrigger.SynergyBattleIntroduction)
        {
            await SelectFlowTabAsync(EOutgameFeature.LobbyDeckTab, _ct);
            if (t_index > 0 && t_index < t_chapter.StepCount - 1)
            {
                if (!(m_shell.CurrentPanel is DeckTabController t_deck))
                    throw new InvalidOperationException("덱 화면을 찾을 수 없습니다.");
                if (DeckEditController.OpenEditor == null)
                    using (InternalNavigation()) t_deck.OpenEditor(DeckSaveManager.SelectedSlot);
            }
        }
        else if (_flow.destination != EOutgameFeature.None)
            await SelectFlowTabAsync(_flow.destination, _ct);
        GuideResume.SetStep(t_step.StepId);
    }

    void OnApplicationPause(bool _paused)
    {
        if (_paused)
        {
            m_pauseVersion++;
            m_applicationPaused = true;
            CancelMatchMission();
            if (m_flow != null || m_retryRequested || m_flowInput != null) CancelMissionFlow(true, false);
            return;
        }
        if (!m_applicationPaused) return;
        ResumeAfterPauseAsync(++m_pauseVersion).Forget();
    }

    async UniTask ResumeAfterPauseAsync(int _version)
    {
        bool recovered = false;
        try
        {
            var t_ct = this.GetCancellationTokenOnDestroy();
            if (m_resumeFlow == null && !GuideResume.HasPending) return;
            HoldMissionInput();
            m_flowPreparing = true;
            ShowTransition();
            while (ServerSaveCommands.IsInFlight)
            {
                if (_version != m_pauseVersion) return;
                await UniTask.Yield(t_ct);
            }
            await OnboardingCommands.RecoverPendingAsync(t_ct);
            await OutgameTutorialGuide.RefreshFreeShotSpentAsync().AttachExternalCancellation(t_ct);
            if (_version != m_pauseVersion) return;
            using (InternalNavigation())
            {
                if (GuideResume.IsFor(EOutgameTutorialTrigger.CollectionTabFirstEnter)
                    || GuideResume.IsFor(EOutgameTutorialTrigger.SynergyGrowthIntroduction))
                {
                    CardDetailOverlayView.Close();
                    AlbumPageOverlayView.CloseOpen();
                }
                if (m_resumeFlow?.tutorial == EOutgameTutorialTrigger.AdventureUnlocked)
                    m_launcher?.CancelGuidedAdventureEntry();
            }
            recovered = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception t_exception)
        {
            if (_version == m_pauseVersion) DeferCurrentGuide(t_exception.Message);
        }
        finally
        {
            if (_version == m_pauseVersion)
            {
                m_applicationPaused = false;
                m_flowPreparing = false;
                ClearTransition();
                if (recovered || OutgameTutorialRunner.IsDefeatEnhanceInterlude)
                {
                    RequestCurrentMission();
                    AdvanceMissionFlow();
                }
            }
        }
    }
}
