using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class OutgameTutorialBridge
{
    TutorialStepDef m_step, m_satisfiedStep;
    CancellationToken m_stepToken;
    int m_sessionVersion;
    bool m_completing, m_applying, m_pendingApply, m_returnPreparing;
    float m_synergyDeckOpenDeadline;
    readonly object m_entryWait = new object(), m_completionWait = new object();
    public int Entries, Presentations, Failures, Advances;
    public bool SignalDuringEntry;
    bool CursorRunning => Harness.Current != null;
    bool TryGetCursorStep(out TutorialStepDef step) { step = Harness.Current; return step != null; }
    CancellationToken GetCancellationTokenOnDestroy() => CancellationToken.None;
    Task<EOutgameTutorialStepResult> EnterCursorStepAsync(CancellationToken token)
    {
        Entries++;
        if (SignalDuringEntry) { SignalDuringEntry = false; ApplyCurrentStep(); }
        return Task.FromResult(EOutgameTutorialStepResult.Gated);
    }
    void PresentStep() { Presentations++; }
    void CloseGate() { m_step = null; }
    void ShowStepFailure(Exception error)
    {
        Failures++;
        OnboardingSession.SetPhase(EOnboardingPhase.Failed);
        CloseGate();
    }
    void SatisfyCursorStep() { Advances++; Harness.Current = Harness.Next; Harness.Next = null; }
    public void Apply() => ApplyCurrentStep();
    public void Satisfy() => OnGateSatisfied();
    /* PRODUCTION_BRIDGE_METHODS */
}

public static class Harness
{
    public static TutorialStepDef Current, Next;
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static OutgameTutorialBridge Fresh()
    {
        OnboardingSession.Suspend();
        DataSaveManager.Data = new UserSaveData();
        Current = new TutorialStepDef { StepId = 101 };
        Next = new TutorialStepDef { StepId = 102 };
        GuideResume.SaveCalls = 0;
        GuideResume.FailNextSave = false;
        GuideResume.FailAtSave = 0;
        GuideResume.PendingSave = null;
        return new OutgameTutorialBridge();
    }
    public static void Main()
    {
        var bridge = Fresh();
        bridge.SignalDuringEntry = true;
        bridge.Apply();
        for (int i = 0; i < 5; i++) bridge.Apply();
        Require(bridge.Entries == 1 && bridge.Failures == 0,
            "Repeated or reentrant surface notifications reexecuted the waiting step.");
        Current = Next;
        Next = null;
        bridge.Apply();
        Require(bridge.Entries == 2, "A changed cursor did not enter the new step.");

        bridge = Fresh();
        bridge.Apply();
        GuideResume.FailNextSave = true;
        bridge.Satisfy();
        Require(bridge.Failures == 1 && bridge.Advances == 0 && Current.StepId == 101,
            "Failed initial confirmation advanced the cursor.");
        int presentations = bridge.Presentations;
        bridge.Apply();
        Require(bridge.Entries == 2 && bridge.Advances == 1 && Current.StepId == 102,
            "Confirmation retry replayed the completed entry or failed to enter the next step.");
        Require(bridge.Presentations == presentations + 1 && GuideResume.SaveCalls == 3,
            "Confirmation retry presented the satisfied step again or skipped its save boundary.");

        bridge = Fresh();
        bridge.Apply();
        GuideResume.FailAtSave = 2;
        bridge.Satisfy();
        Require(bridge.Failures == 1 && bridge.Advances == 1 && Current.StepId == 102,
            "Second confirmation failure rolled the completed cursor back.");
        bridge.Apply();
        Require(bridge.Entries == 2 && bridge.Advances == 1 && GuideResume.SaveCalls == 3,
            "Retrying the advanced cursor replayed its preceding step or skipped the pending save.");

        bridge = Fresh();
        bridge.Apply();
        var pending = new TaskCompletionSource<bool>();
        GuideResume.PendingSave = pending;
        bridge.Satisfy();
        bridge.Satisfy();
        bridge.Apply();
        Require(bridge.Entries == 1 && bridge.Advances == 0 && GuideResume.SaveCalls == 1,
            "Concurrent completion notifications duplicated an in-flight save or entry.");
        pending.SetResult(true);
        Require(bridge.Advances == 1 && bridge.Entries == 2 && GuideResume.SaveCalls == 2,
            "Confirmed asynchronous completion did not advance exactly once.");
        Console.WriteLine("PASS: same-step/reentrant notifications, changed cursor, first/second-save failure retry without replay, concurrent completion, asynchronous confirmation.");
    }
}

