using System;
using System.Threading.Tasks;

// Only UI boundaries are stubbed. SuspendFreeBattleHint and Update are extracted
// from the production bridge by test-tutorial-free-navigation.ps1.
public sealed class OutgameTutorialBridge
{
    TutorialStepDef m_step;
    bool m_freeBattleHintSuspended, m_returnPreparing, m_completing, m_restoringSurface;
    bool m_enhancing, m_awaitingUnlockFx, m_waitingEnhanceRequest;
    float m_anchorMissingSince = -1f, m_synergyDeckOpenDeadline;
    int m_anchorRestoreStepId;
    object gatePrefab;
    public bool GuidedCursor, SuppressGuideUI;
    public int Hidden, Presentations, Restores, Completions, Closed;
    public TutorialStepDef Cursor;
    public OutgameTutorialBridge(TutorialStepDef step) { m_step = Cursor = step; }
    public void Tick(float seconds) { Time.unscaledTime = seconds; Update(); }
    public bool Suspended => m_freeBattleHintSuspended;
    public bool HasStep => m_step != null;
    bool TryGetCursorStep(out TutorialStepDef step) { step = Cursor; return step != null; }
    void HideGuide() { Hidden++; OutgameTutorialGateUI.Instance.IsTransitionOnly = false; }
    void PresentStep() { Presentations++; }
    void CloseGate() { Closed++; m_step = null; }
    void OnGateSatisfied() { Completions++; }
    void TryOpenGate() { OutgameTutorialGateUI.Instance.IsTransitionOnly = false; }
    Task RestoreMissingSurfaceAsync(TutorialStepDef step) { Restores++; return Task.CompletedTask; }
    void TryPresentContentIntro() { }
    void OnUnlockIntroCancelled() { }
    /* PRODUCTION_BRIDGE_METHODS */
}

public static class Harness
{
    static int checks;
    static void Require(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }
    static OutgameTutorialBridge Fresh()
    {
        GuidanceCoordinator.CurrentTab = EOutgameTutorialAnchor.LobbyMatchTab;
        GuidanceCoordinator.IsRestoring = false;
        GuidanceCoordinator.IsLobbyPresentationBlockingNavigation = false;
        OutgameFeatureLock.IsFtueFreeNavigation = true;
        DeckEditController.OpenEditor = null;
        CardDetailOverlayView.IsOpen = PackOpenOverlay.IsOpen = false;
        TutorialAnchorRegistry.Available = true;
        OutgameTutorialGateUI.Instance = new OutgameTutorialGateUI();
        return new OutgameTutorialBridge(new TutorialStepDef {
            StepId = 25, Action = EOutgameTutorialAction.BattleEntry,
            Anchor = EOutgameTutorialAnchor.LobbyPlayButton,
            Completion = EOutgameTutorialCompletion.Click
        });
    }
    static void WaitAway(OutgameTutorialBridge bridge)
    {
        var cursor = bridge.Cursor;
        TutorialAnchorRegistry.Available = false;
        for (int i = 0; i <= 12; i++) bridge.Tick(i);
        Require(OutgameTutorialGateUI.Instance.Transitions == 0 && bridge.Restores == 0,
            "Free navigation installed a blocking gate or forced a surface restore after five seconds.");
        Require(bridge.Suspended && bridge.Hidden > 0, "Free battle hint was not hidden away from match.");
        Require(ReferenceEquals(bridge.Cursor, cursor) && bridge.HasStep && bridge.Completions == 0,
            "Free navigation changed the tutorial cursor or discarded its active step.");
    }
    public static void Main()
    {
        foreach (var tab in new[] { EOutgameTutorialAnchor.LobbyDeckTab,
            EOutgameTutorialAnchor.LobbyCollectionTab, EOutgameTutorialAnchor.LobbyPackTab,
            EOutgameTutorialAnchor.None }) // None models a tab slide before arrival.
        {
            var bridge = Fresh();
            bridge.Tick(0);
            GuidanceCoordinator.CurrentTab = tab;
            WaitAway(bridge);
            GuidanceCoordinator.CurrentTab = EOutgameTutorialAnchor.LobbyMatchTab;
            TutorialAnchorRegistry.Available = true;
            bridge.Tick(13); bridge.Tick(14);
            Require(!bridge.Suspended && bridge.Presentations == 1,
                "Returning to match did not restore the same hint exactly once.");
            Require(bridge.Completions == 0 && bridge.Restores == 0,
                "Returning to match completed the battle step or invoked recovery.");
        }
        for (int overlay = 0; overlay < 3; overlay++)
        {
            var bridge = Fresh();
            if (overlay == 0) DeckEditController.OpenEditor = new object();
            if (overlay == 1) CardDetailOverlayView.IsOpen = true;
            if (overlay == 2) PackOpenOverlay.IsOpen = true;
            WaitAway(bridge);
            DeckEditController.OpenEditor = null;
            CardDetailOverlayView.IsOpen = PackOpenOverlay.IsOpen = false;
            TutorialAnchorRegistry.Available = true;
            bridge.Tick(13); bridge.Tick(14);
            Require(bridge.Presentations == 1, "Closing a foreign surface did not restore the battle hint once.");
        }
        // A real missing target on the match surface must retain the original watchdog.
        var current = Fresh();
        TutorialAnchorRegistry.Available = false;
        current.Tick(0); current.Tick(6);
        Require(!current.Suspended && OutgameTutorialGateUI.Instance.Transitions == 1 && current.Restores == 1,
            "The free-navigation exception disabled real missing-target recovery on match.");

        current = Fresh();
        OutgameFeatureLock.IsFtueFreeNavigation = false;
        GuidanceCoordinator.CurrentTab = EOutgameTutorialAnchor.LobbyDeckTab;
        TutorialAnchorRegistry.Available = false;
        current.Tick(0); current.Tick(6);
        Require(!current.Suspended && OutgameTutorialGateUI.Instance.Transitions == 1 && current.Restores == 1,
            "Forced navigation no longer blocks and restores a missing target.");

        current = Fresh();
        current.GuidedCursor = true;
        GuidanceCoordinator.CurrentTab = EOutgameTutorialAnchor.LobbyDeckTab;
        TutorialAnchorRegistry.Available = false;
        current.Tick(0); current.Tick(6);
        Require(!current.Suspended && current.Restores == 1,
            "Free FTUE navigation incorrectly suspended a guided mission.");

        current = Fresh();
        current.Cursor.Action = EOutgameTutorialAction.Other;
        GuidanceCoordinator.CurrentTab = EOutgameTutorialAnchor.LobbyDeckTab;
        TutorialAnchorRegistry.Available = false;
        current.Tick(0); current.Tick(6);
        Require(!current.Suspended && current.Restores == 1,
            "The exception escaped BattleEntry and disabled another tutorial action.");

        current = Fresh();
        GuidanceCoordinator.CurrentTab = EOutgameTutorialAnchor.LobbyDeckTab;
        WaitAway(current);
        current.Cursor = new TutorialStepDef();
        current.Tick(13);
        Require(!current.HasStep && current.Closed == 1,
            "A suspended hint ignored a changed tutorial cursor.");
        Console.WriteLine($"PASS: tutorial free navigation ({checks} assertions; production suspension and watchdog methods).");
    }
}

