using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;

static class Tests
{
    static int passed, failed;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static bool Done(UniTask<bool> task)
    {
        Check(task.GetAwaiter().IsCompleted, "Expected request to have completed");
        return task.GetAwaiter().GetResult();
    }
    static MissionSnapshot Snapshot(string key = "fresh") => new MissionSnapshot
    {
        DailyKey = key, WeeklyKey = key,
        DailyResetAtMs = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds(),
        WeeklyResetAtMs = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds(),
        Progress = new Dictionary<string, long>(), Claimed = new Dictionary<string, bool>()
    };
    static MissionGetResponse Response(string key = "fresh") => new MissionGetResponse
    {
        Missions = Snapshot(key), Definitions = new List<MissionDefinition>
        {
            new MissionDefinition { Id = "mission", Period = "daily", Event = "Play", Target = 1 }
        }
    };
    static void Reset()
    {
        MissionCommands.ResetSession();
        typeof(MissionManager).GetMethod("ResetRuntimeState", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        FakeClock.Reset(); ServerSaveCommands.Reset();
        ContentProfileConfig.Active = new ContentProfileConfig();
        FirebaseAuthService.Instance = new FirebaseAuthService();
        PlayerSaveCloud.Revision = 1; PlayerSaveCloud.HasPendingUpload = false;
    }
    static void Seed(string key = "fresh")
    {
        var read = ServerSaveCommands.QueueRead(); var task = MissionCommands.RefreshAsync();
        read.SetResult(Response(key)); Check(Done(task), "Seed request should succeed");
    }
    static void Run(string name, Action test)
    {
        Reset();
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    static void SharedRequest()
    {
        var read = ServerSaveCommands.QueueRead();
        var tasks = new List<UniTask<bool>>();
        for (int i = 0; i < 10; i++) tasks.Add(MissionCommands.RefreshAsync(i == 5));
        Check(ServerSaveCommands.ReadCalls == 1, "Concurrent normal/forced queries must share one callable");
        read.SetResult(Response());
        foreach (var task in tasks) Check(Done(task), "Every shared waiter should receive success");
    }
    static void CacheExpires()
    {
        Seed(); FakeClock.Advance(29.9);
        Check(Done(MissionCommands.RefreshAsync()) && ServerSaveCommands.ReadCalls == 1, "Fresh response must serve cache");
        FakeClock.Advance(0.1); var read = ServerSaveCommands.QueueRead();
        var task = MissionCommands.RefreshAsync();
        Check(ServerSaveCommands.ReadCalls == 2, "30-second boundary must fetch again");
        read.SetResult(Response()); Check(Done(task), "Expired refresh must complete");
    }
    static void ForceBypassesCache()
    {
        Seed(); var read = ServerSaveCommands.QueueRead();
        var task = MissionCommands.RefreshAsync(true);
        var other = MissionCommands.RefreshAsync(true);
        Check(ServerSaveCommands.ReadCalls == 2, "Force must bypass TTL and share in-flight request");
        read.SetResult(Response()); Check(Done(task) && Done(other), "Forced waiters should share completion");
    }
    static void ResetBoundary(bool daily)
    {
        var read = ServerSaveCommands.QueueRead(); var task = MissionCommands.RefreshAsync();
        var response = Response();
        if (daily) response.Missions.DailyResetAtMs = 1; else response.Missions.WeeklyResetAtMs = 1;
        read.SetResult(response); Check(Done(task), "Server snapshot may reach reset boundary");
        var next = ServerSaveCommands.QueueRead(); task = MissionCommands.RefreshAsync();
        Check(ServerSaveCommands.ReadCalls == 2, "Reset boundary must bypass realtime TTL");
        next.SetResult(Response()); Check(Done(task), "Boundary refresh should succeed");
    }
    static void SnapshotWithoutDefinitions()
    {
        MissionManager.Adopt(Snapshot("mutation-only"));
        Check(MissionManager.IsReady && MissionManager.Definitions.Count == 0, "Fixture must have state only");
        var read = ServerSaveCommands.QueueRead(); var task = MissionCommands.RefreshAsync();
        Check(ServerSaveCommands.ReadCalls == 1, "State-only snapshot must not suppress definition fetch");
        read.SetResult(Response()); Check(Done(task) && MissionManager.Definitions.Count == 1, "Definitions must be installed");
    }
    static void MutationDuringRead()
    {
        var read = ServerSaveCommands.QueueRead(); var followup = ServerSaveCommands.QueueRead();
        var task = MissionCommands.RefreshAsync();
        MissionManager.Adopt(Snapshot("mutation"));
        read.SetResult(Response("old"));
        Check(MissionManager.DailyKey == "mutation" && ServerSaveCommands.ReadCalls == 2,
            "Old query must not overwrite mutation; one follow-up should start");
        Check(!task.GetAwaiter().IsCompleted, "Shared waiter must include follow-up");
        followup.SetResult(Response("new"));
        Check(Done(task) && MissionManager.DailyKey == "new", "Valid follow-up must adopt");
    }
    static void MutationDoesNotExtendDefinitionTtl()
    {
        Seed(); FakeClock.Advance(20); MissionManager.Adopt(Snapshot("mutation"));
        Check(Done(MissionCommands.RefreshAsync()) && ServerSaveCommands.ReadCalls == 1 && MissionManager.DailyKey == "mutation",
            "Latest mutation state may reuse still-fresh definitions");
        FakeClock.Advance(10); var read = ServerSaveCommands.QueueRead(); var task = MissionCommands.RefreshAsync();
        Check(ServerSaveCommands.ReadCalls == 2, "Mutation must not extend original definition TTL");
        read.SetResult(Response()); Check(Done(task), "Definition refresh should complete");
    }
    static void ClaimBlocksReadAndFailureIsRetryable()
    {
        Seed(); var claim = ServerSaveCommands.QueueClaim();
        var claimTask = MissionCommands.ClaimAsync("mission");
        Check(!Done(MissionCommands.RefreshAsync()) && ServerSaveCommands.ReadCalls == 1, "Query must not start during claim");
        claim.SetException(new Exception("offline"));
        Check(claimTask.GetAwaiter().IsCompleted && claimTask.GetAwaiter().GetResult() == null, "Claim failure should finish cleanly");
        var read = ServerSaveCommands.QueueRead(); var task = MissionCommands.RefreshAsync();
        Check(ServerSaveCommands.ReadCalls == 2, "Failed claim must leave query eligible despite previous TTL");
        read.SetResult(Response()); Check(Done(task), "Recovery query must succeed");
    }
    static void ClaimOverlappingReadDiscardsOldResult()
    {
        var read = ServerSaveCommands.QueueRead(); var task = MissionCommands.RefreshAsync();
        var claim = ServerSaveCommands.QueueClaim(); var claimTask = MissionCommands.ClaimAsync("mission");
        MissionManager.Adopt(Snapshot("claim-state"));
        read.SetResult(Response("old"));
        Check(!Done(task) && MissionManager.DailyKey == "claim-state" && ServerSaveCommands.ReadCalls == 1,
            "Read overlapped by active claim must discard without issuing concurrent follow-up");
        claim.SetResult(new ClaimMissionResult()); Check(claimTask.GetAwaiter().IsCompleted, "Claim should finish");
    }
    static void RevisionAndPendingUpload()
    {
        Seed(); PlayerSaveCloud.Revision++;
        var revised = ServerSaveCommands.QueueRead(); var task = MissionCommands.RefreshAsync();
        Check(ServerSaveCommands.ReadCalls == 2, "Changed save revision must bypass TTL");
        revised.SetResult(Response()); Check(Done(task), "Revision refresh should succeed");
        PlayerSaveCloud.HasPendingUpload = true;
        var pending = ServerSaveCommands.QueueRead(); task = MissionCommands.RefreshAsync();
        Check(ServerSaveCommands.ReadCalls == 3, "Pending save upload must bypass TTL");
        pending.SetResult(Response()); Check(Done(task), "Pending upload response still supplies visible state");
        PlayerSaveCloud.HasPendingUpload = false;
        var clean = ServerSaveCommands.QueueRead(); task = MissionCommands.RefreshAsync();
        Check(ServerSaveCommands.ReadCalls == 4, "Response read during upload must not become cache after upload clears");
        clean.SetResult(Response()); Check(Done(task), "Clean refresh should succeed");
    }
    static void RevisionChangesDuringRead()
    {
        var read = ServerSaveCommands.QueueRead(); var next = ServerSaveCommands.QueueRead();
        var task = MissionCommands.RefreshAsync(); PlayerSaveCloud.Revision++;
        read.SetResult(Response("old"));
        Check(!MissionManager.IsReady && ServerSaveCommands.ReadCalls == 2, "Revision change must discard first read");
        next.SetResult(Response()); Check(Done(task), "Stable revision follow-up should succeed");
    }
    static void InvalidationIsBounded()
    {
        var read = ServerSaveCommands.QueueRead(); var next = ServerSaveCommands.QueueRead();
        var task = MissionCommands.RefreshAsync(); MissionCommands.Invalidate(); MissionCommands.Invalidate();
        read.SetResult(Response("old"));
        Check(ServerSaveCommands.ReadCalls == 2 && !MissionManager.IsReady, "Invalidations must coalesce to one follow-up");
        MissionCommands.Invalidate(); next.SetResult(Response("also-old"));
        Check(!Done(task) && ServerSaveCommands.ReadCalls == 2 && !MissionManager.IsReady,
            "Repeated invalidation must stop after one follow-up, without stale adoption");
        var fresh = ServerSaveCommands.QueueRead(); task = MissionCommands.RefreshAsync(); fresh.SetResult(Response());
        Check(Done(task), "Subsequent explicit refresh must remain retryable");
    }
    static void SharedFailureAndRetry()
    {
        var read = ServerSaveCommands.QueueRead(); var first = MissionCommands.RefreshAsync(); var other = MissionCommands.RefreshAsync();
        read.SetException(new Exception("offline"));
        Check(!Done(first) && !Done(other) && ServerSaveCommands.ReadCalls == 1, "Failure must reach all waiters without auto retry");
        var retry = ServerSaveCommands.QueueRead(); first = MissionCommands.RefreshAsync();
        Check(ServerSaveCommands.ReadCalls == 2, "Next caller must retry");
        retry.SetResult(Response()); Check(Done(first), "Retry should succeed");
    }
    static void ChangedIdentity(bool env)
    {
        var old = ServerSaveCommands.QueueRead(); var first = MissionCommands.RefreshAsync();
        if (env) ContentProfileConfig.Active.CloudEnvId = "live"; else FirebaseAuthService.Instance.UserId = "user-2";
        var next = ServerSaveCommands.QueueRead(); var current = MissionCommands.RefreshAsync();
        Check(!Done(first) && ServerSaveCommands.ReadCalls == 2, "Identity change must release old waiter and start new query");
        old.SetResult(Response("old-account"));
        Check(!MissionManager.IsReady && !current.GetAwaiter().IsCompleted, "Old identity must neither adopt nor complete new waiter");
        next.SetResult(Response("new-account"));
        Check(Done(current) && MissionManager.DailyKey == "new-account", "New identity must adopt normally");
        Check(ServerSaveCommands.Environments[1] == ContentProfileConfig.Active.CloudEnvId, "Query must target current env");
    }
    static void ResetSessionDiscardsOldRead()
    {
        var old = ServerSaveCommands.QueueRead(); var first = MissionCommands.RefreshAsync(); MissionCommands.ResetSession();
        Check(!Done(first), "Session reset must release previous waiters");
        var next = ServerSaveCommands.QueueRead(); var current = MissionCommands.RefreshAsync(); old.SetResult(Response("old"));
        Check(!MissionManager.IsReady && !current.GetAwaiter().IsCompleted, "Old session response must not change current state or gate");
        next.SetResult(Response()); Check(Done(current), "Current session should refresh");
    }
    static void DebugResetGuardsOldReadAndOldReset()
    {
        var old = ServerSaveCommands.QueueRead(); var query = MissionCommands.RefreshAsync();
        var reset = ServerSaveCommands.QueueReset(); var resetTask = MissionCommands.ResetDailyForDebugAsync();
        old.SetResult(Response("before-reset"));
        Check(!Done(query) && !MissionManager.IsReady, "Old query must not adopt during debug reset");
        Check(!Done(MissionCommands.RefreshAsync(true)) && ServerSaveCommands.ReadCalls == 1, "Active debug reset must block new query");
        reset.SetResult(new ServerCommandResult { Missions = Snapshot("reset") });
        Check(Done(resetTask) && MissionManager.DailyKey == "reset", "Reset response must adopt current session state");
        var next = ServerSaveCommands.QueueRead(); query = MissionCommands.RefreshAsync();
        Check(ServerSaveCommands.ReadCalls == 2, "Post-reset query must remain eligible");
        next.SetResult(Response("after-reset")); Check(Done(query), "Post-reset refresh should succeed");
        var staleReset = ServerSaveCommands.QueueReset(); var staleTask = MissionCommands.ResetDailyForDebugAsync();
        MissionCommands.ResetSession(); MissionManager.Adopt(Snapshot("new-session"));
        staleReset.SetResult(new ServerCommandResult { Missions = Snapshot("stale-reset") });
        Check(!Done(staleTask) && MissionManager.DailyKey == "new-session", "Old-session debug response must not adopt");
    }
    static int Main()
    {
        Run("ten concurrent queries and force share one result", SharedRequest);
        Run("30-second response cache expires at boundary", CacheExpires);
        Run("force bypasses cache and shares pending request", ForceBypassesCache);
        Run("daily reset bypasses cache", () => ResetBoundary(true));
        Run("weekly reset bypasses cache", () => ResetBoundary(false));
        Run("snapshot-only state still loads definitions", SnapshotWithoutDefinitions);
        Run("mutation invalidates old in-flight read", MutationDuringRead);
        Run("mutation preserves state without extending definition TTL", MutationDoesNotExtendDefinitionTtl);
        Run("claim blocks query; failure remains retryable", ClaimBlocksReadAndFailureIsRetryable);
        Run("active claim discards overlapped query", ClaimOverlappingReadDiscardsOldResult);
        Run("save revision and pending upload bypass cache", RevisionAndPendingUpload);
        Run("revision change during query triggers one follow-up", RevisionChangesDuringRead);
        Run("repeated invalidation allows at most one follow-up", InvalidationIsBounded);
        Run("shared network failure and next caller retry", SharedFailureAndRetry);
        Run("environment change rejects old response", () => ChangedIdentity(true));
        Run("user change rejects old response", () => ChangedIdentity(false));
        Run("explicit session reset releases old query", ResetSessionDiscardsOldRead);
        Run("debug reset guards old query and old-session response", DebugResetGuardsOldReadAndOldReset);
        Console.WriteLine(passed + " passed, " + failed + " failed against production mission sources");
        return failed == 0 ? 0 : 1;
    }
}
