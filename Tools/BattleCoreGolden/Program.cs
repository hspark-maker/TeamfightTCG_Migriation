using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

internal static class Program
{
    static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    static int Main(string[] _args)
    {
        string t_repoRoot = ResolveRepoRoot(_args.Length > 0 ? _args[0] : Directory.GetCurrentDirectory());
        if (t_repoRoot == null)
        {
            Console.Error.WriteLine("저장소 루트를 찾지 못했다. 첫 인자로 경로를 넘길 것.");
            return 2;
        }

        string t_goldenRoot = Path.Combine(t_repoRoot, "functions", "testdata", "golden");
        string[] t_files = Directory.GetFiles(t_goldenRoot, "*.json").OrderBy(_path => _path).ToArray();
        int t_passed = 0;
        int t_failed = 0;
        int t_skipped = 0;

        foreach (string t_file in t_files)
        {
            string t_name = Path.GetFileName(t_file);
            try
            {
                GoldenDocument t_golden = JsonSerializer.Deserialize<GoldenDocument>(
                    File.ReadAllText(t_file), JsonOptions)
                    ?? throw new InvalidOperationException("JSON 문서가 null이다.");

                if (!t_golden.Eligible)
                {
                    Console.WriteLine($"SKIP {t_name}: {t_golden.ExclusionReason}");
                    t_skipped++;
                    continue;
                }
                if (t_golden.Decks == null || t_golden.Decks.Any(
                        _deck => _deck.BoardOrder == null || _deck.BoardOrder.Count == 0))
                {
                    Console.WriteLine($"SKIP {t_name}: boardOrder 없음");
                    t_skipped++;
                    continue;
                }

                SynergyRuleProvider.Install(GoldenRuleProvider.Create(t_repoRoot, t_golden.CardSpecs));
                BattleReplayResult t_result = BattleReplay.Run(BuildInput(t_golden));
                List<string> t_errors = Compare(t_golden, t_result);
                if (t_errors.Count == 0)
                {
                    Console.WriteLine($"PASS {t_name}");
                    t_passed++;
                }
                else
                {
                    Console.WriteLine($"FAIL {t_name}: {string.Join("; ", t_errors)}");
                    t_failed++;
                }
            }
            catch (Exception t_exception)
            {
                Console.WriteLine($"FAIL {t_name}: {t_exception.GetType().Name}: {t_exception.Message}");
                t_failed++;
            }
            finally
            {
                SynergyRuleProvider.Reset();
                MatchRandom.Reset();
                BattleRuleBridge.Reset();
            }
        }

        Console.WriteLine($"RESULT pass={t_passed} fail={t_failed} skip={t_skipped} total={t_files.Length}");
        return t_failed == 0 ? 0 : 1;
    }

    static BattleReplayInput BuildInput(GoldenDocument _golden)
    {
        var t_decks = new List<BattleReplayDeck>(_golden.Decks.Count);
        for (int i = 0; i < _golden.Decks.Count; i++)
        {
            GoldenDeck t_source = _golden.Decks[i];
            var t_cards = new List<BattleReplayCard>(t_source.Cards.Count);
            for (int j = 0; j < t_source.Cards.Count; j++)
            {
                GoldenCardSnapshot t_card = t_source.Cards[j];
                t_cards.Add(new BattleReplayCard(t_card.CardId,
                    new CardGrowth(t_card.Level, t_card.HpBonus, t_card.EvolutionStage,
                        (CardKeyword)t_card.UnlockedKeywords, t_card.SynergyUnlocked)));
            }
            t_decks.Add(new BattleReplayDeck
            {
                OwnerIndex = t_source.OwnerIndex,
                Cards = t_cards,
                BoardOrder = t_source.BoardOrder,
            });
        }

        string t_seed = (_golden.SeedHex ?? string.Empty).StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? _golden.SeedHex.Substring(2) : _golden.SeedHex;
        return new BattleReplayInput
        {
            Seed = ulong.Parse(t_seed, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
            Decks = t_decks,
            CommandLog = Convert.FromBase64String(_golden.CommandLog ?? string.Empty),
        };
    }

    static List<string> Compare(GoldenDocument _golden, BattleReplayResult _actual)
    {
        var t_errors = new List<string>();
        if (!_actual.Ok)
        {
            t_errors.Add("replay=" + _actual.Reason);
            return t_errors;
        }
        if (_actual.FirstOwner != _golden.FirstOwner)
            t_errors.Add($"firstOwner {_actual.FirstOwner}!={_golden.FirstOwner}");
        string t_hash = _actual.FinalStateHash.ToString("x16", CultureInfo.InvariantCulture);
        if (!string.Equals(t_hash, _golden.FinalStateHash, StringComparison.OrdinalIgnoreCase))
            t_errors.Add($"hash {t_hash}!={_golden.FinalStateHash}");
        if (_actual.DrawCount != _golden.FinalDrawCount)
            t_errors.Add($"draws {_actual.DrawCount}!={_golden.FinalDrawCount}");
        if (_golden.Remaining == null || _golden.Remaining.Length != 2
            || !_actual.Remaining.SequenceEqual(_golden.Remaining))
            t_errors.Add($"remaining [{string.Join(",", _actual.Remaining)}]" +
                         $"!=[{string.Join(",", _golden.Remaining ?? Array.Empty<int>())}]");
        if (_golden.SchemaVersion >= 2)
        {
            if (_actual.WinnerOwner != _golden.WinnerOwner)
                t_errors.Add($"winner {_actual.WinnerOwner}!={_golden.WinnerOwner}");
            if (_actual.Draw != _golden.Draw)
                t_errors.Add($"draw {_actual.Draw}!={_golden.Draw}");
        }

        Dictionary<string, BattleReplayCheckpoint> t_actualCheckpoints = _actual.Checkpoints
            .ToDictionary(_checkpoint => CheckpointKey(_checkpoint.Turn, _checkpoint.ActingOwner));
        if (_golden.Checkpoints != null)
        {
            for (int i = 0; i < _golden.Checkpoints.Count; i++)
            {
                GoldenCheckpoint t_expected = _golden.Checkpoints[i];
                string t_key = CheckpointKey(t_expected.Turn, t_expected.ActingOwner);
                if (!t_actualCheckpoints.Remove(t_key, out BattleReplayCheckpoint t_actual))
                {
                    t_errors.Add("checkpoint missing:" + t_key);
                    continue;
                }
                string t_checkpointHash = t_actual.StateHash.ToString("x16", CultureInfo.InvariantCulture);
                if (!string.Equals(t_checkpointHash, t_expected.StateHash, StringComparison.OrdinalIgnoreCase)
                    || t_actual.DrawCount != t_expected.DrawCount)
                    t_errors.Add("checkpoint mismatch:" + t_key);
            }
        }
        if (t_actualCheckpoints.Count > 0)
            t_errors.Add("checkpoint extra:" + string.Join(",", t_actualCheckpoints.Keys));
        return t_errors;
    }

    static string CheckpointKey(int _turn, int _owner) => _turn + ":" + _owner;

    static string ResolveRepoRoot(string _start)
    {
        DirectoryInfo t_current = new DirectoryInfo(Path.GetFullPath(_start));
        if (!t_current.Exists && t_current.Parent != null) t_current = t_current.Parent;
        while (t_current != null)
        {
            if (File.Exists(Path.Combine(t_current.FullName, "Assets", "Scripts", "BattleCore", "BattleCore.asmdef"))
                && Directory.Exists(Path.Combine(t_current.FullName, "functions", "testdata", "golden")))
                return t_current.FullName;
            t_current = t_current.Parent;
        }
        return null;
    }
}
