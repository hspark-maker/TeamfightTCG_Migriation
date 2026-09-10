using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

static class Tests
{
    static int passed;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static PayoutListResult Empty() => new PayoutListResult { Payouts = new List<PayoutEntry>() };
    static PayoutListResult One(string id = "match-1") => new PayoutListResult
    {
        Payouts = new List<PayoutEntry> { new PayoutEntry { MatchId = id, Rank = new PayoutRank(), Currency = new PayoutCurrency() } }
    };
    static PayoutAckResult Ack(string id = "match-1") => new PayoutAckResult { Acked = new List<string> { id } };
    static void Reset()
    {
        PayoutInbox.Shutdown();
        LocalPrefs.Values.Clear();
        FakeClock.Reset();
        ServerSaveCommands.Reset();
        RankManager.Applied = BattleRewardHandoff.Amount = 0;
        MissionCommands.Invalidations = 0;
        RankManager.IsConfigured = SaveDependentManagersStep.IsInstalled = MatchResultSubmission.SignedIn = true;
        GameInitialization.IsTerminated = false;
    }
    static void Run(string name, Action action)
    {
        Reset(); action(); passed++; Console.WriteLine("PASS " + name);
    }
    static void CooldownAndFlush()
    {
        var first = ServerSaveCommands.QueueList();
        PayoutInbox.Initialize("test");
        Check(ServerSaveCommands.ListCalls == 1, "Initialization must fetch once");
        first.SetResult(Empty());
        Check(MissionCommands.Invalidations == 0, "Empty inbox must not invalidate mission cache");
        Check(PayoutInbox.FlushAsync().GetAwaiter().IsCompleted, "Empty flush should complete");
        FakeClock.Advance(59.9);
        PayoutInbox.RefreshOnResume();
        Check(ServerSaveCommands.ListCalls == 1, "Recent empty resume and flush must not fetch");
        var next = ServerSaveCommands.QueueList();
        FakeClock.Advance(0.1);
        PayoutInbox.RefreshOnResume();
        Check(ServerSaveCommands.ListCalls == 2, "Resume must fetch at 60 seconds");
        next.SetResult(Empty());
    }
    static void ForceBypassesCooldown()
    {
        var first = ServerSaveCommands.QueueList();
        PayoutInbox.Initialize("test"); first.SetResult(Empty());
        var next = ServerSaveCommands.QueueList();
        PayoutInbox.RetryPending();
        Check(ServerSaveCommands.ListCalls == 2, "Settlement force must bypass cooldown");
        next.SetResult(Empty());
    }
    static void InFlightForceAndFlush()
    {
        var first = ServerSaveCommands.QueueList();
        var next = ServerSaveCommands.QueueList();
        PayoutInbox.Initialize("test");
        PayoutInbox.RetryPending(); PayoutInbox.RetryPending(); PayoutInbox.RetryPending();
        UniTask flush = PayoutInbox.FlushAsync();
        Check(!flush.GetAwaiter().IsCompleted && ServerSaveCommands.ListCalls == 1, "Force must share current operation");
        first.SetResult(Empty());
        Check(ServerSaveCommands.ListCalls == 2 && !flush.GetAwaiter().IsCompleted, "Flush must wait for one coalesced follow-up");
        next.SetResult(Empty());
        Check(flush.GetAwaiter().IsCompleted && ServerSaveCommands.ListCalls == 2, "Follow-up must complete shared flush");
    }
    static void RealTimeRetry()
    {
        var first = ServerSaveCommands.QueueList();
        var next = ServerSaveCommands.QueueList();
        PayoutInbox.Initialize("test"); first.SetException(new Exception("offline"));
        Check(FakeClock.DelayTypes.Count == 1 && FakeClock.DelayTypes[0] == DelayType.Realtime, "Retry delay must use realtime");
        FakeClock.Advance(29.9);
        Check(ServerSaveCommands.ListCalls == 1, "Must not retry early");
        FakeClock.Advance(0.1);
        Check(ServerSaveCommands.ListCalls == 2, "Must retry at 30 seconds");
        next.SetResult(Empty());
    }
    static void StaleRetryInvalidated()
    {
        var first = ServerSaveCommands.QueueList();
        PayoutInbox.Initialize("test"); first.SetException(new Exception("offline"));
        var next = ServerSaveCommands.QueueList();
        PayoutInbox.RetryPending(); next.SetResult(Empty());
        FakeClock.Advance(30);
        Check(ServerSaveCommands.ListCalls == 2, "Successful external force must invalidate old timer");
    }
    static void AckRecovery()
    {
        var first = ServerSaveCommands.QueueList(); var ack = ServerSaveCommands.QueueAck();
        PayoutInbox.Initialize("test"); first.SetResult(One());
        Check(MissionCommands.Invalidations == 1, "Observed server payout must invalidate mission cache");
        Check(RankManager.Applied == 1 && BattleRewardHandoff.Amount == 0, "Rank once; reward only after ack");
        ack.SetException(new Exception("ack lost"));
        var retry = ServerSaveCommands.QueueList(); var retryAck = ServerSaveCommands.QueueAck();
        // A known unfinished payout must still be recoverable by lifecycle flush.
        UniTask flush = PayoutInbox.FlushAsync(); retry.SetResult(One());
        Check(RankManager.Applied == 1 && ServerSaveCommands.AckCalls == 2, "Retry must ack without duplicate rank application");
        Check(!flush.GetAwaiter().IsCompleted, "Flush must wait for ack");
        retryAck.SetResult(Ack());
        Check(flush.GetAwaiter().IsCompleted && BattleRewardHandoff.Amount == 10, "Successful ack must finish and publish reward once");
        Check(LocalPrefs.Values.Count == 0, "Ack must clear durable applied marker");
        FakeClock.Advance(30);
        Check(ServerSaveCommands.ListCalls == 2, "Recovered ack must invalidate old retry");
    }
    static void OldListCannotEnterNewSession()
    {
        var oldList = ServerSaveCommands.QueueList();
        PayoutInbox.Initialize("old");
        PayoutInbox.Shutdown();
        var newList = ServerSaveCommands.QueueList();
        PayoutInbox.Initialize("new");
        UniTask newFlush = PayoutInbox.FlushAsync();
        oldList.SetResult(One("old-match"));
        Check(RankManager.Applied == 0 && ServerSaveCommands.AckCalls == 0, "Old list must not apply or ack");
        Check(!newFlush.GetAwaiter().IsCompleted, "Old completion must not release new session gate");
        newList.SetResult(Empty());
        Check(newFlush.GetAwaiter().IsCompleted, "New session gate must complete normally");
    }
    static void OldAckCannotPublishIntoNewSession()
    {
        var oldList = ServerSaveCommands.QueueList(); var oldAck = ServerSaveCommands.QueueAck();
        PayoutInbox.Initialize("old"); oldList.SetResult(One("old-match"));
        PayoutInbox.Shutdown();
        // LocalPrefs is account-scoped in the application; emulate changing account namespace.
        LocalPrefs.Values.Clear();
        var newList = ServerSaveCommands.QueueList();
        PayoutInbox.Initialize("new");
        UniTask newFlush = PayoutInbox.FlushAsync();
        oldAck.SetResult(Ack("old-match"));
        Check(BattleRewardHandoff.Amount == 0, "Old ack must not publish reward into new session");
        Check(!newFlush.GetAwaiter().IsCompleted, "Old ack must not release new gate");
        newList.SetResult(Empty());
    }
    static void ShutdownStopsRetryAndInitWait()
    {
        var first = ServerSaveCommands.QueueList();
        PayoutInbox.Initialize("test"); first.SetException(new Exception("offline"));
        PayoutInbox.Shutdown(); FakeClock.Advance(30);
        Check(ServerSaveCommands.ListCalls == 1, "Shutdown must invalidate delayed retry");
        Reset(); SaveDependentManagersStep.IsInstalled = false;
        PayoutInbox.Initialize("test"); UniTask flush = PayoutInbox.FlushAsync();
        Check(!flush.GetAwaiter().IsCompleted, "Initialization barrier must remain pending");
        PayoutInbox.Shutdown(); FakeClock.Advance(0);
        Check(flush.GetAwaiter().IsCompleted && ServerSaveCommands.ListCalls == 0, "Shutdown must release old initialization wait without network");
    }
    static PayoutListResult Page(bool valid = true)
    {
        var result = Empty();
        for (int i = 0; i < 20; i++)
        {
            var entry = One("page-" + i).Payouts[0];
            if (!valid) entry.Currency.Currency = "unknown";
            result.Payouts.Add(entry);
        }
        return result;
    }
    static void FullPageDrainsRemainder()
    {
        var page = Page();
        var first = ServerSaveCommands.QueueList(); var firstAck = ServerSaveCommands.QueueAck();
        var remainder = ServerSaveCommands.QueueList(); var remainderAck = ServerSaveCommands.QueueAck();
        PayoutInbox.Initialize("test"); UniTask flush = PayoutInbox.FlushAsync();
        first.SetResult(page);
        var ids = new List<string>(); foreach (var payout in page.Payouts) ids.Add(payout.MatchId);
        firstAck.SetResult(new PayoutAckResult { Acked = ids });
        Check(ServerSaveCommands.ListCalls == 2 && !flush.GetAwaiter().IsCompleted, "Full acknowledged page must fetch remaining payouts before flush completes");
        remainder.SetResult(One("remainder")); remainderAck.SetResult(Ack("remainder"));
        Check(flush.GetAwaiter().IsCompleted && RankManager.Applied == 21 && BattleRewardHandoff.Amount == 210, "All 21 payouts must settle once");
        Check(ServerSaveCommands.ListCalls == 2, "Partial final page must stop paging");
    }
    static void FullPageWithoutProgressStops()
    {
        var first = ServerSaveCommands.QueueList(); var ack = ServerSaveCommands.QueueAck();
        PayoutInbox.Initialize("test"); UniTask flush = PayoutInbox.FlushAsync(); first.SetResult(Page());
        ack.SetResult(new PayoutAckResult { Acked = new List<string>() });
        Check(ServerSaveCommands.ListCalls == 1 && flush.GetAwaiter().IsCompleted,
            "A page with no acknowledgement progress must stop automatic pagination");
    }
    static void FullPageInvalidEntriesStops()
    {
        var first = ServerSaveCommands.QueueList();
        PayoutInbox.Initialize("test"); first.SetResult(Page(false));
        Check(ServerSaveCommands.ListCalls == 1 && ServerSaveCommands.AckCalls == 0, "Invalid full page must not spin or acknowledge unknown currency");
        Check(PayoutInbox.FlushAsync().GetAwaiter().IsCompleted, "Invalid entries must not leave in-flight operation stuck");
    }
    static int Main()
    {
        try
        {
            Run("initial fetch, 60s empty cooldown, passive flush", CooldownAndFlush);
            Run("settlement force bypasses cooldown", ForceBypassesCooldown);
            Run("in-flight force coalesces; flush awaits follow-up", InFlightForceAndFlush);
            Run("failure retries after 30 realtime seconds", RealTimeRetry);
            Run("external success invalidates stale retry", StaleRetryInvalidated);
            Run("failed ack recovers without duplicate rank/reward", AckRecovery);
            Run("old list cannot mutate new session", OldListCannotEnterNewSession);
            Run("old ack cannot publish in new session", OldAckCannotPublishIntoNewSession);
            Run("shutdown cancels stale retry and initialization wait", ShutdownStopsRetryAndInitWait);
            Run("20-entry page drains remaining payout before flush ends", FullPageDrainsRemainder);
            Run("zero acknowledged entries stop automatic pagination", FullPageWithoutProgressStops);
            Run("invalid full page does not loop", FullPageInvalidEntriesStops);
            Console.WriteLine(passed + " behavior tests passed against production PayoutInbox.cs"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
