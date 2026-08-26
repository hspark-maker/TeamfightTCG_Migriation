using System.Collections.Generic;

// 카드 ID 덱의 순수함수로 SynergyState를 1회 산출한다.
// 결정론: Dictionary 순회 tie-break 없이, 입력(덱) 등장 순서로 결정. UnityEngine.Random 금지.
public static class SynergyResolver
{
    public static SynergyState Resolve(IEnumerable<int> deckCards)
    {
        if (deckCards == null) return SynergyState.Empty;

        // 등장 순서 보존 + 카운트 집계
        var t_order  = new List<SynergyData>();
        var t_counts = new Dictionary<SynergyData, int>();

        foreach (var t_card in deckCards)
        {
            if (!CardCatalog.Contains(t_card)) continue;
            IReadOnlyList<SynergyData> t_synergies = CardCatalog.RequireSynergies(t_card);

            // 한 카드는 같은 시너지를 중복 나열해도 1회만 카운트(Distinct).
            // 배열 등장 순서로 순회 → 결정론 유지(HashSet은 중복 판정용, 순회 순서엔 미개입).
            var t_seen = new HashSet<SynergyData>();
            foreach (var t_synergy in t_synergies)
            {
                if (t_synergy == null) continue;
                if (!t_seen.Add(t_synergy)) continue;  // 이 카드에서 이미 카운트한 시너지
                Accumulate(t_synergy, t_order, t_counts);
            }
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
