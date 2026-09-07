using System.Collections.Generic;

// 카드 ID 입력 순서로 활성 시너지를 결정한다. Dictionary 순회 순서에는 의존하지 않는다.
public static class SynergyResolver
{
    public static SynergyState Resolve(IEnumerable<int> deckCards)
    {
        if (deckCards == null) return SynergyState.Empty;

        ISynergyRuleProvider t_provider = SynergyRuleProvider.Current;
        var t_order = new List<string>();
        var t_counts = new Dictionary<string, int>(System.StringComparer.Ordinal);

        foreach (int t_card in deckCards)
        {
            if (!t_provider.ContainsCard(t_card)) continue;
            IReadOnlyList<string> t_synergies = t_provider.SynergyIdsOf(t_card);

            var t_seen = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (string t_synergyId in t_synergies)
            {
                if (string.IsNullOrEmpty(t_synergyId) || !t_seen.Add(t_synergyId)) continue;
                Accumulate(t_synergyId, t_order, t_counts);
            }
        }

        var t_active = new List<ActiveSynergy>();
        foreach (string t_synergyId in t_order)
        {
            int t_count = t_counts[t_synergyId];
            int t_bestIndex = -1;
            SynergyTier t_bestTier = null;
            IReadOnlyList<SynergyTier> t_tiers = t_provider.TiersOf(t_synergyId);

            if (t_tiers != null)
            {
                for (int i = 0; i < t_tiers.Count; i++)
                {
                    SynergyTier t_tier = t_tiers[i];
                    if (t_tier == null || t_tier.requiredCount > t_count) continue;
                    if (t_bestTier == null || t_tier.requiredCount >= t_bestTier.requiredCount)
                    {
                        t_bestIndex = i;
                        t_bestTier = t_tier;
                    }
                }
            }

            if (t_bestTier == null) continue;
            t_active.Add(new ActiveSynergy
            {
                Runtime = new SynergyRuntime(t_synergyId),
                Count = t_count,
                TierIndex = t_bestIndex,
                Tier = t_bestTier,
            });
        }

        return new SynergyState(t_active);
    }

    static void Accumulate(string _synergyId, List<string> _order, Dictionary<string, int> _counts)
    {
        if (_counts.TryGetValue(_synergyId, out int t_count))
        {
            _counts[_synergyId] = t_count + 1;
            return;
        }

        _counts[_synergyId] = 1;
        _order.Add(_synergyId);
    }
}