public static class TaskExtensions
{
    public static void Forget(this Task task)
    {
        if (task.IsCompleted) task.GetAwaiter().GetResult();
        else task.GetAwaiter().OnCompleted(() => task.GetAwaiter().GetResult());
    }
}
public enum EOutgameTutorialStepResult { Failed, Gated, Advanced }
public enum EOutgameTutorialCompletion { Other, SynergyDeckEditor }
public sealed class TutorialStepDef { public int StepId; public int Action; public bool LeavesScene; public EOutgameTutorialCompletion Completion; }
public sealed class TutorialActionMeta
{
    public bool RequiresEntryConfirmation => true;
    public bool RequiresCompletionConfirmation => true;
    public static TutorialActionMeta Of(int action) => new TutorialActionMeta();
}
public sealed class TutorialChapter
{
    public int StepCount => 0;
    public bool TryGetStep(int index, out TutorialStepDef step) { step = null; return false; }
}
public sealed class TutorialData { public readonly List<TutorialChapter> Chapters = new List<TutorialChapter>(); }
public static class OutgameTutorialRunner
{
    public static bool IsRunning => Harness.Current != null;
    public static bool IsGuidedRunning => false;
    public static TutorialData Data => null;
}
public static class OutgameTutorialGuide { public static bool TryGetCurrentStep(out TutorialStepDef step) { step = Harness.Current; return step != null; } }
public static class OnboardingCommands { public static bool HasPending => false; public static Task RecoverPendingAsync(CancellationToken token) => Task.CompletedTask; }
public static class ServerSaveCommands { public static bool IsInFlight => false; }
public static class GuideResume
{
    public static int SaveCalls;
    public static int FailAtSave;
    public static bool FailNextSave;
    public static TaskCompletionSource<bool> PendingSave;
    public static Task<bool> SaveConfirmedAsync(CancellationToken token)
    {
        SaveCalls++;
        if (PendingSave != null) { var pending = PendingSave; PendingSave = null; return pending.Task; }
        bool success = !FailNextSave && SaveCalls != FailAtSave;
        FailNextSave = false;
        return Task.FromResult(success);
    }
}
public sealed class UserSaveData { public TutorialSaveData Tutorial = new TutorialSaveData(); }
public sealed class TutorialSaveData { public OnboardingExecutionSaveData Execution; }
public static class DataSaveManager { public static UserSaveData Data = new UserSaveData(); }
public static class OutgameTutorialProgress { public static void Save() { } }
public static class OutgameFeatureLock { public static void Refresh() { } }
public static class LoadingCoverView { public static bool OwnsLobbyPreparation => false; }
public static class GuidanceCoordinator { public static bool IsRestoring => false; }
public static class ServerWaitOverlay { public static void Hold(object owner) { } public static void Release(object owner) { } }
public static class Time { public static float unscaledTime => 1; }
namespace Firebase.Firestore
{
    public enum UnknownPropertyHandling { Ignore }
    public sealed class FirestoreDataAttribute : Attribute { public UnknownPropertyHandling UnknownPropertyHandling { get; set; } }
    public sealed class FirestorePropertyAttribute : Attribute { public FirestorePropertyAttribute(string name) { } }
}
