using System.Collections.Generic;

/// <summary>덱 편성 화면용 시너지 진행도 1건. 미달 시너지도 포함한다(활성 여부는 IsActive).</summary>
public class SynergyProgress
{
    public SynergyData Synergy;
    public int         Count;            // 덱에 있는 해당 시너지 카드 수(카드당 1회, 중복 나열 무시)
    public SynergyTier ActiveTier;       // null = 아직 미활성
    public int         ActiveTierIndex;  // -1 = 미활성
    public SynergyTier NextTier;         // null = 더 열 티어 없음(최고 티어 도달)

    public bool IsActive => this.ActiveTier != null;

    /// <summary>더 열 티어가 없는 상태(최고 티어 도달). 이때는 "4 / 2" 같은 분모 표시가 무의미하다.</summary>
    public bool IsMaxed => this.ActiveTier != null && this.NextTier == null;

    /// <summary>진행 바/분모 기준값. **다음 티어가 있을 때만 유효**하고, 최고 티어 도달 시 0이다.
    /// (보유 수가 요구치를 넘길 수 있으므로 현재 티어 요구치를 분모로 쓰면 "4 / 2"가 된다.)</summary>
    public int Goal => this.NextTier?.requiredCount ?? 0;
}

/// <summary>
/// 덱 편성 UI 전용 시너지 집계. **전투용 <see cref="SynergyResolver"/>와 별개다.**
///
/// Resolver는 "활성 티어가 열린 시너지"만 반환한다(전투는 그것만 알면 됨).
/// 덱을 짜는 중에는 "4장 중 2장 모았다" 같은 **미달 진행도**가 보여야 하므로 여기서 따로 집계한다.
/// 순수 함수이고 게임 상태를 건드리지 않는다 — 표시 전용이라 결정론 대상이 아니다.
/// 집계 규칙(카드당 Distinct 1회)은 Resolver와 동일하게 유지할 것. 어긋나면 표시와 실제가 갈린다.
/// </summary>
public static class SynergyPreview
{
    /// <summary>덱의 모든 시너지를 진행도와 함께 반환. 활성 먼저, 그다음 보유 수 많은 순.
    /// null 카드/빈 슬롯은 무시한다(편성 중 부분 덱 허용).</summary>
    /// <summary>카드가 그 시너지를 갖는지. 집계(Resolve)와 강조 표시가 같은 판정을 쓰게 하는 창구다.</summary>
    public static bool Has(CardData _card, SynergyData _synergy)
    {
        if (_card == null || _synergy == null) return false;
        IReadOnlyList<SynergyData> t_synergies = CardCatalog.SynergiesOf(_card);

        for (int i = 0; i < t_synergies.Count; i++)
            if (t_synergies[i] == _synergy) return true;

        return false;
    }

    public static List<SynergyProgress> Resolve(IEnumerable<CardData> _deckCards)
    {
        var t_result = new List<SynergyProgress>();
        if (_deckCards == null) return t_result;

        // 등장 순서 보존 + 카운트 집계 (SynergyResolver와 동일 규칙)
        var t_order  = new List<SynergyData>();
        var t_counts = new Dictionary<SynergyData, int>();

        foreach (var t_card in _deckCards)
        {
            if (t_card == null) continue;
            IReadOnlyList<SynergyData> t_synergies = CardCatalog.SynergiesOf(t_card);

            var t_seen = new HashSet<SynergyData>();   // 한 카드가 같은 시너지를 중복 나열해도 1회만
            foreach (var t_synergy in t_synergies)
            {
                if (t_synergy == null) continue;
                if (!t_seen.Add(t_synergy)) continue;

                if (t_counts.TryGetValue(t_synergy, out int t_c))
                {
                    t_counts[t_synergy] = t_c + 1;
                }
                else
                {
                    t_counts[t_synergy] = 1;
                    t_order.Add(t_synergy);
                }
            }
        }

        foreach (var t_synergy in t_order)
        {
            int t_count = t_counts[t_synergy];

            SynergyTier t_active = null, t_next = null;
            int t_activeIndex = -1;
            var t_tiers = t_synergy.tiers;

            if (t_tiers != null)
            {
                for (int i = 0; i < t_tiers.Length; i++)
                {
                    var t_tier = t_tiers[i];
                    if (t_tier == null) continue;

                    if (t_tier.requiredCount <= t_count)
                    {
                        // 만족하는 티어 중 최고(동률이면 뒤쪽) — Resolver와 동일 선택 규칙
                        if (t_active == null || t_tier.requiredCount >= t_active.requiredCount)
                        {
                            t_active      = t_tier;
                            t_activeIndex = i;
                        }
                    }
                    else
                    {
                        // 아직 못 연 티어 중 가장 가까운 것
                        if (t_next == null || t_tier.requiredCount < t_next.requiredCount)
                            t_next = t_tier;
                    }
                }
            }

            t_result.Add(new SynergyProgress
            {
                Synergy         = t_synergy,
                Count           = t_count,
                ActiveTier      = t_active,
                ActiveTierIndex = t_activeIndex,
                NextTier        = t_next,
            });
        }

        // 활성 먼저 → 보유 수 많은 순 → 덱 등장 순.
        // List.Sort는 불안정 정렬이라 마지막 tie-break를 등장 인덱스로 명시해야 한다.
        // (안 그러면 동률 시너지 순서가 새로고침마다 흔들려 목록이 튄다.)
        var t_orderIndex = new Dictionary<SynergyData, int>();
        for (int i = 0; i < t_order.Count; i++)
            t_orderIndex[t_order[i]] = i;

        t_result.Sort((a, b) =>
        {
            if (a.IsActive != b.IsActive) return a.IsActive ? -1 : 1;
            if (a.Count != b.Count)       return b.Count.CompareTo(a.Count);
            return t_orderIndex[a.Synergy].CompareTo(t_orderIndex[b.Synergy]);
        });
        return t_result;
    }
}
