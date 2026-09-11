using System;
using Newtonsoft.Json;

public static class Tests
{
    static int assertions;
    static int changes;
    static bool canPersist;
    static ContentUnlockSaveData Slot => DataSaveManager.Data.Profile.ContentUnlocks;

    public static void Main()
    {
        ContentUnlockManager.OnChanged += () => changes++;
        Run(nameof(AndBoundaries), AndBoundaries);
        Run(nameof(MissingData), MissingData);
        Run(nameof(SilentSeedAndFtueGraduation), SilentSeedAndFtueGraduation);
        Run(nameof(PermanentRankAndLevelUnlock), PermanentRankAndLevelUnlock);
        Run(nameof(LegacyMigration), LegacyMigration);
        Run(nameof(ServerAdoptionDefersMutation), ServerAdoptionDefersMutation);
        Run(nameof(PresentationDefersPersistence), PresentationDefersPersistence);
        Run(nameof(AccountSessionIsolation), AccountSessionIsolation);
        Run(nameof(DebugResetReevaluates), DebugResetReevaluates);
        Run(nameof(DebugResetDefersDuringServerAdoption), DebugResetDefersDuringServerAdoption);
        Run(nameof(DeferredDebugResetCannotCrossAccounts), DeferredDebugResetCannotCrossAccounts);
        Run(nameof(ProfileJsonRoundTrip), ProfileJsonRoundTrip);
        ContentUnlockManager.ResetSession();
        Console.WriteLine($"PASS: 12 scenarios, {assertions} assertions; actual manager, rules, save and profile sources.");
    }

    static void Run(string name, Action test)
    {
        Reset(); test(); Console.WriteLine("PASS " + name);
    }

    static void Reset()
    {
        ContentUnlockManager.ResetSession();
        DataSaveManager.Data = new TestUserSave();
        DataSaveManager.SaveCount = 0;
        OutgameFeatureLock.Notifications = 0;
        changes = 0; canPersist = true;
        OutgameTutorialProgress.IsCompleted = false;
        RankManager.IsConfigured = true; RankManager.IsRanked = true; RankManager.BestTierIndex = 0;
        AccountLevelManager.IsConfigured = true; AccountLevelManager.Level = 1;
        ContentUnlockConfig.IsReady = true;
        ContentUnlockConfig.Rules = new[]
        {
            new ContentUnlockRule("Mission", ftue: true),
            new ContentUnlockRule("Adventure", grade: 1, division: 1),
            new ContentUnlockRule("Roulette"),
        };
    }

