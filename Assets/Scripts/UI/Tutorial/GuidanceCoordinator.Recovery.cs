using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed partial class GuidanceCoordinator
{
    int m_pauseVersion;
    public static bool CanCloseCardDetail => !IsInputLocked || IsInternalNavigation
        || (CanAcceptGuideReturn && OutgameTutorialGuide.TryGetCurrentStep(out var t_step)
            && (t_step.Completion == EOutgameTutorialCompletion.CardDetailReturn
                || t_step.Completion == EOutgameTutorialCompletion.LobbyReturn));
    public static bool CanCloseAlbum => !IsInputLocked || IsInternalNavigation
        || (CanAcceptGuideReturn && OutgameTutorialGuide.TryGetCurrentStep(out var t_step)
            && t_step.Completion == EOutgameTutorialCompletion.LobbyReturn);

    static bool CanAcceptGuideReturn => !IsRestoring
        && (OutgameTutorialGateUI.Instance == null || !OutgameTutorialGateUI.Instance.IsTransitionOnly);

    async UniTask SelectFlowTabAsync(EOutgameFeature _feature, CancellationToken _ct)
    {
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
            bool t_detail = t_index >= t_enhance && t_enhance >= 0
                && t_step.Action != EOutgameTutorialAction.CloseAlbumPage
                && !(t_step.Completion == EOutgameTutorialCompletion.Confirm
                    && t_step.Anchor == EOutgameTutorialAnchor.None
                    && t_index == t_chapter.StepCount - 1);
            bool t_page = t_detail || t_step.Anchor == EOutgameTutorialAnchor.AlbumCardSlot;
            if (_flow.tutorial != EOutgameTutorialTrigger.CollectionTabFirstEnter || t_index > 0)
                await SelectFlowTabAsync(EOutgameFeature.LobbyCollectionTab, _ct);
            int t_card = OutgameTutorialGuide.TargetCardId;
            if (t_page && t_card > 0)
            {
                if (!(m_shell.CurrentPanel is AlbumTabController t_album))
                    throw new InvalidOperationException("도감 화면을 준비하지 못했습니다.");
                bool t_found = false;
                foreach (var t_theme in CardAlbum.Themes)
                {
                    if (t_theme.IsLocked) continue;
                    for (int t_pageIndex = 0; t_pageIndex < t_theme.Pages.Count; t_pageIndex++)
                    {
                        if (!t_theme.Pages[t_pageIndex].CardIds.Contains(t_card)) continue;
                        using (InternalNavigation()) t_album.OpenThemePage(t_theme, t_pageIndex);
                        t_found = true;
                        break;
                    }
                    if (t_found) break;
                }
                if (!t_found) throw new InvalidOperationException("안내 대상 카드의 도감 페이지가 없습니다.");
                if (t_detail) using (InternalNavigation()) CardDetailOverlayView.Open(t_card);
            }
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
                using (InternalNavigation()) t_deck.OpenEditor(DeckSaveManager.SelectedSlot);
            }
        }
        else if (_flow.destination != EOutgameFeature.None)
            await SelectFlowTabAsync(_flow.destination, _ct);
        GuideResume.SetStep(t_step.StepId);
    }

    void OnFlowCurrencyChanged(ECurrencyType _currency, long _balance) => RequestFlowRetry();
    void RequestFlowRetry()
    {
        if (m_flowDeferred) m_retryRequested = true;
    }

    void SubscribeFlowRecovery()
    {
        CurrencyManager.OnCurrencyChanged += OnFlowCurrencyChanged;
        OwnershipManager.OnOwnershipChanged += RequestFlowRetry;
        CardGrowthManager.OnGrowthChanged += RequestFlowRetry;
    }

    void UnsubscribeFlowRecovery()
    {
        CurrencyManager.OnCurrencyChanged -= OnFlowCurrencyChanged;
        OwnershipManager.OnOwnershipChanged -= RequestFlowRetry;
        CardGrowthManager.OnGrowthChanged -= RequestFlowRetry;
    }

    void OnApplicationPause(bool _paused)
    {
        if (_paused)
        {
            m_pauseVersion++;
            m_applicationPaused = true;
            if (m_flow != null) CancelMissionFlow(true);
            return;
        }
        if (!m_applicationPaused) return;
        ResumeAfterPauseAsync(++m_pauseVersion).Forget();
    }

    async UniTask ResumeAfterPauseAsync(int _version)
    {
        try
        {
            var t_ct = this.GetCancellationTokenOnDestroy();
            if (!GuideResume.HasPending)
            {
                if (m_flowDeferred) RequestCurrentMission();
                return;
            }
            m_flowLocked = true;
            m_flowPreparing = true;
            ShowTransition();
            using (InternalNavigation())
            {
                CardDetailOverlayView.Close();
                AlbumPageOverlayView.CloseOpen();
            }
            float t_deadline = Time.realtimeSinceStartup + 5f;
            while (ServerSaveCommands.IsInFlight)
            {
                if (_version != m_pauseVersion) return;
                if (Time.realtimeSinceStartup >= t_deadline)
                    throw new InvalidOperationException("강화 결과를 확인하지 못했습니다. 연결 복구 후 다시 시도해 주세요.");
                await UniTask.Yield(t_ct);
            }
            using (var t_refresh = CancellationTokenSource.CreateLinkedTokenSource(t_ct))
            {
                using var t_timer = t_refresh.CancelAfterSlim(TimeSpan.FromSeconds(5));
                try { await OutgameTutorialGuide.RefreshFreeShotSpentAsync().AttachExternalCancellation(t_refresh.Token); }
                catch (OperationCanceledException) when (!t_ct.IsCancellationRequested)
                { throw new InvalidOperationException("강화 결과 확인이 지연되고 있습니다. 연결 복구 후 다시 시도해 주세요."); }
            }
            if (_version != m_pauseVersion) return;
            RequestCurrentMission();
        }
        catch (OperationCanceledException) { }
        catch (Exception t_exception) { if (_version == m_pauseVersion) ShowFlowFailure(t_exception.Message); }
        finally
        {
            if (_version == m_pauseVersion)
            {
                m_applicationPaused = false;
                m_flowPreparing = false;
                m_flowLocked = false;
                ClearTransition();
            }
        }
    }
}
