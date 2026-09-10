using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Firebase.Functions;

static class Tests
{
    static int passed;
    static IList Pending => (IList)typeof(MatchResultSubmission).GetField("s_pending", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    static int Attempts(int index = 0) => (int)Pending[index].GetType().GetField("attempts").GetValue(Pending[index]);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Start(string match = "match-1")
    {
        SoloMatchHandoff.MatchId = match;
        Check(MatchResultSubmission.TryEnqueue(true, 2, 0, 100, _endStateHash: 123), "Valid result must enqueue");
    }
    static void Reset()
    {
        MatchResultSubmission.Shutdown();
        FakeClock.Advance(0);
        MatchResultSubmission.DiscardPending();
        LocalPrefs.Values.Clear(); FakeClock.Reset(); Forgotten.Faults.Clear(); FakeServer.Reset();
        FirebaseAuthService.Instance.IsCurrentUserActive = true;
        FirebaseAuthService.Instance.Initialization = null;
        PayoutInbox.Retries = MissionCommands.Invalidations = MatchResultFailurePopup.Shown = 0;
        MatchResultSubmission.Initialize("test");
    }
    static void Run(string name, Action test)
    {
        Reset(); test();
        Check(Forgotten.Faults.Count == 0, "Forgotten task fault: " + string.Join("; ", Forgotten.Faults));
        passed++; Console.WriteLine("PASS " + name);
    }
    static void ResumeReplacesTimer()
    {
        Start(); FakeServer.Reply("pending");
        Check(FakeClock.ActiveCount == 1 && FakeClock.Delays.Last() == 15, "First pending should reserve 15 seconds");
        FakeClock.Advance(5);
        MatchResultSubmission.RetryPending();
        Check(FakeClock.ActiveCount == 0, "Immediate send cancels reservation before awaiting response");
        FakeServer.Reply("pending");
        Check(FakeClock.ActiveCount == 1 && FakeClock.Delays.Last() == 30, "Resume schedules one 30-second retry");
        FakeClock.Advance(10);
        Check(FakeServer.Calls.Count == 2, "Old 15-second timer must not submit");
        FakeClock.Advance(19);
        Check(FakeServer.Calls.Count == 2, "Replacement timer must not fire early");
        FakeClock.Advance(1);
        Check(FakeServer.Calls.Count == 3 && FakeClock.ActiveCount == 0, "Replacement timer submits once at t35");
        FakeServer.Reply("confirmed");
        Check(FakeClock.ActiveCount == 0 && Pending.Count == 0, "Completed retry must leave no reservation");
    }
    static void RepeatedResume(bool immediateCancellation)
    {
        FakeClock.ImmediateCancellation = immediateCancellation;
        Start(); FakeServer.Reply("pending");
        for (int i = 0; i < 10; i++)
        {
            MatchResultSubmission.RetryPending(); FakeServer.Reply("pending");
            Check(FakeClock.ActiveCount == 1, "Repeated resume must keep exactly one effective timer");
        }
        FakeClock.Advance(299);
        Check(FakeServer.Calls.Count == 11, "Canceled timers must not submit while latest waits");
        FakeClock.Advance(1);
        Check(FakeServer.Calls.Count == 12, "Final timer fires once");
        FakeServer.Reply("confirmed");
    }
    static void ActiveSendDeduplicates()
    {
        Start();
        for (int i = 0; i < 5; i++) { MatchResultSubmission.RetryPending(); MatchResultSubmission.FlushAsync().GetAwaiter().GetResult(); }
        Check(FakeServer.Calls.Count == 1 && FakeClock.ActiveCount == 0, "Resume and flush during a send must not duplicate submission");
        FakeServer.Reply("pending");
        Check(FakeClock.ActiveCount == 1, "Active send must leave only one reservation");
    }
    static void FlushReplacesTimer()
    {
        Start(); FakeServer.Reply("pending"); FakeClock.Advance(5);
        var flush = MatchResultSubmission.FlushAsync();
        Check(!flush.GetAwaiter().IsCompleted && FakeClock.ActiveCount == 0, "Flush cancels old timer and waits for its own send");
        FakeServer.Reply("pending"); flush.GetAwaiter().GetResult();
        FakeClock.Advance(10);
        Check(FakeServer.Calls.Count == 2 && FakeClock.ActiveCount == 1, "Old timer cannot interrupt flush backoff");
    }
    static void ConfirmedClearsQueue()
    {
        Start(); FakeServer.Reply("pending"); MatchResultSubmission.RetryPending(); FakeServer.Reply("confirmed");
        FakeClock.Advance(1000);
        Check(Pending.Count == 0 && LocalPrefs.Values.Count == 0 && FakeClock.ActiveCount == 0 && FakeServer.Calls.Count == 2,
            "Confirmation clears persisted queue and all retries");
        Check(PayoutInbox.Retries == 1 && MissionCommands.Invalidations == 1, "Confirmed settlement hooks must remain intact");
    }
    static void PermanentRejectionClearsQueue()
    {
        Start(); FakeServer.Reply("pending"); MatchResultSubmission.RetryPending(); FakeServer.Fail(FunctionsErrorCode.PermissionDenied);
        FakeClock.Advance(1000);
        Check(Pending.Count == 0 && FakeClock.ActiveCount == 0 && FakeServer.Calls.Count == 2 && MatchResultFailurePopup.Shown == 1,
            "Permanent rejection stops resubmission and keeps failure notice");
    }
    static void TransientBackoffPreserved()
    {
        Start();
        int[] expected = { 15, 30, 60, 120, 300, 300, 300, 300, 300 };
        for (int i = 0; i < expected.Length; i++)
        {
            FakeServer.Fail(FunctionsErrorCode.Unavailable);
            Check(Pending.Count == 1 && Attempts() == Math.Min(i + 1, 8), "Transient errors must retain pending with capped attempts");
            Check(FakeClock.Delays.Last() == expected[i] && FakeClock.ActiveCount == 1, "Existing backoff must be preserved");
            Check(FakeClock.DelayTypes.Last() == Cysharp.Threading.Tasks.DelayType.DeltaTime, "Existing scaled-time delay must remain unchanged");
            FakeClock.Advance(expected[i]);
            Check(FakeServer.Calls.Count == i + 2, "Backoff must cause one request");
        }
        FakeServer.Reply("confirmed");
    }
    static void InitializeInvalidatesOldTimer()
    {
        Start(); FakeServer.Reply("pending");
        MatchResultSubmission.Initialize("test");
        Check(Pending.Count == 1 && FakeClock.ActiveCount == 0, "Initialize restores queue while canceling old session timer");
        MatchResultSubmission.RetryPending(); FakeServer.Reply("pending");
        FakeClock.Advance(15);
        Check(FakeServer.Calls.Count == 2 && FakeClock.ActiveCount == 1, "Old session timer cannot clear or fire replacement");
        FakeClock.Advance(15);
        Check(FakeServer.Calls.Count == 3, "Restored retry retains attempts and 30-second interval");
        FakeServer.Reply("confirmed");
    }
    static void ShutdownCancelsTimer()
    {
        Start(); FakeServer.Reply("pending"); MatchResultSubmission.Shutdown();
        MatchResultSubmission.RetryPending(); MatchResultSubmission.FlushAsync().GetAwaiter().GetResult(); FakeClock.Advance(1000);
        Check(FakeServer.Calls.Count == 1 && FakeClock.ActiveCount == 0 && LocalPrefs.Values.Count == 1,
            "Shutdown cancels timers, blocks submissions, and preserves unfinished result");
    }
    static void DiscardCancelsTimer()
    {
        Start(); FakeServer.Reply("pending"); MatchResultSubmission.DiscardPending(); FakeClock.Advance(1000);
        Check(Pending.Count == 0 && LocalPrefs.Values.Count == 0 && FakeClock.ActiveCount == 0 && FakeServer.Calls.Count == 1,
            "Discard removes queue and its retry");
    }
    static void DiscardIgnoresLateResponse(bool fail)
    {
        Start("old"); MatchResultSubmission.DiscardPending(); Start("new");
        Check(FakeServer.Calls.Count == 2, "New session must submit without waiting for discarded result");
        if (fail) FakeServer.Fail(FunctionsErrorCode.PermissionDenied, 0); else FakeServer.Reply("confirmed", 0);
        Check(Pending.Count == 1 && MatchResultFailurePopup.Shown == 0 && PayoutInbox.Retries == 0 && MissionCommands.Invalidations == 0,
            "Late discarded result must not mutate new queue or display settlement");
        MatchResultSubmission.RetryPending();
        Check(FakeServer.Calls.Count == 2, "Old finally must not release the new send guard");
        FakeServer.Reply("pending", 1);
        Check(FakeClock.ActiveCount == 1 && Attempts() == 1, "New pending result owns its single retry");
    }
    static void DiscardIgnoresLateAuth()
    {
        FirebaseAuthService.Instance.IsCurrentUserActive = false;
        var oldAuth = FirebaseAuthService.Instance.Initialization = new TaskCompletionSource<bool>();
        Start("old"); MatchResultSubmission.DiscardPending();
        var newAuth = FirebaseAuthService.Instance.Initialization = new TaskCompletionSource<bool>();
        Start("new"); oldAuth.SetResult(false);
        Check(Pending.Count == 1 && Attempts() == 0 && FakeClock.ActiveCount == 0, "Old auth result must not charge new queue or schedule retry");
        newAuth.SetResult(false);
        Check(Attempts() == 1 && FakeClock.ActiveCount == 1 && FakeServer.Calls.Count == 0, "Current auth failure retains one recovery timer");
    }
    static void NewResultReplacesTimer()
    {
        Start("first"); FakeServer.Reply("pending"); FakeClock.Advance(5); Start("second");
        Check(FakeServer.Calls.Count == 2 && FakeClock.ActiveCount == 0, "New result immediately cancels previous retry");
        FakeServer.Reply("pending"); FakeServer.Reply("pending");
        Check(Pending.Count == 2 && FakeClock.ActiveCount == 1 && FakeClock.Delays.Last() == 15,
            "Multiple pending items share one retry using least attempted item");
        FakeClock.Advance(10);
        Check(FakeServer.Calls.Count == 3, "Old timer must not submit the batch early");
        FakeClock.Advance(5);
        Check(FakeServer.Calls.Count == 4, "Single replacement timer starts next batch");
        FakeServer.Reply("confirmed"); FakeServer.Reply("confirmed");
        Check(Pending.Count == 0 && FakeClock.ActiveCount == 0, "Confirmed batch drains queue");
    }
    static void ModuleInitializationResumesSavedResult()
    {
        Start(); FakeServer.Reply("pending"); MatchResultSubmission.Shutdown();
        var module = new MatchResultFirebaseModule();
        module.Initialize(new FirebaseContext { EnvId = "test" });
        Check(FakeServer.Calls.Count == 2, "Startup must resume saved results without a battle entry flush");
        FakeServer.Reply("pending");
        FakeClock.Advance(15);
        Check(FakeServer.Calls.Count == 2 && FakeClock.ActiveCount == 1, "Old session timer must not duplicate startup retry");
        FakeClock.Advance(15);
        Check(FakeServer.Calls.Count == 3, "Startup submission must retain normal retry backoff");
        FakeServer.Reply("confirmed");
        Check(Pending.Count == 0 && PayoutInbox.Retries == 1, "Startup recovery must collect confirmed payout");
    }

    public static int Main()
    {
        try
        {
            Run("resume replaces t15 with t35 retry", ResumeReplacesTimer);
            Run("repeated resume with player-loop cancellation", () => RepeatedResume(false));
            Run("repeated resume with immediate cancellation", () => RepeatedResume(true));
            Run("active send deduplicates resume and flush", ActiveSendDeduplicates);
            Run("flush replaces prior timer", FlushReplacesTimer);
            Run("confirmed clears queue and timers", ConfirmedClearsQueue);
            Run("permanent rejection stops retry", PermanentRejectionClearsQueue);
            Run("transient backoff and attempt cap preserved", TransientBackoffPreserved);
            Run("initialize cancels stale session timer", InitializeInvalidatesOldTimer);
            Run("shutdown cancels retry and retains queue", ShutdownCancelsTimer);
            Run("discard cancels retry", DiscardCancelsTimer);
            Run("discard ignores late success", () => DiscardIgnoresLateResponse(false));
            Run("discard ignores late permanent rejection", () => DiscardIgnoresLateResponse(true));
            Run("discard ignores late authentication", DiscardIgnoresLateAuth);
            Run("new result replaces timer and shares batch retry", NewResultReplacesTimer);
            Run("module startup resumes saved results without entry flush", ModuleInitializationResumesSavedResult);
            Console.WriteLine($"{passed} tests passed."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