public enum EOutgameTutorialAction { BattleEntry, Other }
public enum EOutgameTutorialAnchor { None, LobbyPlayButton, LobbyMatchTab, LobbyDeckTab,
    LobbyCollectionTab, LobbyPackTab, CardDetailKeywordDescription, CardDetailCardView }
public enum EOutgameTutorialCompletion { Click, SynergyDeckEditor, SynergyDeck, Enhance, Confirm, ContentUnlockIntro }
public sealed class TutorialStepDef {
    public int StepId;
    public EOutgameTutorialAction Action;
    public EOutgameTutorialAnchor Anchor;
    public EOutgameTutorialCompletion Completion;
}
public static class Time { public static float unscaledTime; }
public static class OutgameFeatureLock { public static bool IsFtueFreeNavigation; }
public static class GuidanceCoordinator {
    public static EOutgameTutorialAnchor CurrentTab;
    public static bool IsRestoring, IsLobbyPresentationBlockingNavigation;
    public static bool IsCurrentTabAnchor(EOutgameTutorialAnchor anchor) =>
        anchor != EOutgameTutorialAnchor.None && anchor == CurrentTab;
    public static void DeferCurrentGuide(string message) { }
}
public static class LoadingCoverView { public static bool OwnsLobbyPreparation; }
public static class DeckEditController { public static object OpenEditor; }
public static class CardDetailOverlayView { public static bool IsOpen, IsRitualPlaying, IsUnlockFxPlaying; }
public static class PackOpenOverlay { public static bool IsOpen; }
public static class UnlockIntroOverlay { public static bool IsOpen; }
public static class SynergyBattleGuide { public static bool IsEditorOpen, IsDeckReady; }
public static class OutgameTutorialGuide {
    public static int TargetCardId;
    public static bool IsEnhanceIntroduction;
    public static bool CanContinueEnhance() => true;
}
public sealed class ResumeRecord { public bool GoalReached; }
public static class GuideResume { public static ResumeRecord Record; }
public sealed class Rect { public GameObject gameObject = new GameObject(); }
public sealed class GameObject { public bool activeInHierarchy = true; }
public sealed class Button { public bool IsInteractable() => true; }
public static class TutorialAnchorRegistry {
    public static bool Available;
    public static bool TryGet(EOutgameTutorialAnchor anchor, out Rect rect, out Button button)
    { rect = Available ? new Rect() : null; button = null; return Available; }
}
public sealed class OutgameTutorialGateUI {
    public static OutgameTutorialGateUI Instance;
    public bool IsTransitionOnly;
    public int Transitions;
    public static OutgameTutorialGateUI Ensure(object prefab) => Instance;
    public void ShowTransitionGate(object owner) { Transitions++; IsTransitionOnly = true; }
}
public static class AsyncExtensions { public static void Forget(this Task task) => task.GetAwaiter().GetResult(); }