    static void Init() => ContentUnlockManager.Initialize(() => canPersist);
    static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }

    static void AndBoundaries()
    {
        var rule = new ContentUnlockRule("Mission", true, 1, 1, 5);
        var missing = ContentUnlockRules.Evaluate(rule, false, true, true, 2, 3, true, 4);
        Check(missing.Missing == (EContentUnlockRequirement.Ftue | EContentUnlockRequirement.Rank | EContentUnlockRequirement.AccountLevel), "AND reports all unmet conditions");
        Check(!ContentUnlockRules.Evaluate(rule, false, true, true, 3, 3, true, 5).IsUnlocked, "FTUE required at exact rank/level");
        Check(!ContentUnlockRules.Evaluate(rule, true, true, true, 2, 3, true, 5).IsUnlocked, "Rank below threshold");
        Check(!ContentUnlockRules.Evaluate(rule, true, true, true, 3, 3, true, 4).IsUnlocked, "Level below threshold");
        Check(!ContentUnlockRules.Evaluate(rule, true, true, false, 3, 3, true, 5).IsUnlocked, "Unranked cannot meet rank condition");
        Check(ContentUnlockRules.Evaluate(rule, true, true, true, 3, 3, true, 5).IsUnlocked, "Exact AND boundary unlocks");
    }

    static void MissingData()
    {
        ContentUnlockConfig.IsReady = false;
        Init();
        Check(DataSaveManager.SaveCount == 0 && Slot.Version == 0, "Missing spec must not seed");
        Check(ContentUnlockManager.Evaluate("Mission").Missing == EContentUnlockRequirement.Data, "Missing spec reports Data");
        ContentUnlockConfig.IsReady = true;
        RankManager.IsConfigured = false;
        ContentUnlockManager.FlushPending();
        Check(!ContentUnlockManager.IsUnlocked("Adventure"), "Unloaded rank cannot permanently unlock");
        Check(ContentUnlockManager.Evaluate("Adventure").Missing == EContentUnlockRequirement.Data, "Rank readiness reported");
        var rule = new ContentUnlockRule("Mission", level: 5);
        Check(ContentUnlockRules.Evaluate(rule, true, true, true, 0, -1, false, 99).Missing == EContentUnlockRequirement.Data, "Level readiness wins numeric level");
        Check(ContentUnlockManager.Evaluate("Missing").Missing == EContentUnlockRequirement.Data, "Unknown content stays locked");
    }

    static void SilentSeedAndFtueGraduation()
    {
        Init();
        Check(ContentUnlockManager.IsUnlocked("Roulette") && Slot.Version == 1, "Initial always-open seed");
        Check(ContentUnlockManager.PendingKeys.Count == 0, "Seed is silent");
        Check(!ContentUnlockManager.IsUnlocked("Mission"), "FTUE incomplete mission locked");
        OutgameTutorialProgress.IsCompleted = true;
        DataSaveManager.NotifySaved();
        OutgameTutorialRunner.Notify();
        Check(ContentUnlockManager.IsUnlocked("Mission"), "Saved graduation unlocks");
        Check(ContentUnlockManager.PendingKeys.Count == 1 && ContentUnlockManager.IsPending("Mission"), "Repeated events enqueue once");
        Check(ContentUnlockManager.TryGetKey(EOutgameFeature.Mission, out var key) && key == "Mission", "Feature mapping");
        Check(!ContentUnlockManager.TryGetKey(EOutgameFeature.Other, out _), "Unmapped feature preserves legacy path");
    }

    static void PermanentRankAndLevelUnlock()
    {
        ContentUnlockConfig.Rules = new[] { new ContentUnlockRule("Adventure", grade: 1, division: 1, level: 5) };
        Init();
        RankManager.BestTierIndex = 3; RankManager.Notify();
        Check(!ContentUnlockManager.IsUnlocked("Adventure"), "Rank alone insufficient");
        AccountLevelManager.Level = 5; AccountLevelManager.Notify();
        Check(ContentUnlockManager.IsUnlocked("Adventure") && ContentUnlockManager.IsPending("Adventure"), "Level event meets combined boundary");
        ContentUnlockConfig.Rules = new[] { new ContentUnlockRule("Adventure", true, 1, 3, 50) };
        RankManager.BestTierIndex = 0; AccountLevelManager.Level = 1;
        ContentUnlockManager.RequestRefresh();
        Check(ContentUnlockManager.Evaluate("Adventure").IsUnlocked, "Raised policy and lowered facts never relock permanent mark");
        Check(Slot.Unlocked.Count == 1 && Slot.Pending.Count == 1, "No duplicate permanent or pending entries");
    }

    static void LegacyMigration()
    {
        ContentUnlockConfig.Rules = new[]
        {
            new ContentUnlockRule("Mission", true, 1, 3, 99),
            new ContentUnlockRule("Adventure", grade: 1, division: 3, level: 99),
        };
        OutgameTutorialProgress.IsCompleted = true;
        DataSaveManager.Data.Tutorial.AdventureUnlocked = true;
        Init();
        Check(ContentUnlockManager.IsUnlocked("Mission") && ContentUnlockManager.IsUnlocked("Adventure"), "Legacy FTUE/adventure survive higher requirements");
        Check(Slot.Pending.Count == 0 && Slot.Version == 1, "Legacy migration silent and versioned");
    }

    static void ServerAdoptionDefersMutation()
    {
        Init();
        canPersist = false;
        int savesBefore = DataSaveManager.SaveCount, changesBefore = changes, notificationsBefore = OutgameFeatureLock.Notifications;
        OutgameTutorialProgress.IsCompleted = true;
        ContentUnlockManager.NotifyRehydrated();
        RankManager.Notify(); AccountLevelManager.Notify(); DataSaveManager.NotifySaved();
        ContentUnlockManager.FlushPending();
        Check(!Slot.Unlocked.Contains("Mission") && Slot.Pending.Count == 0, "Sealed server adoption must not mutate profile");
        Check(DataSaveManager.SaveCount == savesBefore && changes == changesBefore && OutgameFeatureLock.Notifications == notificationsBefore, "Sealed adoption does not save or notify");
        canPersist = true;
        ContentUnlockManager.FlushPending();
        Check(ContentUnlockManager.IsPending("Mission") && ContentUnlockManager.IsUnlocked("Mission"), "Resume flush applies deferred graduation");
        Check(DataSaveManager.SaveCount == savesBefore + 1 && changes == changesBefore + 1, "Resume flush saves and notifies once");
    }

    static void PresentationDefersPersistence()
    {
        Init(); OutgameTutorialProgress.IsCompleted = true; OutgameTutorialRunner.Notify();
        canPersist = false;
        int savesBefore = DataSaveManager.SaveCount;
        ContentUnlockManager.MarkPresented("Mission");
        Check(!ContentUnlockManager.IsPending("Mission") && ContentUnlockManager.PendingKeys.Count == 0, "Presented hidden immediately while sealed");
        Check(Slot.Pending.Contains("Mission") && DataSaveManager.SaveCount == savesBefore, "Stored pending unchanged while sealed");
        canPersist = true; ContentUnlockManager.FlushPending();
        Check(!Slot.Pending.Contains("Mission") && DataSaveManager.SaveCount == savesBefore + 1, "Flush persists presentation removal");
        ContentUnlockManager.MarkPresented("Mission");
        Check(DataSaveManager.SaveCount == savesBefore + 1, "Duplicate acknowledgment is inert");
    }

    static void AccountSessionIsolation()
    {
        Init(); OutgameTutorialProgress.IsCompleted = true; OutgameTutorialRunner.Notify();
        canPersist = false; ContentUnlockManager.MarkPresented("Mission");
        int epoch = ContentUnlockManager.SessionVersion;
        ContentUnlockManager.ResetSession();
        Check(ContentUnlockManager.SessionVersion != epoch && ContentUnlockManager.PendingKeys.Count == 0, "Reset advances epoch and hides previous account");
        DataSaveManager.Data = new TestUserSave();
        Slot.Version = 1; Slot.Unlocked.Add("Mission"); Slot.Pending.Add("Mission");
        canPersist = true; Init();
        Check(ContentUnlockManager.IsPending("Mission"), "Previous account presentation cannot acknowledge new account");
        Check(ContentUnlockManager.SessionVersion != epoch, "New account has distinct epoch");
    }

    static void DebugResetReevaluates()
    {
        OutgameTutorialProgress.IsCompleted = true; DataSaveManager.Data.Tutorial.AdventureUnlocked = true; Init();
        OutgameTutorialProgress.IsCompleted = false;
        ContentUnlockManager.ResetForDebug();
        Check(!ContentUnlockManager.IsUnlocked("Mission") && !ContentUnlockManager.IsUnlocked("Adventure"), "Debug rewind removes permanent legacy unlocks");
        Check(!DataSaveManager.Data.Tutorial.AdventureUnlocked && Slot.Version == 1, "Debug reset cannot remigrate legacy");
        Check(ContentUnlockManager.IsUnlocked("Roulette"), "Debug reset reevaluates always-open rule");
    }

    static void ProfileJsonRoundTrip()
    {
        Init(); OutgameTutorialProgress.IsCompleted = true; OutgameTutorialRunner.Notify();
        DataSaveManager.Data.Profile.Nickname = "tester";
        DataSaveManager.Data.Profile.AccountExp = 9876543210L;
        string json = JsonConvert.SerializeObject(DataSaveManager.Data.Profile);
        var restored = JsonConvert.DeserializeObject<ProfileSaveData>(json);
        Check(restored.AccountExp == 9876543210L && restored.Nickname == "tester", "Existing profile survives Newtonsoft round trip");
        Check(restored.ContentUnlocks.Version == 1 && restored.ContentUnlocks.Unlocked.Contains("Mission") && restored.ContentUnlocks.Pending.Contains("Mission"), "Nested unlock state survives JSON round trip");
        var legacy = JsonConvert.DeserializeObject<ProfileSaveData>("{\"Nickname\":\"old\"}");
        Check(legacy.ContentUnlocks != null && legacy.ContentUnlocks.Version == 0, "Legacy JSON receives migratable default");
        DataSaveManager.Data.Profile = restored;
        ContentUnlockManager.NotifyRehydrated(); ContentUnlockManager.FlushPending();
        Check(ContentUnlockManager.IsUnlocked("Mission") && ContentUnlockManager.IsPending("Mission"), "Restored profile retains unlock and pending");
    }

    static void DebugResetDefersDuringServerAdoption()
    {
        OutgameTutorialProgress.IsCompleted = true;
        DataSaveManager.Data.Tutorial.AdventureUnlocked = true;
        Init();
        OutgameTutorialProgress.IsCompleted = false;
        canPersist = false;
        var original = Slot;
        int savesBefore = DataSaveManager.SaveCount, changesBefore = changes;
        int notificationsBefore = OutgameFeatureLock.Notifications;
        ContentUnlockManager.ResetForDebug();
        ContentUnlockManager.FlushPending();
        Check(ReferenceEquals(original, Slot) && Slot.Unlocked.Contains("Mission") && Slot.Unlocked.Contains("Adventure"), "Sealed debug reset preserves profile slot and marks");
        Check(DataSaveManager.Data.Tutorial.AdventureUnlocked, "Sealed debug reset preserves legacy tutorial mark");
        Check(DataSaveManager.SaveCount == savesBefore && changes == changesBefore && OutgameFeatureLock.Notifications == notificationsBefore, "Sealed debug reset neither saves nor notifies");
        canPersist = true;
        ContentUnlockManager.FlushPending();
        Check(!ReferenceEquals(original, Slot) && !Slot.Unlocked.Contains("Mission") && !Slot.Unlocked.Contains("Adventure"), "Safe flush applies deferred debug reset");
        Check(!DataSaveManager.Data.Tutorial.AdventureUnlocked && Slot.Version == 1 && ContentUnlockManager.IsUnlocked("Roulette"), "Safe debug flush clears legacy and reevaluates rules");
        Check(DataSaveManager.SaveCount == savesBefore + 1 && changes == changesBefore + 1, "Safe debug flush saves and notifies once");
    }

    static void DeferredDebugResetCannotCrossAccounts()
    {
        Init();
        canPersist = false;
        ContentUnlockManager.ResetForDebug();
        ContentUnlockManager.ResetSession();
        DataSaveManager.Data = new TestUserSave();
        Slot.Version = 1;
        Slot.Unlocked.Add("Mission");
        DataSaveManager.Data.Tutorial.AdventureUnlocked = true;
        canPersist = true;
        Init();
        Check(ContentUnlockManager.IsUnlocked("Mission"), "Previous account debug request cannot erase new account permanent mark");
        Check(DataSaveManager.Data.Tutorial.AdventureUnlocked, "Previous account debug request cannot clear new account legacy state");
    }
}
