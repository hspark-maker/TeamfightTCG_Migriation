using System.Globalization;

internal sealed class BattleRuleSet : ISynergyRuleProvider
{
    readonly Dictionary<int, CardSpec> specs;
    readonly Dictionary<int, IReadOnlyList<string>> synergyIdsByCard;
    readonly Dictionary<string, IReadOnlyList<SynergyTier>> tiersBySynergy;

    BattleRuleSet(
        Dictionary<int, CardSpec> _specs,
        Dictionary<int, IReadOnlyList<string>> _synergyIdsByCard,
        Dictionary<string, IReadOnlyList<SynergyTier>> _tiersBySynergy)
    {
        specs = _specs;
        synergyIdsByCard = _synergyIdsByCard;
        tiersBySynergy = _tiersBySynergy;
    }

    public static BattleRuleSet Create(IReadOnlyDictionary<string, SpecTable> _tables)
    {
        var t_specs = new Dictionary<int, CardSpec>();
        var t_idsByCard = new Dictionary<int, IReadOnlyList<string>>();
        foreach (IReadOnlyDictionary<string, string> t_row in _tables["Card"].Rows)
        {
            int t_id = Int(t_row, "id");
            List<string> t_synergies = Split(t_row, "synergies", new[] { '|', '/' }, _normalizeSynergy: true);
            CardKeyword t_keywords = Keywords(Value(t_row, "keywords"));
            var t_spec = new CardSpec(
                t_id,
                Required(t_row, "name"),
                Value(t_row, "displayName"),
                EnumValue<ECardChannel>(t_row, "channel"),
                Int(t_row, "maxHp"),
                t_keywords,
                Int(t_row, "keywordUnlockLevel"),
                Int(t_row, "defaultEvolutionStage"),
                Int(t_row, "hp2"),
                Int(t_row, "hp3"),
                Int(t_row, "hp4"),
                Value(t_row, "cardExplain"),
                EnumValue<ECardGrade>(t_row, "grade"),
                t_synergies);
            if (!t_specs.TryAdd(t_id, t_spec)) throw new SpecLoadException("card_duplicate:" + t_id);
            t_idsByCard.Add(t_id, t_synergies);
        }

        Dictionary<string, IReadOnlyList<SynergyTier>> t_tiers = BuildTiers(
            _tables["SynergyDef"], _tables["SynergyTierDef"], _tables["SynergyEffectDef"]);
        return new BattleRuleSet(t_specs, t_idsByCard, t_tiers);
    }

    public bool ContainsCard(int _cardId) => specs.ContainsKey(_cardId);

    public CardSpec SpecOf(int _cardId)
        => specs.TryGetValue(_cardId, out CardSpec? t_spec)
            ? t_spec : throw new KeyNotFoundException("card_spec_missing:" + _cardId);

    public IReadOnlyList<string> SynergyIdsOf(int _cardId)
        => synergyIdsByCard.TryGetValue(_cardId, out IReadOnlyList<string>? t_ids)
            ? t_ids : Array.Empty<string>();

    public IReadOnlyList<SynergyTier> TiersOf(string _synergyId)
        => tiersBySynergy.TryGetValue(SynergyRuntime.NormalizeId(_synergyId), out IReadOnlyList<SynergyTier>? t_tiers)
            ? t_tiers : Array.Empty<SynergyTier>();

