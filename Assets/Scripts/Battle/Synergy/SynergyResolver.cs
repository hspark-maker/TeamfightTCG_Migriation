using System.Collections.Generic;

// 덱(CardData 목록)의 순수함수로 SynergyState를 1회 산출한다.
// 결정론: Dictionary 순회 tie-break 없이, 입력(덱) 등장 순서로 결정. UnityEngine.Random 금지.
public static class SynergyResolver
{
    public static SynergyState Resolve(IEnumerable<CardData> deckCards)
    {
        if (deckCards == null) return SynergyState.Empty;

        // 등장 순서 보존 + 카운트 집계
        var t_order  = new List<SynergyData>();
        var t_counts = new Dictionary<SynergyData, int>();

        foreach (var t_card in deckCards)
        {
            if (t_card == null) continue;
            Accumulate(t_card.mainSynergy, t_order, t_counts);
            Accumulate(t_card.subClass,    t_order, t_counts);
        }

        var t_active = new List<ActiveSynergy>();
        foreach (var t_synergy in t_order)
        {
            int t_count = t_counts[t_synergy];

            int         t_bestIndex = -1;
            SynergyTier t_bestTier  = null;
            var         t_tiers      = t_synergy.tiers;
            if (t_tiers != null)
            {
                for (int i = 0; i < t_tiers.Length; i++)
                {
                    var t_tier = t_tiers[i];
                    if (t_tier == null) continue;
                    if (t_tier.requiredCount > t_count) continue;
                    // 만족하는 티어 중 requiredCount 최대(동률이면 뒤쪽 인덱스) = 최고 티어
                    if (t_bestTier == null || t_tier.requiredCount >= t_bestTier.requiredCount)
                    {
                        t_bestIndex = i;
                        t_bestTier  = t_tier;
                    }
                }
            }

            if (t_bestTier == null) continue;  // 열린 티어 없음 → 비활성

            t_active.Add(new ActiveSynergy
            {
                Synergy   = t_synergy,
                Count     = t_count,
                TierIndex = t_bestIndex,
                Tier      = t_bestTier,
            });
        }

        return new SynergyState(t_active);
    }

    private static void Accumulate(SynergyData _synergy, List<SynergyData> _order, Dictionary<SynergyData, int> _counts)
    {
        if (_synergy == null) return;
        if (_counts.TryGetValue(_synergy, out int t_c))
        {
            _counts[_synergy] = t_c + 1;
        }
        else
        {
            _counts[_synergy] = 1;
            _order.Add(_synergy);  // 첫 등장 순서 고정 → 결정론
        }
    }
}
