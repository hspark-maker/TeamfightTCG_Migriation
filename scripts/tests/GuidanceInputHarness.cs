using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// UI surfaces and frame scheduling are boundaries. Policy and restoration members are extracted each run.
namespace UnityEngine { public static class Debug { public static void LogError(string message) => throw new Exception(message); } }
class TutorialStepDef
{
    public int StepId;
    public EOutgameTutorialAction Action;
    public EOutgameTutorialAnchor Anchor;
    public bool UseDim;
    public EOutgameTutorialCompletion Completion => TutorialActionMeta.Of(Action).Completion;
}
static class OutgameTutorialRunner
{
    public static bool IsRunning;
    public static TutorialStepDef Step;
    public static bool TryGetCurrentStep(out TutorialStepDef step) { step = Step; return step != null; }
}
static class OutgameTutorialGuide
{
    public static bool TryGetCurrentStep(out TutorialStepDef step) => OutgameTutorialRunner.TryGetCurrentStep(out step);
}
static class OutgameFeatureLock { public static bool IsFtueFreeNavigation; }
static class OnboardingSession { public static bool IsBusy, IsActive; }
static class GuideMissionTrackerView { public static bool IsShowingNextMission; }
class OutgameTutorialGateUI { public static OutgameTutorialGateUI Instance; public bool IsTransitionOnly; }
static class ContentUnlockPresentation { public static bool IsPlaying, IsReady; }
static class ContentUnlockIntroView { public static bool IsOpen; }
static class UnlockIntroOverlay { public static bool IsOpen; }
static class RankPromoteOverlay { public static bool IsOpen; }
static class LobbyRankEffectDirector { public static bool Playing; }
static class Time { public static float realtimeSinceStartup; }
static class TestLoop
{
    static TaskCompletionSource<bool> pending;
    public static Task Yield(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (pending != null) throw new Exception("Unexpected concurrent frame wait");
        pending = new TaskCompletionSource<bool>();
        return pending.Task;
    }
    public static void Tick()
    {
        var next = pending;
        pending = null;
        if (next == null) throw new Exception("No frame awaited");
        next.SetResult(true);
    }
}
static class DeckEditController { public static object OpenEditor; }
static class DeckSaveManager { public static int SelectedSlot = 2; }
static class AlbumInsertSession { public static bool IsRunning; }
class DeckTabController
{
    public int OpenCount, Slot;
    public void OpenEditor(int slot)
    {
        if (!GuidanceCoordinator.IsInternalNavigation) throw new Exception("Editor opened outside internal scope");
        Slot = slot; OpenCount++; DeckEditController.OpenEditor = this;
    }
}
class PageOverlay
{
    public int CloseCount;
    public void Close()
    {
        if (!GuidanceCoordinator.IsInternalNavigation) throw new Exception("Album closed outside internal scope");
        CloseCount++;
    }
}
class AlbumTabController { public PageOverlay PageOverlay = new PageOverlay(); }
struct CardDetailOpenOptions { public bool ReadOnly { get; set; } public bool LiftAboveAll { get; set; } public bool CoverFullScreen { get; set; } }
class CardDetailOverlayView
{
    public static int Opens, Resolves;
    bool m_readOnly;
    static CardDetailOverlayView Resolve() { Resolves++; return new CardDetailOverlayView(); }
    void InitializeUI() { }
    void LiftAbove(bool above) { }
    void SetFullScreen(bool full) { }
    void Show(IReadOnlyList<int> cards, int index) { Opens++; }
    /* PRODUCTION_DETAIL_OPEN */
}
class Shell
{
    public object CurrentPanel;
    public int SelectionRequestVersion, Selections;
    public bool Reject, AutoArrive;
    public EOutgameFeature Destination;
    Action arrived;
    public bool TrySelectFeature(EOutgameFeature feature, Action _onArrived)
    {
        if (!GuidanceCoordinator.IsInternalNavigation) throw new Exception("Recovery tab selection is not internal");
        if (Reject) return false;
        Selections++; SelectionRequestVersion++; Destination = feature;
        CurrentPanel = feature == EOutgameFeature.LobbyDeckTab ? new DeckTabController()
            : feature == EOutgameFeature.LobbyCollectionTab ? new AlbumTabController() : new object();
        arrived = _onArrived;
        if (AutoArrive) Arrive();
        return true;
    }
    public void Arrive() { var callback = arrived; arrived = null; callback?.Invoke(); }
}
class GuidanceCoordinator
{
    internal static GuidanceCoordinator s_instance;
    internal bool m_flowLocked, m_flowPreparing;
    int m_internalNavigation;
    internal Shell m_shell = new Shell();
    /* PRODUCTION_MEMBERS */
}
static class Program
{
    static int checks;
    static readonly TutorialStepDef[] authored = { /* AUTHORED_FTUE */ };
    static readonly EOutgameTutorialAnchor[] tabs = { EOutgameTutorialAnchor.LobbyMatchTab,
        EOutgameTutorialAnchor.LobbyDeckTab, EOutgameTutorialAnchor.LobbyPackTab, EOutgameTutorialAnchor.LobbyCollectionTab };
    static void Check(bool ok, string description) { checks++; if (!ok) throw new Exception(description); }
    static GuidanceCoordinator Reset()
    {
        OutgameTutorialRunner.IsRunning = false; OutgameTutorialRunner.Step = null;
        OutgameFeatureLock.IsFtueFreeNavigation = false;
        OnboardingSession.IsBusy = false; OnboardingSession.IsActive = false;
        GuideMissionTrackerView.IsShowingNextMission = false;
        OutgameTutorialGateUI.Instance = null;
        ContentUnlockPresentation.IsPlaying = false; ContentUnlockPresentation.IsReady = false;
        ContentUnlockIntroView.IsOpen = false; UnlockIntroOverlay.IsOpen = false;
        RankPromoteOverlay.IsOpen = false; LobbyRankEffectDirector.Playing = false;
        DeckEditController.OpenEditor = null; AlbumInsertSession.IsRunning = false;
        Time.realtimeSinceStartup = 0;
        CardDetailOverlayView.Opens = 0; CardDetailOverlayView.Resolves = 0;
        return GuidanceCoordinator.s_instance = new GuidanceCoordinator();
    }
    static void InputMatrix()
    {
        foreach (var step in authored)
        {
            Reset(); OutgameTutorialRunner.IsRunning = true; OutgameTutorialRunner.Step = step;
            Check(GuidanceCoordinator.IsInputLocked, $"step {step.StepId} unlocked forced input (dim={step.UseDim})");
            foreach (EOutgameTutorialAnchor anchor in Enum.GetValues(typeof(EOutgameTutorialAnchor)))
                Check(GuidanceCoordinator.AllowsUserAction(anchor) == (anchor != EOutgameTutorialAnchor.None && anchor == step.Anchor),
                    $"step {step.StepId}: wrong action permission for {anchor}");
            foreach (var anchor in tabs)
                Check(GuidanceCoordinator.AllowsUserNavigation(anchor) == (step.Completion == EOutgameTutorialCompletion.Click && anchor == step.Anchor),
                    $"step {step.StepId}: wrong tab permission for {anchor}");
            Check(!GuidanceCoordinator.AllowsUserAction(EOutgameTutorialAnchor.None), "Settings bypasses forced step");
            Check(GuidanceCoordinator.CanCloseCardDetail == (step.Completion == EOutgameTutorialCompletion.CardDetailReturn || step.Completion == EOutgameTutorialCompletion.LobbyReturn), "Wrong detail close policy");
            Check(GuidanceCoordinator.CanCloseAlbum == (step.Completion == EOutgameTutorialCompletion.LobbyReturn), "Wrong album close policy");
        }
        var owner = Reset();
        OutgameTutorialRunner.IsRunning = true; OutgameFeatureLock.IsFtueFreeNavigation = true;
        Check(!GuidanceCoordinator.IsInputLocked && GuidanceCoordinator.AllowsUserAction(EOutgameTutorialAnchor.None), "Free FTUE blocks settings");
        foreach (var tab in tabs) Check(GuidanceCoordinator.AllowsUserNavigation(tab), "Free FTUE blocks tab");
        Check(GuidanceCoordinator.CanCloseAlbum && GuidanceCoordinator.CanCloseCardDetail, "Free FTUE blocks closing");
        owner.m_flowLocked = true;
        OutgameTutorialRunner.Step = new TutorialStepDef { Anchor = EOutgameTutorialAnchor.CardDetailEnhanceButton };
        Check(GuidanceCoordinator.IsInputLocked && !GuidanceCoordinator.AllowsUserAction(EOutgameTutorialAnchor.None), "Guided flow not locked");
        Check(GuidanceCoordinator.AllowsUserAction(EOutgameTutorialAnchor.CardDetailEnhanceButton), "Guided target blocked");
        owner.m_flowPreparing = true;
        Check(!GuidanceCoordinator.AllowsUserAction(EOutgameTutorialAnchor.CardDetailEnhanceButton), "Preparing accepts input");
        owner.m_flowPreparing = false;
        OutgameTutorialGateUI.Instance = new OutgameTutorialGateUI { IsTransitionOnly = true };
        Check(!GuidanceCoordinator.AllowsUserAction(EOutgameTutorialAnchor.CardDetailEnhanceButton), "Transition accepts target input");
        using (GuidanceCoordinator.InternalNavigation())
        {
            Check(GuidanceCoordinator.AllowsUserAction(EOutgameTutorialAnchor.None), "Internal action blocked");
            Check(GuidanceCoordinator.CanCloseAlbum && GuidanceCoordinator.CanCloseCardDetail, "Internal close blocked");
            foreach (var tab in tabs) Check(GuidanceCoordinator.AllowsUserNavigation(tab), "Internal navigation blocked");
        }
        Check(!GuidanceCoordinator.IsInternalNavigation, "Internal scope leaked");
        foreach (var action in new[] { EOutgameTutorialAction.WaitLobbyReturn, EOutgameTutorialAction.WaitCardDetailReturn })
        {
            owner = Reset(); OutgameTutorialRunner.IsRunning = true;
            OutgameTutorialRunner.Step = new TutorialStepDef { Action = action };
            Check(GuidanceCoordinator.CanCloseCardDetail, "Requested detail return blocked");
            Check(GuidanceCoordinator.CanCloseAlbum == (action == EOutgameTutorialAction.WaitLobbyReturn), "Wrong requested album return");
            OnboardingSession.IsBusy = true;
            Check(!GuidanceCoordinator.CanCloseAlbum && !GuidanceCoordinator.CanCloseCardDetail, "Busy return permits close");
            OnboardingSession.IsBusy = false; owner.m_flowPreparing = true;
            Check(!GuidanceCoordinator.CanCloseAlbum && !GuidanceCoordinator.CanCloseCardDetail, "Restore permits user close");
        }
    }
    static void PresentationMatrix()
    {
        Action[] start = { () => ContentUnlockPresentation.IsPlaying = true,
            () => { ContentUnlockPresentation.IsReady = true; OnboardingSession.IsActive = true;
                OutgameTutorialRunner.Step = new TutorialStepDef { Action = EOutgameTutorialAction.ContentUnlockIntro }; },
            () => ContentUnlockIntroView.IsOpen = true, () => UnlockIntroOverlay.IsOpen = true,
            () => RankPromoteOverlay.IsOpen = true, () => LobbyRankEffectDirector.Playing = true,
            () => GuideMissionTrackerView.IsShowingNextMission = true };
        foreach (var begin in start)
        {
            Reset(); begin();
            Check(GuidanceCoordinator.IsLobbyPresentationBlockingNavigation, "Presentation lock missing");
            foreach (var tab in tabs) Check(!GuidanceCoordinator.AllowsUserNavigation(tab), "Presentation permits tab");
            using (GuidanceCoordinator.InternalNavigation())
                foreach (var tab in tabs) Check(GuidanceCoordinator.AllowsUserNavigation(tab), "Presentation blocks internal recovery");
            Reset();
            Check(!GuidanceCoordinator.IsLobbyPresentationBlockingNavigation, "Presentation lock stale");
            foreach (var tab in tabs) Check(GuidanceCoordinator.AllowsUserNavigation(tab), "Presentation leaves navigation locked");
        }
        Reset(); OnboardingSession.IsBusy = true;
        foreach (var tab in tabs) Check(!GuidanceCoordinator.AllowsUserNavigation(tab), "Busy command permits navigation");
    }
    static void DetailEntryAndHandoff()
    {
        Reset(); OutgameTutorialRunner.IsRunning = true;
        OutgameTutorialRunner.Step = new TutorialStepDef { Action = EOutgameTutorialAction.WaitClick, Anchor = EOutgameTutorialAnchor.PackAcquireButton };
        CardDetailOverlayView.Open(new[] { 17 }, 0);
        Check(CardDetailOverlayView.Opens == 0 && CardDetailOverlayView.Resolves == 0, "Pack acquire opens a detail that cannot be closed");
        OutgameTutorialRunner.Step = new TutorialStepDef { Action = EOutgameTutorialAction.WaitClick, Anchor = EOutgameTutorialAnchor.AlbumCardSlot };
        CardDetailOverlayView.Open(new[] { 17 }, 0);
        Check(CardDetailOverlayView.Opens == 1, "Guide target card detail rejected");
        OutgameTutorialRunner.Step = new TutorialStepDef { Action = EOutgameTutorialAction.WaitClick, Anchor = EOutgameTutorialAnchor.PackAcquireButton };
        using (GuidanceCoordinator.InternalNavigation()) CardDetailOverlayView.Open(new[] { 17 }, 0);
        Check(CardDetailOverlayView.Opens == 2, "Internal detail restoration rejected");
        Reset(); CardDetailOverlayView.Open(new[] { 17 }, 0);
        Check(CardDetailOverlayView.Opens == 1, "Normal detail entry rejected");
        CardDetailOverlayView.Open(null, 0); CardDetailOverlayView.Open(Array.Empty<int>(), 0);
        Check(CardDetailOverlayView.Resolves == 1, "Empty detail entry instantiates UI");
        Reset(); OutgameTutorialRunner.IsRunning = true;
        Check(!GuidanceCoordinator.IsInputLocked && GuidanceCoordinator.CanCloseCardDetail, "Final handoff without current step locks deck/cancel UI");
        foreach (var tab in tabs) Check(!GuidanceCoordinator.AllowsUserNavigation(tab), "Final handoff allows unrelated tab");
    }
    static async Task ExpectFailure(Task<bool> task, bool cancellation)
    {
        try { await task; throw new Exception("Restoration unexpectedly succeeded"); }
        catch (OperationCanceledException) when (cancellation) { checks++; }
        catch (InvalidOperationException) when (!cancellation) { checks++; }
    }
    static async Task RecoveryMatrix()
    {
        var anchors = new[] { EOutgameTutorialAnchor.LobbyPlayButton, EOutgameTutorialAnchor.LobbyMatchTab,
            EOutgameTutorialAnchor.PackBuyButton, EOutgameTutorialAnchor.LobbyPackTab,
            EOutgameTutorialAnchor.DeckEditCollectionCard, EOutgameTutorialAnchor.DeckEditSaveButton,
            EOutgameTutorialAnchor.LobbyDeckTab, EOutgameTutorialAnchor.AlbumThemeCell, EOutgameTutorialAnchor.LobbyCollectionTab };
        var destinations = new[] { EOutgameFeature.LobbyMatchTab, EOutgameFeature.LobbyMatchTab,
            EOutgameFeature.LobbyPackTab, EOutgameFeature.LobbyPackTab, EOutgameFeature.LobbyDeckTab,
            EOutgameFeature.LobbyDeckTab, EOutgameFeature.LobbyDeckTab, EOutgameFeature.LobbyCollectionTab,
            EOutgameFeature.LobbyCollectionTab };
        for (int i = 0; i < anchors.Length; i++)
        {
            var owner = Reset();
            var task = GuidanceCoordinator.TryRestoreForcedSurfaceAsync(new TutorialStepDef { Anchor = anchors[i] }, default);
            Check(!task.IsCompleted && owner.m_shell.Selections == 1 && owner.m_shell.Destination == destinations[i], "Recovery destination or arrival wait incorrect");
            Check(DeckEditController.OpenEditor == null, "Deck opens before arrival");
            owner.m_shell.Arrive(); TestLoop.Tick();
            Check(await task, "Recovery returned false");
            if (destinations[i] == EOutgameFeature.LobbyDeckTab)
            {
                var deck = (DeckTabController)owner.m_shell.CurrentPanel;
                Check(deck.OpenCount == 1 && deck.Slot == DeckSaveManager.SelectedSlot, "Deck editor not restored to selected slot");
            }
            if (destinations[i] == EOutgameFeature.LobbyCollectionTab)
                Check(((AlbumTabController)owner.m_shell.CurrentPanel).PageOverlay.CloseCount == (anchors[i] == EOutgameTutorialAnchor.AlbumThemeCell ? 1 : 0), "Album page restoration wrong");
            Check(!GuidanceCoordinator.IsInternalNavigation, "Recovery internal scope leaked");
        }
        foreach (EOutgameTutorialAnchor anchor in Enum.GetValues(typeof(EOutgameTutorialAnchor)))
        {
            if (Array.IndexOf(anchors, anchor) >= 0) continue;
            var owner = Reset();
            Check(!await GuidanceCoordinator.TryRestoreForcedSurfaceAsync(new TutorialStepDef { Anchor = anchor }, default)
                && owner.m_shell.Selections == 0, "Unsupported anchor reroutes tutorial");
        }
        var current = Reset(); current.m_shell.AutoArrive = true;
        DeckEditController.OpenEditor = new object();
        Check(await GuidanceCoordinator.TryRestoreForcedSurfaceAsync(new TutorialStepDef { Anchor = EOutgameTutorialAnchor.DeckEditSaveButton }, default), "Open editor recovery failed");
        Check(((DeckTabController)current.m_shell.CurrentPanel).OpenCount == 0, "Existing deck editor replaced");
        current = Reset(); current.m_shell.AutoArrive = true; AlbumInsertSession.IsRunning = true;
        await GuidanceCoordinator.TryRestoreForcedSurfaceAsync(new TutorialStepDef { Anchor = EOutgameTutorialAnchor.AlbumThemeCell }, default);
        Check(((AlbumTabController)current.m_shell.CurrentPanel).PageOverlay.CloseCount == 0, "Active album insert closed");
        var step = new TutorialStepDef { Anchor = EOutgameTutorialAnchor.LobbyPlayButton };
        current = Reset();
        await ExpectFailure(GuidanceCoordinator.TryRestoreForcedSurfaceAsync(step, new CancellationToken(true)), true);
        Check(current.m_shell.Selections == 0, "Pre-canceled recovery moves tab");
        current = Reset();
        using (var cancel = new CancellationTokenSource())
        {
            var task = GuidanceCoordinator.TryRestoreForcedSurfaceAsync(step, cancel.Token);
            cancel.Cancel(); TestLoop.Tick(); await ExpectFailure(task, true);
            Check(!GuidanceCoordinator.IsInternalNavigation, "Canceled recovery leaks scope");
        }
        current = Reset();
        var timed = GuidanceCoordinator.TryRestoreForcedSurfaceAsync(step, default);
        Time.realtimeSinceStartup = 6; TestLoop.Tick(); await ExpectFailure(timed, false);
        current = Reset();
        var superseded = GuidanceCoordinator.TryRestoreForcedSurfaceAsync(step, default);
        current.m_shell.SelectionRequestVersion++; TestLoop.Tick(); await ExpectFailure(superseded, false);
        current = Reset(); current.m_shell.Reject = true;
        await ExpectFailure(GuidanceCoordinator.TryRestoreForcedSurfaceAsync(step, default), false);
        current = Reset(); current.m_shell = null;
        await ExpectFailure(GuidanceCoordinator.TryRestoreForcedSurfaceAsync(step, default), false);
        Reset(); GuidanceCoordinator.s_instance = null;
        await ExpectFailure(GuidanceCoordinator.TryRestoreForcedSurfaceAsync(step, default), false);
        Check(!await GuidanceCoordinator.TryRestoreForcedSurfaceAsync(null, default), "Null step recovery succeeds");
    }
    public static async Task Main()
    {
        InputMatrix(); PresentationMatrix(); DetailEntryAndHandoff(); await RecoveryMatrix();
        Console.WriteLine($"PASS {checks} assertions: {authored.Length} authored FTUE steps x all anchors, settings/close/detail-entry policy, final handoff, free/internal navigation, seven presentation locks, surface restoration, arrival, cancellation, timeout, deck and album recovery.");
    }
}
