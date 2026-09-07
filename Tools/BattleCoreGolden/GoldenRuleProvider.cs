using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

internal sealed class GoldenRuleProvider : ISynergyRuleProvider
{
    readonly Dictionary<int, CardSpec> specs;
    readonly Dictionary<int, IReadOnlyList<string>> synergyIdsByCard;
    readonly Dictionary<string, IReadOnlyList<SynergyTier>> tiersBySynergy;

    GoldenRuleProvider(Dictionary<int, CardSpec> _specs,
        Dictionary<int, IReadOnlyList<string>> _synergyIdsByCard,
        Dictionary<string, IReadOnlyList<SynergyTier>> _tiersBySynergy)
    {
        specs = _specs;
        synergyIdsByCard = _synergyIdsByCard;
        tiersBySynergy = _tiersBySynergy;
    }

    public static GoldenRuleProvider Create(string _repoRoot, IReadOnlyList<GoldenCardSpec> _goldenSpecs)
    {
        if (_goldenSpecs == null || _goldenSpecs.Count == 0)
            throw new InvalidOperationException("cardSpecs가 비어 있다.");

        var t_specs = new Dictionary<int, CardSpec>();
        var t_idsByCard = new Dictionary<int, IReadOnlyList<string>>();
        for (int i = 0; i < _goldenSpecs.Count; i++)
        {
            GoldenCardSpec t_source = _goldenSpecs[i];
            var t_synergies = new List<string>();
            if (t_source.Synergies != null)
            {
                for (int j = 0; j < t_source.Synergies.Count; j++)
                {
                    string t_id = SynergyRuntime.NormalizeId(t_source.Synergies[j]);
                    if (t_id.Length > 0 && !t_synergies.Contains(t_id)) t_synergies.Add(t_id);
                }
            }

            var t_spec = new CardSpec(
                t_source.Id, "golden_" + t_source.Id, "golden_" + t_source.Id,
                default, t_source.MaxHp, (CardKeyword)t_source.Keywords,
                t_source.KeywordUnlockLevel, t_source.DefaultEvolutionStage,
                t_source.Hp2, t_source.Hp3, t_source.Hp4, string.Empty, default, t_synergies);
            t_specs.Add(t_source.Id, t_spec);
            t_idsByCard.Add(t_source.Id, t_synergies);
        }

        string t_specRoot = Path.Combine(_repoRoot, "docs", "SpecData");
        Dictionary<string, IReadOnlyList<SynergyTier>> t_tiers = LoadSynergyTiers(t_specRoot);
        return new GoldenRuleProvider(t_specs, t_idsByCard, t_tiers);
    }

    public bool ContainsCard(int _cardId) => specs.ContainsKey(_cardId);

    public CardSpec SpecOf(int _cardId)
        => specs.TryGetValue(_cardId, out CardSpec t_spec)
            ? t_spec
            : throw new KeyNotFoundException($"card_spec_missing:{_cardId}");

    public IReadOnlyList<string> SynergyIdsOf(int _cardId)
        => synergyIdsByCard.TryGetValue(_cardId, out IReadOnlyList<string> t_ids)
            ? t_ids : Array.Empty<string>();

    public IReadOnlyList<SynergyTier> TiersOf(string _synergyId)
        => tiersBySynergy.TryGetValue(SynergyRuntime.NormalizeId(_synergyId), out IReadOnlyList<SynergyTier> t_tiers)
            ? t_tiers : Array.Empty<SynergyTier>();