    static Dictionary<string, IReadOnlyList<SynergyTier>> BuildTiers(
        SpecTable _definitions, SpecTable _tiers, SpecTable _effects)
    {
        var t_known = new HashSet<string>(StringComparer.Ordinal);
        foreach (IReadOnlyDictionary<string, string> t_row in _definitions.Rows)
            t_known.Add(SynergyRuntime.NormalizeId(Required(t_row, "synergyId")));

        var t_builders = new Dictionary<string, TierBuilder>(StringComparer.Ordinal);
        foreach (IReadOnlyDictionary<string, string> t_row in _tiers.Rows)
        {
            string t_id = SynergyRuntime.NormalizeId(Required(t_row, "synergyId"));
            if (!t_known.Contains(t_id)) throw new SpecLoadException("synergy_definition_missing:" + t_id);
            int t_index = Int(t_row, "tierIndex");
            string t_key = t_id + ":" + t_index;
            if (!t_builders.TryAdd(t_key, new TierBuilder
            {
                SynergyId = t_id,
                TierIndex = t_index,
                Tier = new SynergyTier
                {
                    requiredCount = Int(t_row, "requiredCount"),
                    label = Value(t_row, "label"),
                    effectSummary = Value(t_row, "effectSummary"),
                    effects = Array.Empty<SynergyEffect>(),
                },
            })) throw new SpecLoadException("synergy_tier_duplicate:" + t_key);
        }

        foreach (IReadOnlyDictionary<string, string> t_row in _effects.Rows)
        {
            string t_id = SynergyRuntime.NormalizeId(Required(t_row, "synergyId"));
            int t_index = Int(t_row, "tierIndex");
            string t_key = t_id + ":" + t_index;
            if (!t_builders.TryGetValue(t_key, out TierBuilder? t_builder))
                throw new SpecLoadException("synergy_tier_missing:" + t_key);
            string t_type = Required(t_row, "effectType");
            SynergyEffect t_effect = SynergyEffectFactory.Create(t_type)
                ?? throw new SpecLoadException("synergy_effect_unknown:" + t_type);
            ApplyParameters(t_effect, Value(t_row, "parameters"));
            t_builder.Effects.Add((Int(t_row, "effectOrder"), t_effect));
        }

        var t_grouped = new Dictionary<string, List<TierBuilder>>(StringComparer.Ordinal);
        foreach (TierBuilder t_builder in t_builders.Values)
        {
            t_builder.Effects.Sort((_a, _b) => _a.Order.CompareTo(_b.Order));
            t_builder.Tier.effects = t_builder.Effects.Select(_pair => _pair.Effect).ToArray();
            if (!t_grouped.TryGetValue(t_builder.SynergyId, out List<TierBuilder>? t_list))
                t_grouped[t_builder.SynergyId] = t_list = new List<TierBuilder>();
            t_list.Add(t_builder);
        }

        var t_result = new Dictionary<string, IReadOnlyList<SynergyTier>>(StringComparer.Ordinal);
        foreach ((string t_id, List<TierBuilder> t_list) in t_grouped)
        {
            t_list.Sort((_a, _b) => _a.TierIndex.CompareTo(_b.TierIndex));
            t_result[t_id] = t_list.Select(_builder => _builder.Tier).ToArray();
        }
        return t_result;
    }

    static void ApplyParameters(SynergyEffect _effect, string _parameters)
    {
        if (string.IsNullOrWhiteSpace(_parameters)) return;
        foreach (string t_pair in _parameters.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int t_separator = t_pair.IndexOf('=');
            if (t_separator <= 0) throw new SpecLoadException("synergy_parameter_invalid:" + t_pair);
            string t_key = t_pair[..t_separator].Trim();
            string t_value = t_pair[(t_separator + 1)..].Trim();
            if (!_effect.TrySetParam(t_key, t_value))
                throw new SpecLoadException($"synergy_parameter_unknown:{_effect.GetType().Name}:{t_key}");
        }
    }

    static CardKeyword Keywords(string _text)
    {
        CardKeyword t_result = CardKeyword.None;
        if (string.IsNullOrWhiteSpace(_text)) return t_result;
        foreach (string t_name in _text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse(t_name, true, out CardKeyword t_keyword))
                throw new SpecLoadException("card_keyword_invalid:" + t_name);
            t_result |= t_keyword;
        }
        return t_result;
    }

    static List<string> Split(
        IReadOnlyDictionary<string, string> _row, string _key, char[] _separators, bool _normalizeSynergy)
    {
        var t_result = new List<string>();
        foreach (string t_item in Value(_row, _key).Split(
                     _separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string t_value = _normalizeSynergy ? SynergyRuntime.NormalizeId(t_item) : t_item;
            if (t_value.Length > 0 && !t_result.Contains(t_value, StringComparer.Ordinal)) t_result.Add(t_value);
        }
        return t_result;
    }

    static T EnumValue<T>(IReadOnlyDictionary<string, string> _row, string _key) where T : struct, Enum
        => Enum.TryParse(Required(_row, _key), true, out T t_value)
            ? t_value : throw new SpecLoadException("enum_invalid:" + _key);

    static int Int(IReadOnlyDictionary<string, string> _row, string _key)
        => int.TryParse(Required(_row, _key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int t_value)
            ? t_value : throw new SpecLoadException("integer_invalid:" + _key);

    static string Required(IReadOnlyDictionary<string, string> _row, string _key)
    {
        string t_value = Value(_row, _key);
        return t_value.Length > 0 ? t_value : throw new SpecLoadException("required:" + _key);
    }

    static string Value(IReadOnlyDictionary<string, string> _row, string _key)
        => _row.TryGetValue(_key, out string? t_value) ? t_value : string.Empty;

    sealed class TierBuilder
    {
        public string SynergyId = string.Empty;
        public int TierIndex;
        public SynergyTier Tier = null!;
        public readonly List<(int Order, SynergyEffect Effect)> Effects = new();
    }
}
