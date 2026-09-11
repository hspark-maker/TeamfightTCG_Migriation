using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string text) { } }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }
}
public sealed class RankGrade { public string gradeKey; }
public sealed class AccountLevel { }
public enum ERankGrade { Bronze, Silver, Gold, Platinum, Diamond }
public enum EOutgameFeature { Mission, Adventure, Roulette, Deck }
public static class RankConfig { public const int DivisionsPerGrade = 4; }
public sealed class Table<T> { public IReadOnlyList<T> All; }
public sealed class SpecDataManager
{
    public Table<RankGrade> RankGrade;
    public Table<AccountLevel> AccountLevel;
}
public static class SpecSource { public static SpecDataManager Manager; }
public static class ContentUnlockManager
{
    public static bool TryGetKey(EOutgameFeature feature, out string key)
    {
        key = feature == EOutgameFeature.Deck || !Enum.IsDefined(typeof(EOutgameFeature), feature) ? null : feature.ToString();
        return key != null;
    }
}

static class Tests
{
    static readonly RankGrade[] Ranks = { new RankGrade { gradeKey = "Bronze" } };
    static int checks;
    static ContentUnlockDef[] Rows() => new[] {
        new ContentUnlockDef { feature = EOutgameFeature.Mission, requireFtue = true },
        new ContentUnlockDef { feature = EOutgameFeature.Adventure, requireRank = true, minRankGrade = ERankGrade.Bronze, minRankDivision = 2 },
        new ContentUnlockDef { feature = EOutgameFeature.Roulette },
    };
    static void Check(bool success, string name)
    {
        if (!success) throw new Exception(name);
        checks++;
    }
    static void Invalid(Action<ContentUnlockDef[]> mutate, string name)
    {
        var rows = Rows(); mutate(rows);
        Check(!ContentUnlockConfig.TryValidate(rows, out string error) && !string.IsNullOrEmpty(error), name);
    }
    static void Main()
    {
        Check(!ContentUnlockConfig.TryValidateProgression(out _), "progression requires injection");
        Check(ContentUnlockConfig.TryValidate(Rows(), out _), "initial SO definitions");
        Check(!ContentUnlockConfig.TryValidate(null, out _), "missing definitions");
        Check(!ContentUnlockConfig.TryValidate(Array.Empty<ContentUnlockDef>(), out _), "empty definitions");
        Check(!ContentUnlockConfig.TryValidate(new[] { Rows()[0] }, out _), "missing required content");
        Invalid(r => r[1].feature = r[0].feature, "duplicate feature");
        Invalid(r => r[0].feature = EOutgameFeature.Deck, "unsupported feature");
        Invalid(r => r[0].feature = (EOutgameFeature)999, "unknown feature");
        Invalid(r => r[0] = null, "null definition");
        Invalid(r => r[0].minRankDivision = 1, "unused rank division");
        Invalid(r => r[1].minRankGrade = (ERankGrade)999, "invalid rank enum");
        Invalid(r => r[1].minRankGrade = (ERankGrade)(-2), "invalid rank sentinel");
        Invalid(r => r[1].minRankDivision = 0, "rank division low");
        Invalid(r => r[1].minRankDivision = 5, "rank division high");
        Invalid(r => r[0].minAccountLevel = -1, "negative account level");

        var authored = Rows();
        Check(ContentUnlockConfig.TrySetSource(authored, out _) && ContentUnlockConfig.IsReady && ContentUnlockConfig.Rules.Count == 3, "SO source injection");
        Check(ContentUnlockConfig.TryGet("Mission", out var rule) && rule.RequireFtue && !rule.RequireRank, "rule projection");
        Check(!ContentUnlockConfig.TryGet("mission", out _), "stable key case");
        authored[0].requireFtue = false;
        Check(ContentUnlockConfig.TryGet("Mission", out rule) && rule.RequireFtue, "runtime rules copied from SO");
        Check(ContentUnlockConfig.TryGet(authored, "Mission", out rule) && !rule.RequireFtue, "editor lookup uses supplied SO independent of runtime source");
        Check(!ContentUnlockConfig.TryGet((IReadOnlyList<ContentUnlockDef>)null, "Mission", out _), "null editor source");
        Check(!ContentUnlockConfig.TryGet(new ContentUnlockDef[] { null }, "Mission", out _), "null editor item");
        Check(!ContentUnlockConfig.TryGet(authored, "mission", out _), "editor keys case-sensitive");
        Check(!ContentUnlockConfig.TryValidateProgression(out _), "missing rank table rejected");

        SpecSource.Manager = new SpecDataManager {
            RankGrade = new Table<RankGrade> { All = Ranks },
            AccountLevel = new Table<AccountLevel> { All = new AccountLevel[10] }
        };
        Check(ContentUnlockConfig.TryValidateProgression(out _), "valid remote progression tables");
        var boundary = Rows(); boundary[0].minAccountLevel = 10;
        Check(ContentUnlockConfig.TrySetSource(boundary, out _) && ContentUnlockConfig.TryValidateProgression(out _), "level upper boundary");
        boundary[0].minAccountLevel = 11;
        Check(ContentUnlockConfig.TrySetSource(boundary, out _) && !ContentUnlockConfig.TryValidateProgression(out _), "level beyond remote table");
        boundary = Rows(); boundary[1].minRankGrade = ERankGrade.Silver;
        Check(ContentUnlockConfig.TrySetSource(boundary, out _) && !ContentUnlockConfig.TryValidateProgression(out _), "defined rank missing from remote table");
        boundary = Rows(); boundary[0].minAccountLevel = 1;
        ContentUnlockConfig.TrySetSource(boundary, out _);
        SpecSource.Manager.AccountLevel = null;
        Check(!ContentUnlockConfig.TryValidateProgression(out _), "required level table unavailable");
        boundary[0].minAccountLevel = 0;
        ContentUnlockConfig.TrySetSource(boundary, out _);
        Check(ContentUnlockConfig.TryValidateProgression(out _), "unused level condition needs no table");
        Check(!ContentUnlockConfig.TrySetSource(Array.Empty<ContentUnlockDef>(), out _) && !ContentUnlockConfig.IsReady && ContentUnlockConfig.Rules.Count == 0 && !ContentUnlockConfig.TryGet("Mission", out _), "invalid replacement cannot expose previous runtime rules");
        Check(ContentUnlockConfig.TryGet(Rows(), "Mission", out rule) && rule.RequireFtue, "editor lookup works with uninitialized runtime");
        Console.WriteLine("Content unlock SO config tests passed: " + checks);
    }
}
