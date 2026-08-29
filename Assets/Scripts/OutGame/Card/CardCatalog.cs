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
    static readonly Dictionary<string, int> s_legacyNameToId = new Dictionary<string, int>(StringComparer.Ordinal);

    public static bool IsReady { get; private set; }
    public static IReadOnlyList<int> AllIds => s_allIds;
    public static IReadOnlyList<CardSpec> AllSpecs => s_allSpecs;
    public static int Count => s_allSpecs.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => Clear();

    public static void SetSource(SynergyRegistry _synergyRegistry, EContentRunMode _mode, bool _includeTestCards)
    {
        Clear();
        if (_synergyRegistry == null) throw new InvalidOperationException("[CardCatalog] SynergyRegistry가 배선되지 않았다.");
        _synergyRegistry.ValidateOrThrow();
        Dictionary<int, CardSpec> t_specs = SpecSource.LoadCards(_mode);

        foreach (KeyValuePair<int, CardSpec> t_pair in t_specs)
        {
            CardSpec t_spec = t_pair.Value;
            var t_synergies = new List<SynergyData>(t_spec.SynergyNames.Count);
            foreach (string t_name in t_spec.SynergyNames) t_synergies.Add(_synergyRegistry.Require(t_name));
            s_specById.Add(t_spec.Id, t_spec);
            s_synergiesById.Add(t_spec.Id, t_synergies.AsReadOnly());
            if (!s_legacyNameToId.ContainsKey(t_spec.AssetName)) s_legacyNameToId.Add(t_spec.AssetName, t_spec.Id);
            if (!_includeTestCards && t_spec.Channel != ECardChannel.Live) continue;
            s_includedIds.Add(t_spec.Id);
            s_allIds.Add(t_spec.Id);
            s_allSpecs.Add(t_spec);
        }
        IsReady = true;
    }

    static void Clear()
    {
        s_allIds.Clear();
        s_allSpecs.Clear();
        s_includedIds.Clear();
        s_specById.Clear();
        s_synergiesById.Clear();
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
    public static int LegacyIdOfName(string _name)
        => !string.IsNullOrEmpty(_name) && s_legacyNameToId.TryGetValue(_name, out int t_id) ? t_id : 0;
}
