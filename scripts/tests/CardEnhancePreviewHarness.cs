using System;
using System.Collections.Generic;

public enum CardKeyword { None, Ranged }
public enum ECurrencyType { Shard }
public sealed class CardSpec
{
    public const int BaseGrowthLevel = 1, MaxEvolutionStage = 2;
    public int KeywordUnlockLevel => 2;
    public CardKeyword Keywords => CardKeyword.Ranged;
    public bool TryGetHpGain(int level, out int gain)
    {
        gain = level == 2 ? 20 : level == 3 ? 40 : level == 4 ? 60 : 0;
        return gain > 0;
    }
}
public static class CardCatalog
{
    public static CardSpec RequireSpec(int id) => new CardSpec();
}
public struct EnhanceCost
{
    public ECurrencyType Currency;
    public long Cost;
    public float SuccessRate;
}
public static class GrowthSpec
{
    public const int CardMaxLevelCeiling = 4;
    public static int CardMaxLevel = 4;
    public static bool TryGetCardEnhanceCost(int level, out EnhanceCost cost)
    {
        cost = new EnhanceCost { Cost = (level - 1) * 100, SuccessRate = 1 };
        return level >= 2 && level <= CardMaxLevel;
    }
}
public sealed class CardGrowthEntry { public int Level, ShardProgress; }
public static class OutgameTutorialGuide
{
    public static bool Free, Synergy;
    public static bool HasFreeCardEnhance(int id) => Free;
    public static bool CanUseFreeSynergyGrowth(int id) => Synergy;
}
public static class CardGrowthManager
{
    static readonly Dictionary<int, CardGrowthEntry> s_growth = new Dictionary<int, CardGrowthEntry>();
    public static int ClampLevel(int level) => Math.Clamp(level, 1, 4);
    public static CardGrowth PreviewGrowthAtLevel(int id, int level) => Snapshot(id, ClampLevel(level));

    /* PRODUCTION_METHODS */

    static int s_checks;
    static void Check(string label, int level, int progress, int amount, int expectedLevel, int expectedHp,
        int expectedStage = 0, bool free = false, bool synergy = false)
    {
        var entry = new CardGrowthEntry { Level = level, ShardProgress = progress };
        s_growth[1] = entry;
        OutgameTutorialGuide.Free = free;
        OutgameTutorialGuide.Synergy = synergy;
        CardGrowth after = PreviewGrowthAfterEnhance(1, amount);
        CardKeyword expectedKeyword = expectedLevel >= 2 ? CardKeyword.Ranged : CardKeyword.None;
        if (after.Level != expectedLevel || after.HpBonus != expectedHp || after.EvolutionStage != expectedStage
            || after.UnlockedKeywords != expectedKeyword || after.SynergyUnlocked != (expectedStage > 0))
            throw new Exception($"{label}: got level={after.Level}, hp={after.HpBonus}, stage={after.EvolutionStage}, keyword={after.UnlockedKeywords}");
        if (s_growth.Count != 1 || !ReferenceEquals(s_growth[1], entry) || entry.Level != level || entry.ShardProgress != progress)
            throw new Exception(label + ": preview mutated saved growth");
        if (amount == 1 && PreviewGrowthAfterEnhance(1).HpBonus != after.HpBonus)
            throw new Exception(label + ": one-shard overload mismatch");
        s_checks++;
    }

    public static void Main()
    {
        Check("one shard floors fractional HP", 1, 0, 1, 1, 0);
        Check("existing progress plus selected shards", 1, 20, 30, 1, 10);
        Check("one below upgrade", 1, 20, 79, 1, 19);
        Check("exact upgrade unlocks keyword", 1, 20, 80, 2, 20);
        Check("extra shards stop at next level", 1, 20, 150, 2, 20);
        Check("request capped at 150", 2, 0, int.MaxValue, 2, 50);
        Check("remaining cap evolves", 2, 180, int.MaxValue, 3, 60, 1);
        Check("second evolution", 3, 290, 10, 4, 120, 2);
        Check("zero request", 2, 100, 0, 2, 40);
        Check("negative request", 2, 100, int.MinValue, 2, 40);
        Check("max level", 4, 0, 150, 4, 120, 2);
        Check("free ordinary upgrade", 1, 0, 1, 2, 20, free: true);
        Check("free synergy evolution", 1, 0, 1, 3, 60, 1, free: true, synergy: true);
        Check("free still needs positive request", 1, 0, 0, 1, 0, free: true, synergy: true);
        GrowthSpec.CardMaxLevel = 0;
        Check("unavailable growth rule", 1, 0, 150, 1, 0);
        if (PreviewGrowthAfterEnhance(0, 150).Level != CardGrowth.BaseLevel)
            throw new Exception("invalid card should remain at base growth");
        Console.WriteLine($"Card enhancement preview: {s_checks + 1} boundary checks passed; growth entries unchanged.");
    }
}