    static Dictionary<string, IReadOnlyList<SynergyTier>> LoadSynergyTiers(string _specRoot)
    {
        List<Dictionary<string, string>> t_tierRows = ReadSheet(
            Path.Combine(_specRoot, "SynergyTierDef_sheet.csv"));
        List<Dictionary<string, string>> t_effectRows = ReadSheet(
            Path.Combine(_specRoot, "SynergyEffectDef_sheet.csv"));

        var t_builders = new Dictionary<string, TierBuilder>(StringComparer.Ordinal);
        for (int i = 0; i < t_tierRows.Count; i++)
        {
            Dictionary<string, string> t_row = t_tierRows[i];
            string t_synergyId = SynergyRuntime.NormalizeId(Required(t_row, "synergyId"));
            int t_tierIndex = ParseInt(t_row, "tierIndex");
            string t_key = TierKey(t_synergyId, t_tierIndex);
            t_builders.Add(t_key, new TierBuilder
            {
                SynergyId = t_synergyId,
                TierIndex = t_tierIndex,
                Tier = new SynergyTier
                {
                    requiredCount = ParseInt(t_row, "requiredCount"),
                    label = Value(t_row, "label"),
                    effectSummary = Value(t_row, "effectSummary"),
                    effects = Array.Empty<SynergyEffect>(),
                },
            });
        }

        for (int i = 0; i < t_effectRows.Count; i++)
        {
            Dictionary<string, string> t_row = t_effectRows[i];
            string t_synergyId = SynergyRuntime.NormalizeId(Required(t_row, "synergyId"));
            int t_tierIndex = ParseInt(t_row, "tierIndex");
            if (!t_builders.TryGetValue(TierKey(t_synergyId, t_tierIndex), out TierBuilder t_builder))
                throw new InvalidOperationException($"synergy_tier_missing:{t_synergyId}:{t_tierIndex}");

            string t_effectType = Required(t_row, "effectType");
            SynergyEffect t_effect = SynergyEffectFactory.Create(t_effectType)
                ?? throw new InvalidOperationException($"synergy_effect_unknown:{t_effectType}");
            ApplyParameters(t_effect, Value(t_row, "parameters"));
            t_builder.Effects.Add((ParseInt(t_row, "effectOrder"), t_effect));
        }

        var t_grouped = new Dictionary<string, List<TierBuilder>>(StringComparer.Ordinal);
        foreach (TierBuilder t_builder in t_builders.Values)
        {
            t_builder.Effects.Sort((a, b) => a.order.CompareTo(b.order));
            var t_effects = new SynergyEffect[t_builder.Effects.Count];
            for (int i = 0; i < t_effects.Length; i++) t_effects[i] = t_builder.Effects[i].effect;
            t_builder.Tier.effects = t_effects;
            if (!t_grouped.TryGetValue(t_builder.SynergyId, out List<TierBuilder> t_list))
            {
                t_list = new List<TierBuilder>();
                t_grouped.Add(t_builder.SynergyId, t_list);
            }
            t_list.Add(t_builder);
        }

        var t_result = new Dictionary<string, IReadOnlyList<SynergyTier>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<TierBuilder>> t_pair in t_grouped)
        {
            t_pair.Value.Sort((a, b) => a.TierIndex.CompareTo(b.TierIndex));
            var t_tiers = new List<SynergyTier>(t_pair.Value.Count);
            for (int i = 0; i < t_pair.Value.Count; i++) t_tiers.Add(t_pair.Value[i].Tier);
            t_result.Add(t_pair.Key, t_tiers);
        }
        return t_result;
    }

    static void ApplyParameters(SynergyEffect _effect, string _parameters)
    {
        if (string.IsNullOrWhiteSpace(_parameters)) return;
        string[] t_pairs = _parameters.Split(';', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < t_pairs.Length; i++)
        {
            int t_separator = t_pairs[i].IndexOf('=');
            if (t_separator <= 0)
                throw new FormatException("synergy_parameter_invalid:" + t_pairs[i]);
            string t_key = t_pairs[i].Substring(0, t_separator).Trim();
            string t_value = t_pairs[i].Substring(t_separator + 1).Trim();
            if (!_effect.TrySetParam(t_key, t_value))
                throw new InvalidOperationException($"synergy_parameter_unknown:{_effect.GetType().Name}:{t_key}");
        }
    }

    static List<Dictionary<string, string>> ReadSheet(string _path)
    {
        string[] t_lines = File.ReadAllLines(_path);
        int t_headerIndex = -1;
        List<string> t_columns = null;
        for (int i = 0; i < t_lines.Length; i++)
        {
            List<string> t_values = ParseCsvLine(t_lines[i].TrimStart('\uFEFF'));
            if (t_values.Count > 0 && t_values[0] == "id")
            {
                t_headerIndex = i;
                t_columns = t_values;
                break;
            }
        }
        if (t_headerIndex < 0 || t_columns == null)
            throw new InvalidOperationException(Path.GetFileName(_path) + ": header missing");

        var t_rows = new List<Dictionary<string, string>>();
        for (int i = t_headerIndex + 2; i < t_lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(t_lines[i])) continue;
            List<string> t_values = ParseCsvLine(t_lines[i]);
            var t_row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int j = 0; j < t_columns.Count; j++)
                t_row[t_columns[j]] = j < t_values.Count ? t_values[j] : string.Empty;
            t_rows.Add(t_row);
        }
        return t_rows;
    }

    static List<string> ParseCsvLine(string _line)
    {
        var t_values = new List<string>();
        var t_value = new System.Text.StringBuilder();
        bool t_quoted = false;
        for (int i = 0; i < _line.Length; i++)
        {
            char t_char = _line[i];
            if (t_char == '"')
            {
                if (t_quoted && i + 1 < _line.Length && _line[i + 1] == '"')
                {
                    t_value.Append('"');
                    i++;
                }
                else t_quoted = !t_quoted;
            }
            else if (t_char == ',' && !t_quoted)
            {
                t_values.Add(t_value.ToString());
                t_value.Clear();
            }
            else t_value.Append(t_char);
        }
        t_values.Add(t_value.ToString());
        return t_values;
    }

    static int ParseInt(Dictionary<string, string> _row, string _key)
        => int.Parse(Required(_row, _key), NumberStyles.Integer, CultureInfo.InvariantCulture);

    static string Required(Dictionary<string, string> _row, string _key)
    {
        string t_value = Value(_row, _key);
        if (string.IsNullOrWhiteSpace(t_value)) throw new FormatException("required:" + _key);
        return t_value;
    }

    static string Value(Dictionary<string, string> _row, string _key)
        => _row.TryGetValue(_key, out string t_value) ? t_value : string.Empty;

    static string TierKey(string _synergyId, int _tierIndex) => _synergyId + ":" + _tierIndex;

    sealed class TierBuilder
    {
        public string SynergyId;
        public int TierIndex;
        public SynergyTier Tier;
        public readonly List<(int order, SynergyEffect effect)> Effects = new List<(int, SynergyEffect)>();
    }
}
