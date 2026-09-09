using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

internal sealed class ClaimRewardItem
{
    [JsonProperty("rewardType")] public string RewardType { get; set; }
    [JsonProperty("rewardId")] public string RewardId { get; set; }
    [JsonProperty("amount")] public long Amount { get; set; }
}

internal static class RewardItemDisplay
{
    internal static List<DrawnCard> ToDrawn(IReadOnlyList<OpenPackCard> _cards)
    {
        var t_result = new List<DrawnCard>();
        if (_cards != null)
            foreach (var t_card in _cards)
                if (t_card != null && t_card.CardId > 0)
                    t_result.Add(new DrawnCard(t_card.CardId, t_card.IsNew, t_card.Snack));
        return t_result;
    }
    internal static string NameOf(string _type, string _id)
    {
        if (_type == "PackChoice") return "해금된 테마 팩 선택";
        if (_type == "Pack")
        {
            string t_name = PackSpec.DisplayName(_id);
            return string.IsNullOrEmpty(t_name) ? "카드 팩" : t_name;
        }
        if (_type == "Card")
            return int.TryParse(_id, out int t_id) && CardCatalog.TryGetSpec(t_id, out var t_card)
                ? t_card.DisplayName : "카드";
        return _id ?? string.Empty;
    }

    internal static void Append(StringBuilder _text, IReadOnlyList<ClaimRewardItem> _items)
    {
        if (_items == null) return;
        foreach (var t_item in _items)
        {
            if (t_item == null || t_item.Amount <= 0) continue;
            if (_text.Length > 0) _text.Append("  ");
            _text.Append(NameOf(t_item.RewardType, t_item.RewardId)).Append(" ×").Append(t_item.Amount);
        }
    }
}
