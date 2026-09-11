using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType value) { }
    }
}

namespace Firebase.Firestore
{
    public enum UnknownPropertyHandling { Ignore }
    public sealed class FirestoreDataAttribute : Attribute
    {
        public UnknownPropertyHandling UnknownPropertyHandling { get; set; }
    }
    public sealed class FirestorePropertyAttribute : Attribute
    {
        public FirestorePropertyAttribute(string name) { }
    }
}

public enum EOutgameFeature { Mission, Adventure, Roulette, Other }
public enum ESaveUploadTiming { Default }
public enum ERankGrade { Bronze, Silver }
public struct RankTier
{
    public ERankGrade Grade;
    public int Division;
    public int Index;
}
public readonly struct ContentUnlockRule
{
    public string ContentKey { get; }
    public bool RequireFtue { get; }
    public bool RequireRank { get; } public ERankGrade MinRankGrade { get; }
    public int MinRankDivision { get; }
    public int MinAccountLevel { get; }
    public ContentUnlockRule(string key, bool ftue = false, int grade = -1, int division = 0, int level = 0)
    {
        ContentKey = key; RequireFtue = ftue; RequireRank = grade >= 0; MinRankGrade = grade >= 0 ? (ERankGrade)grade : ERankGrade.Bronze;
        MinRankDivision = division; MinAccountLevel = level;
    }
}
public static class ContentUnlockConfig
{
    public static bool IsReady = true;
    public static IReadOnlyList<ContentUnlockRule> Rules = Array.Empty<ContentUnlockRule>();
    public static bool TryGet(string key, out ContentUnlockRule rule)
    {
        if (IsReady)
            foreach (var item in Rules)
                if (item.ContentKey == key) { rule = item; return true; }
        rule = default; return false;
    }
}
public class TestUserSave
{
    public ProfileSaveData Profile = new ProfileSaveData();
    public TestTutorialSave Tutorial = new TestTutorialSave();
}
public class TestTutorialSave { public bool AdventureUnlocked; }
public static class DataSaveManager
{
    public static TestUserSave Data = new TestUserSave();
    public static int SaveCount;
    public static event Action<ESaveUploadTiming> OnSaved;
    public static void Save() { SaveCount++; OnSaved?.Invoke(ESaveUploadTiming.Default); }
    public static void NotifySaved() => OnSaved?.Invoke(ESaveUploadTiming.Default);
}
public static class RankManager
{
    public static bool IsConfigured = true;
    public static bool IsRanked = true;
    public static int BestTierIndex;
    public static event Action OnChanged;
    public static void Notify() => OnChanged?.Invoke();
    public static bool TryGetTier(int index, out RankTier tier)
    {
        tier = new RankTier { Index = index, Grade = index < 3 ? ERankGrade.Bronze : ERankGrade.Silver, Division = index % 3 + 1 };
        return index >= 0 && index < 6;
    }
}
public static class AccountLevelManager
{
    public static bool IsConfigured = true;
    public static int Level = 1;
    public static event Action OnChanged;
    public static void Notify() => OnChanged?.Invoke();
}
public static class OutgameTutorialProgress { public static bool IsCompleted; }
public static class OutgameTutorialRunner
{
    public static event Action OnGuidedChanged;
    public static void Notify() => OnGuidedChanged?.Invoke();
}
public static class OutgameFeatureLock
{
    public static int Notifications;
    public static void NotifyContentChanged() => Notifications++;
}
