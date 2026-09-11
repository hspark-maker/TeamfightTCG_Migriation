using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

internal sealed class ClaimRewardItem
{
    [JsonProperty("rewardType")] public string RewardType { get; set; }
    [JsonProperty("rewardId")] public string RewardId { get; set; }
    [JsonProperty("amount")] public long Amount { get; set; }
}

internal sealed class ClaimRewardPack
{
    [JsonProperty("packId")] public string PackId { get; set; }
    [JsonProperty("cards")] public List<OpenPackCard> Cards { get; set; }
}

internal static class RewardItemDisplay
{
    internal static RewardClaimOutcome ToOutcome(IReadOnlyList<CurrencyGain> _gains,
        IReadOnlyList<OpenPackCard> _cards, IReadOnlyList<ClaimRewardPack> _packs)
    {
        var t_flat = ToDrawn(_cards);
        // 0은 직접 지급, 나머지는 팩 인덱스 + 1. 원본 위치를 유지하며 중복을 한 장씩 배정한다.
        var t_packOwners = new int[t_flat.Count];
        var t_packs = new List<GrantedRewardPack>();
        if (_packs != null)
            foreach (var t_pack in _packs)
            {
                if (t_pack == null || string.IsNullOrEmpty(t_pack.PackId)) continue;
                var t_drawn = ToDrawn(t_pack.Cards);
                if (t_drawn.Count == 0) continue;
                t_packs.Add(new GrantedRewardPack { PackId = t_pack.PackId, Cards = t_drawn });
            }

        // 정상 응답의 팩 카드는 flat에서 연속 구간이다. 팩 전체를 먼저 찾아야 앞선
        // 직접 지급의 동일 카드를 가져가서 그 뒤 성장보다 팩을 먼저 재생하지 않는다.
        var t_matchedPacks = new bool[t_packs.Count];
        for (int t_packIndex = 0; t_packIndex < t_packs.Count; t_packIndex++)
        {
            var t_cards = t_packs[t_packIndex].Cards;
            int t_start = FindPackStart(t_flat, t_packOwners, t_cards);
            if (t_start < 0) continue;
            for (int t_i = 0; t_i < t_cards.Count; t_i++)
                t_packOwners[t_start + t_i] = t_packIndex + 1;
            t_matchedPacks[t_packIndex] = true;
        }

        // 순서가 섞인 legacy flat은 기존 multiset 매칭을 유지한다.
        // 모든 연속 팩을 먼저 확보한 뒤 남은 항목만 한 장씩 배정한다.
        for (int t_packIndex = 0; t_packIndex < t_packs.Count; t_packIndex++)
            if (!t_matchedPacks[t_packIndex])
                foreach (var t_card in t_packs[t_packIndex].Cards)
                {
                    for (int t_i = 0; t_i < t_flat.Count; t_i++)
                    {
                        if (t_packOwners[t_i] != 0 || !SameCard(t_flat[t_i], t_card)) continue;
                        t_packOwners[t_i] = t_packIndex + 1;
                        break;
                    }
                }
        var t_direct = new List<DrawnCard>();
        var t_batches = new List<RewardPresentationBatch>();
        var t_presentedPacks = new bool[t_packs.Count];
        List<DrawnCard> t_directBatch = null;
        for (int t_i = 0; t_i < t_flat.Count; t_i++)
        {
            int t_packIndex = t_packOwners[t_i] - 1;
            if (t_packIndex < 0)
            {
                t_direct.Add(t_flat[t_i]);
                if (t_directBatch == null)
                {
                    t_directBatch = new List<DrawnCard>();
                    t_batches.Add(new RewardPresentationBatch(t_directBatch));
                }
                t_directBatch.Add(t_flat[t_i]);
                continue;
            }

            t_directBatch = null;
            if (t_presentedPacks[t_packIndex]) continue;
            var t_pack = t_packs[t_packIndex];
            t_batches.Add(new RewardPresentationBatch(t_pack.Cards, t_pack.PackId));
            t_presentedPacks[t_packIndex] = true;
        }

        // flat에 포함되지 않은 팩도 유실시키지 않는다. 위치 근거가 없으므로 끝에 붙인다.
        for (int t_i = 0; t_i < t_packs.Count; t_i++)
            if (!t_presentedPacks[t_i])
                t_batches.Add(new RewardPresentationBatch(t_packs[t_i].Cards, t_packs[t_i].PackId));

        return new RewardClaimOutcome(_gains, t_direct, t_packs, t_batches);
    }

    internal static List<DrawnCard> ToDrawn(IReadOnlyList<OpenPackCard> _cards)
    {
        var t_result = new List<DrawnCard>();
        if (_cards != null)
            foreach (var t_card in _cards)
                if (t_card != null && t_card.CardId > 0)
                    t_result.Add(new DrawnCard(t_card.CardId, t_card.IsNew, t_card.Snack, t_card.SnackGrowth));
        return t_result;
    }

    static int FindPackStart(IReadOnlyList<DrawnCard> _flat, int[] _packOwners, IReadOnlyList<DrawnCard> _cards)
    {
        for (int t_start = 0; t_start <= _flat.Count - _cards.Count; t_start++)
        {
            int t_i = 0;
            while (t_i < _cards.Count && _packOwners[t_start + t_i] == 0 &&
                   SameCard(_flat[t_start + t_i], _cards[t_i])) t_i++;
            if (t_i == _cards.Count) return t_start;
        }
        return -1;
    }

    static bool SameCard(DrawnCard _left, DrawnCard _right)
        => _left.CardId == _right.CardId && _left.IsNew == _right.IsNew && _left.Snack == _right.Snack &&
            SameGrowth(_left.SnackGrowth, _right.SnackGrowth);

    static bool SameGrowth(SnackGrowthResult _left, SnackGrowthResult _right)
        => ReferenceEquals(_left, _right) || (_left != null && _right != null &&
            _left.FromStage == _right.FromStage && _left.ToStage == _right.ToStage &&
            _left.HpGain == _right.HpGain && _left.SnackCost == _right.SnackCost &&
            _left.SnackLeft == _right.SnackLeft);
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
