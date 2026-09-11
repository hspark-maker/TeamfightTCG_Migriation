using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json;

/// <summary>서버 매칭 스냅샷을 카드 ID에 고정한다. 수신 뒤 로컬 성장 표로 다시 계산하지 않는다.</summary>
internal static class AiMatchGrowth
{
    internal const int Version = 1;

    internal static IReadOnlyDictionary<int, CardGrowth> Read(
        int? _version, IReadOnlyList<int> _deck, IReadOnlyList<AiMatchCardGrowth> _rows)
    {
        if (!_version.HasValue && _rows == null) return null; // 구 서버 공통 레벨 응답
        if (_version != Version || _deck == null || _rows == null || _rows.Count != _deck.Count)
            throw new InvalidOperationException("Server returned an invalid AI growth contract.");

        var t_ids = new HashSet<int>(_deck);
        var t_growth = new Dictionary<int, CardGrowth>();
        if (t_ids.Count != _deck.Count)
            throw new InvalidOperationException("Server returned duplicate AI cards.");
        foreach (AiMatchCardGrowth t_row in _rows)
        {
            if (t_row == null || !t_ids.Remove(t_row.CardId) || !CardCatalog.Contains(t_row.CardId) ||
                t_row.Level < CardGrowth.BaseLevel || t_row.Level > CardSpec.MaxHpCurveLevel ||
                t_row.LimitBreak < 0 || t_row.LimitBreak > 3 ||
                (t_row.LimitBreak > 0 && t_row.Level != CardSpec.MaxHpCurveLevel) ||
                t_row.HpBonus < 0 || t_row.EvolutionStage < 0 ||
                t_row.EvolutionStage > CardSpec.MaxEvolutionStage || t_row.UnlockedKeywords < 0)
                throw new InvalidOperationException("Server returned invalid AI card growth.");

            t_growth.Add(t_row.CardId, new CardGrowth(t_row.Level, t_row.HpBonus,
                t_row.EvolutionStage, (CardKeyword)t_row.UnlockedKeywords, t_row.SynergyUnlocked));
        }
        return new ReadOnlyDictionary<int, CardGrowth>(t_growth);
    }
}

internal sealed class AiMatchCardGrowth
{
    [JsonProperty("cardId", Required = Required.Always)] public int CardId { get; set; }
    [JsonProperty("level", Required = Required.Always)] public int Level { get; set; }
    [JsonProperty("limitBreak", Required = Required.Always)] public int LimitBreak { get; set; }
    [JsonProperty("hpBonus", Required = Required.Always)] public int HpBonus { get; set; }
    [JsonProperty("evolutionStage", Required = Required.Always)] public int EvolutionStage { get; set; }
    [JsonProperty("unlockedKeywords", Required = Required.Always)] public int UnlockedKeywords { get; set; }
    [JsonProperty("synergyUnlocked", Required = Required.Always)] public bool SynergyUnlocked { get; set; }
}
