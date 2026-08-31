using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Card Battle/Synergy Registry", fileName = "SynergyRegistry")]
public sealed class SynergyRegistry : ScriptableObject
{
    [SerializeField] SynergyData[] entries = Array.Empty<SynergyData>();
    Dictionary<string, SynergyData> byName;

    public IReadOnlyList<SynergyData> Entries => entries;

    public SynergyData Require(string _name)
    {
        EnsureBuilt();
        string t_key = NormalizeName(_name);
        if (t_key.Length == 0 || !byName.TryGetValue(t_key, out SynergyData t_synergy))
            throw new InvalidOperationException($"[SynergyRegistry] 등록되지 않은 시너지: '{_name}'.");
        return t_synergy;
    }

    public void ValidateOrThrow() => EnsureBuilt(true);

    void EnsureBuilt(bool _rebuild = false)
    {
        if (byName != null && !_rebuild) return;
        var t_map = new Dictionary<string, SynergyData>(StringComparer.Ordinal);
        if (entries == null || entries.Length == 0)
            throw new InvalidOperationException("[SynergyRegistry] 등록된 SynergyData가 없다.");
        foreach (SynergyData t_entry in entries)
        {
            if (t_entry == null) throw new InvalidOperationException("[SynergyRegistry] null 항목이 있다.");
            string t_key = NormalizeName(t_entry.name);
            if (!t_map.TryAdd(t_key, t_entry))
                throw new InvalidOperationException($"[SynergyRegistry] 이름 중복: '{t_key}'.");
        }
        byName = t_map;
    }

    public static string NormalizeName(string _value)
    {
        string t_name = (_value ?? string.Empty).Trim();
        return t_name switch
        {
            "낙인" => "Data_Synergy_Brand",
            "덩치" => "Data_Synergy_Bulk",
            "돌보미" => "Data_Synergy_Caretaker",
            "흐름" => "Data_Synergy_Flow",
            "수호자" => "Data_Synergy_Guardian",
            "유산" => "Data_Synergy_Legacy",
            "포식자" => "Data_Synergy_Predator",
            "비늘" => "Data_Synergy_Scale",
            "언데드" => "Data_Synergy_Undead",
            _ => t_name,
        };
    }
}
