using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;

static class Tests
{
    static int passed, failed;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static T Done<T>(UniTask<T> task)
    {
        Check(task.GetAwaiter().IsCompleted, "Expected completed request");
        return task.GetAwaiter().GetResult();
    }
    static PassProgress Progress(long exp = 200, string season = "season-1", params int[] claimed)
    {
        var result = new PassProgress { SeasonId = season, Exp = exp, Claimed = new Dictionary<string, bool>() };
        foreach (int level in claimed) result.Claimed[level.ToString()] = true;
        return result;
    }
    static PassGetResponse Response() => new PassGetResponse
    {
        Season = new PassSeasonDefinition { SeasonId = "season-1", EndAtMs = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds() },
        Progress = Progress(100), CurrentLevel = 1, NextRequiredExp = 200,
        PackChoices = new List<string> { "rank-pack" },
        Levels = new List<PassLevelDefinition>
        {
            new PassLevelDefinition { Level = 1, RequiredExp = 100 },
            new PassLevelDefinition { Level = 2, RequiredExp = 200 },
            new PassLevelDefinition { Level = 3, RequiredExp = 300 }
        }
    };
    static ClaimPassRewardResult ClaimResult(int level = 1) => new ClaimPassRewardResult
    {
        SeasonId = "season-1", Level = level, Progress = Progress(200, "season-1", level)
    };
    static void Reset()
    {
        typeof(PassCommands).GetMethod("ResetRuntimeState", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        PassManager.Clear(); FakeClock.Reset(); ServerSaveCommands.Reset(); RankManager.Points = 100;
    }
    static void Seed(PassGetResponse response = null)
    {
        var read = ServerSaveCommands.QueueRead(); var task = PassCommands.RefreshAsync();
        read.SetResult(response ?? Response()); Check(Done(task), "Seed must succeed");
    }
    // The panel checks these public conditions before calling RefreshAsync on entry.
    static void OpenPanel()
    {
        if (PassCommands.NeedsRefresh || !PassManager.HasSeason ||
            (PassManager.Season.EndAtMs > 0 && PassManager.Season.EndAtMs <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
            _ = PassCommands.RefreshAsync();
    }
    static void ClaimSucceeds(int level = 1)
    {
        var reply = ServerSaveCommands.QueueClaim(); var task = PassCommands.ClaimAsync(level);
        reply.SetResult(ClaimResult(level)); Check(Done(task) != null, "Claim must succeed");
    }
    static void FreshClaimSkipsReentry()
    {
        Seed(); var choices = PassManager.PackChoices;
        ClaimSucceeds(); Check(PassManager.IsClaimed(1), "Server claim mark must be adopted");
        Check(!PassCommands.NeedsRefresh, "Successful claim with current definitions must restore freshness");
        OpenPanel(); Check(ServerSaveCommands.ReadCalls == 1, "Reentry must not call getPass again");
        ClaimSucceeds(2); OpenPanel();
        Check(!PassCommands.NeedsRefresh && ServerSaveCommands.ReadCalls == 1, "Sequential claims must retain reusable definitions");
        Check(ReferenceEquals(choices, PassManager.PackChoices), "Progress response must retain pack choices");
    }
    static void FreshMissionSkipsReentry()
    {
        Seed(); bool observedFresh = false;
        Action listener = () => observedFresh = !PassCommands.NeedsRefresh;
        PassManager.OnChanged += listener;
        try { PassCommands.ApplyMissionProgress(Progress()); }
        finally { PassManager.OnChanged -= listener; }
        Check(observedFresh, "UI change notification must already see the restored freshness");
        Check(PassManager.Exp == 200 && PassManager.CurrentLevel == 2 && PassManager.NextRequiredExp == 300,
            "Mission progress must recalculate display from retained level definitions");
        OpenPanel(); Check(ServerSaveCommands.ReadCalls == 1, "Mission response must save next entry query");
    }
    static void FailedClaimsStayDirty()
    {
        foreach (Exception error in new Exception[] { new ServerCommandRejectedException(), new ServerAdoptionException(), new Exception("offline"), null })
        {
            Reset(); Seed(); var reply = ServerSaveCommands.QueueClaim(); var task = PassCommands.ClaimAsync(1);
            if (error == null) reply.SetResult(null); else reply.SetException(error);
            Check(Done(task) == null && PassCommands.NeedsRefresh, "Failure/null response must leave retry eligible");
            Check(!PassManager.IsClaimed(1), "Failed claim must preserve prior progress");
        }
    }
    static void InvalidProgressDoesNotOverwrite()
    {
        foreach (PassProgress progress in new[] { (PassProgress)null, Progress(999, "season-2"), Progress(999, "") })
        {
            Reset(); Seed(); var reply = ServerSaveCommands.QueueClaim(); var task = PassCommands.ClaimAsync(1);
            reply.SetResult(new ClaimPassRewardResult { Progress = progress }); Done(task);
            Check(PassCommands.NeedsRefresh && PassManager.Exp == 100, "Missing/mismatched season progress cannot replace cached season state");
        }
    }
    static void MissingDefinitionsStayDirty()
    {
        foreach (bool noSeason in new[] { false, true })
        {
            Reset(); var response = Response();
            if (noSeason) response.Season = null; else response.Levels.Clear();
            Seed(response); ClaimSucceeds();
            Check(PassCommands.NeedsRefresh && PassManager.Exp == 100, "Incomplete definitions cannot be made fresh by claim progress");
        }
    }
    static void ExpiredSeasonStaysDirty()
    {
        foreach (bool expiresDuringClaim in new[] { false, true })
        {
            Reset(); var response = Response();
            if (!expiresDuringClaim) response.Season.EndAtMs = 1;
            Seed(response); var reply = ServerSaveCommands.QueueClaim(); var task = PassCommands.ClaimAsync(1);
            response.Season.EndAtMs = 1; reply.SetResult(ClaimResult()); Done(task);
            Check(PassCommands.NeedsRefresh, "Expired season must not become fresh, including expiry during claim");
        }
    }
    static void ExplicitInvalidationStaysDirty()
    {
        foreach (bool duringClaim in new[] { false, true })
        {
            Reset(); Seed(); if (!duringClaim) PassCommands.Invalidate();
            var reply = ServerSaveCommands.QueueClaim(); var task = PassCommands.ClaimAsync(1);
            if (duringClaim) PassCommands.Invalidate();
            reply.SetResult(ClaimResult()); Done(task);
            Check(PassCommands.NeedsRefresh && PassManager.IsClaimed(1), "Independent invalidation must survive successful progress adoption");
        }
    }
    static void RankChangeStaysDirty()
    {
        foreach (bool duringClaim in new[] { false, true })
        {
            Reset(); Seed(); if (!duringClaim) RankManager.Points++;
            var reply = ServerSaveCommands.QueueClaim(); var task = PassCommands.ClaimAsync(1);
            if (duringClaim) RankManager.Points++;
            reply.SetResult(ClaimResult()); Done(task);
            Check(PassCommands.NeedsRefresh, "Claim cannot refresh rank-dependent pack choice baseline");
        }
    }
    static void OldReadCannotUndoProgress()
    {
        foreach (bool mission in new[] { false, true })
        {
            Reset(); Seed(); var read = ServerSaveCommands.QueueRead(); var readTask = PassCommands.RefreshAsync();
            if (mission) PassCommands.ApplyMissionProgress(Progress()); else ClaimSucceeds();
            read.SetResult(Response()); Check(!Done(readTask), "Read started before mutation must be rejected");
            Check(PassManager.Exp == 200 && !PassCommands.NeedsRefresh, "Old read must not overwrite progress or dirty valid reused definitions");
        }
    }
    static void OverlapRemainsConservative()
    {
        foreach (bool reverse in new[] { false, true })
        {
            Reset(); Seed(); var first = ServerSaveCommands.QueueClaim(); var second = ServerSaveCommands.QueueClaim();
            var firstTask = PassCommands.ClaimAsync(1); var secondTask = PassCommands.ClaimAsync(2);
            Check(!Done(PassCommands.RefreshAsync()), "Query must remain blocked during claims");
            if (reverse) { second.SetResult(ClaimResult(2)); first.SetResult(ClaimResult(1)); }
            else { first.SetResult(ClaimResult(1)); second.SetResult(ClaimResult(2)); }
            Done(firstTask); Done(secondTask);
            Check(PassCommands.NeedsRefresh, "Unordered overlapping claims require canonical requery");
        }
        Reset(); Seed(); var reply = ServerSaveCommands.QueueClaim(); var task = PassCommands.ClaimAsync(1);
        PassCommands.ApplyMissionProgress(Progress(300)); reply.SetResult(ClaimResult()); Done(task);
        Check(PassCommands.NeedsRefresh, "Mission overlapping a claim must not incorrectly restore freshness");
    }
    static void MissionFallbackFetchesDefinitions()
    {
        for (int variant = 0; variant < 3; variant++)
        {
            Reset(); var response = Response(); if (variant == 2) response.Levels.Clear(); Seed(response);
            var read = ServerSaveCommands.QueueRead();
            PassCommands.ApplyMissionProgress(variant == 0 ? null : Progress(200, variant == 1 ? "season-2" : "season-1"));
            Check(PassCommands.NeedsRefresh && ServerSaveCommands.ReadCalls == 2,
                "Null/mismatched progress or missing definitions must start existing background refresh");
            read.SetResult(Response()); Check(!PassCommands.NeedsRefresh, "Fallback query must recover cache");
        }
    }
    static void MissionCannotEraseIndependentInvalidation()
    {
        for (int variant = 0; variant < 3; variant++)
        {
            Reset(); Seed();
            if (variant == 0) PassCommands.Invalidate();
            else if (variant == 1) RankManager.Points++;
            else PassManager.Season.EndAtMs = 1;
            PassCommands.ApplyMissionProgress(Progress());
            Check(PassCommands.NeedsRefresh && PassManager.Exp == 200,
                "Mission progress may update display but cannot validate independently stale definitions");
        }
    }
    static void Run(string name, Action test)
    {
        Reset();
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    static int Main()
    {
        Run("fresh claims skip panel reentry queries", FreshClaimSkipsReentry);
        Run("fresh mission progress skips query and updates display", FreshMissionSkipsReentry);
        Run("failed and null claims remain dirty", FailedClaimsStayDirty);
        Run("invalid progress preserves previous season state", InvalidProgressDoesNotOverwrite);
        Run("missing definitions cannot become fresh", MissingDefinitionsStayDirty);
        Run("expiry before or during claim prevents freshness", ExpiredSeasonStaysDirty);
        Run("explicit invalidation survives claim", ExplicitInvalidationStaysDirty);
        Run("rank change requires new pack choice query", RankChangeStaysDirty);
        Run("older reads cannot undo adopted progress", OldReadCannotUndoProgress);
        Run("overlapping claims and missions stay conservative", OverlapRemainsConservative);
        Run("mission fallback loads missing season definitions", MissionFallbackFetchesDefinitions);
        Run("mission does not erase existing invalidation", MissionCannotEraseIndependentInvalidation);
        Console.WriteLine($"{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
