using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>표 기반 카드 정의와 ID 조회의 단일 창구.</summary>
public static class CardCatalog
{
    static readonly List<int> s_allIds = new List<int>();
    static readonly List<CardSpec> s_allSpecs = new List<CardSpec>();
    static readonly HashSet<int> s_includedIds = new HashSet<int>();
    static readonly Dictionary<int, CardSpec> s_specById = new Dictionary<int, CardSpec>();
    static readonly Dictionary<int, IReadOnlyList<SynergyData>> s_synergiesById = new Dictionary<int, IReadOnlyList<SynergyData>>();
    static readonly Dictionary<int, IReadOnlyList<string>> s_synergyIdsByCardId = new Dictionary<int, IReadOnlyList<string>>();
    static readonly Dictionary<string, SynergyData> s_synergyByRuntimeId = new Dictionary<string, SynergyData>(StringComparer.Ordinal);
    static readonly ISynergyRuleProvider s_ruleProvider = new CatalogSynergyRuleProvider();
    static readonly Dictionary<string, int> s_legacyNameToId = new Dictionary<string, int>(StringComparer.Ordinal);

    public static bool IsReady { get; private set; }
    public static IReadOnlyList<int> AllIds => s_allIds;
    public static IReadOnlyList<CardSpec> AllSpecs => s_allSpecs;
    public static int Count => s_allSpecs.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => Clear();

    public static void SetSource(SynergyRegistry _synergyRegistry, bool _includeTestCards)
    {
        Clear();
        if (_synergyRegistry == null) throw new InvalidOperationException("[CardCatalog] SynergyRegistry가 배선되지 않았다.");
        _synergyRegistry.ValidateOrThrow();
        // 카드가 시너지를 참조하기 전에 규칙을 먼저 꽂는다 — 티어가 빈 SynergyData로 전투에 들어가면
        // 예외 없이 시너지만 사라져 무증상 회귀가 된다.
        SynergySpecSource.Apply(_synergyRegistry);
        foreach (SynergyData t_synergy in _synergyRegistry.Entries)
        {
            if (t_synergy == null) continue;
            string t_runtimeId = t_synergy.SynergyId;
            if (!s_synergyByRuntimeId.TryAdd(t_runtimeId, t_synergy))
                throw new InvalidOperationException($"[CardCatalog] Duplicate synergy runtime ID: '{t_runtimeId}'.");
        }
        Dictionary<int, CardSpec> t_specs = SpecSource.LoadCards();

        foreach (KeyValuePair<int, CardSpec> t_pair in t_specs)
        {
            CardSpec t_spec = t_pair.Value;
            var t_synergies = new List<SynergyData>(t_spec.SynergyNames.Count);
            var t_synergyIds = new List<string>(t_spec.SynergyNames.Count);
            var t_seenSynergyIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string t_name in t_spec.SynergyNames)
            {
                SynergyData t_synergy = _synergyRegistry.Require(t_name);
                string t_runtimeId = t_synergy.SynergyId;
                if (!t_seenSynergyIds.Add(t_runtimeId))
                    throw new InvalidOperationException($"[CardCatalog] Card {t_spec.Id} has duplicate synergy ID '{t_runtimeId}'.");
                t_synergies.Add(t_synergy);
                t_synergyIds.Add(t_runtimeId);
            }
            s_specById.Add(t_spec.Id, t_spec);
            s_synergiesById.Add(t_spec.Id, t_synergies.AsReadOnly());
            s_synergyIdsByCardId.Add(t_spec.Id, t_synergyIds.AsReadOnly());
            if (!s_legacyNameToId.ContainsKey(t_spec.AssetName)) s_legacyNameToId.Add(t_spec.AssetName, t_spec.Id);
            if (!_includeTestCards && t_spec.Channel != ECardChannel.Live) continue;
            s_includedIds.Add(t_spec.Id);
            s_allIds.Add(t_spec.Id);
            s_allSpecs.Add(t_spec);
        }
        IsReady = true;
        SynergyRuleProvider.Install(s_ruleProvider);
    }

    static void Clear()
    {
        SynergyRuleProvider.Reset();
        s_allIds.Clear();
        s_allSpecs.Clear();
        s_includedIds.Clear();
        s_specById.Clear();
        s_synergiesById.Clear();
        s_synergyIdsByCardId.Clear();
        s_synergyByRuntimeId.Clear();
        s_legacyNameToId.Clear();
        IsReady = false;
    }

    public static bool Contains(int _id) => _id > 0 && s_includedIds.Contains(_id);
    public static bool TryGetSpec(int _id, out CardSpec _spec)
    {
        _spec = null;
        return IsReady && _id > 0 && s_specById.TryGetValue(_id, out _spec);
    }
    public static CardSpec RequireSpec(int _id)
    {
        if (!IsReady) throw new InvalidOperationException("[CardCatalog] 초기화 전에 CardSpec을 조회했다.");
        if (_id <= 0 || !s_specById.TryGetValue(_id, out CardSpec t_spec))
            throw new InvalidOperationException($"[CardCatalog] 카드 ID {_id}의 CardSpec이 없다.");
        return t_spec;
    }
    public static IReadOnlyList<SynergyData> RequireSynergies(int _id)
    {
        if (!IsReady) throw new InvalidOperationException("[CardCatalog] 초기화 전에 시너지를 조회했다.");
        if (_id <= 0 || !s_synergiesById.TryGetValue(_id, out IReadOnlyList<SynergyData> t_synergies))
            throw new InvalidOperationException($"[CardCatalog] 카드 ID {_id}의 시너지 매핑이 없다.");
        return t_synergies;
    }
    public static IReadOnlyList<string> RequireSynergyIds(int _id)
    {
        if (!IsReady) throw new InvalidOperationException("[CardCatalog] Synergy IDs requested before initialization.");
        if (_id <= 0 || !s_synergyIdsByCardId.TryGetValue(_id, out IReadOnlyList<string> t_synergyIds))
            throw new InvalidOperationException($"[CardCatalog] Card ID {_id} has no synergy ID mapping.");
        return t_synergyIds;
    }
    public static IReadOnlyList<SynergyTier> RequireSynergyTiers(string _synergyId)
    {
        if (!IsReady) throw new InvalidOperationException("[CardCatalog] Synergy tiers requested before initialization.");
        string t_id = SynergyRuntime.NormalizeId(_synergyId);
        if (t_id.Length == 0 || !s_synergyByRuntimeId.TryGetValue(t_id, out SynergyData t_synergy) || t_synergy.tiers == null)
            throw new InvalidOperationException($"[CardCatalog] Synergy '{t_id}' has no rule tiers.");
        return t_synergy.tiers;
    }
    public static bool TryGetSynergyData(SynergyRuntime _runtime, out SynergyData _synergy)
    {
        _synergy = null;
        return IsReady && _runtime != null &&
               s_synergyByRuntimeId.TryGetValue(_runtime.SynergyId, out _synergy);
    }
    public static int LegacyIdOfName(string _name)
        => !string.IsNullOrEmpty(_name) && s_legacyNameToId.TryGetValue(_name, out int t_id) ? t_id : 0;

    sealed class CatalogSynergyRuleProvider : ISynergyRuleProvider
    {
        public bool ContainsCard(int _cardId) => CardCatalog.Contains(_cardId);
        public IReadOnlyList<string> SynergyIdsOf(int _cardId) => CardCatalog.RequireSynergyIds(_cardId);
        public IReadOnlyList<SynergyTier> TiersOf(string _synergyId) => CardCatalog.RequireSynergyTiers(_synergyId);
    }
}
