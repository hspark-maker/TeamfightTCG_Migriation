using System.Collections.Generic;

// SynergyState를 실제 CardInstance들에 적용한다. 배틀 init 시 1회 호출(호출부는 다른 층 담당).
// 규칙은 CardInstance에 위임(ApplySynergy/ClearSynergy) — 여기서 스탯 공식 재구현 금지.
public static class SynergyApplier
{
    public static void ApplyAll(SynergyState state, IEnumerable<CardInstance> cards)
    {
        if (cards == null) return;

        // 리스트로 고정(멱등 초기화 + 다중 순회를 위해)
        var t_cards = new List<CardInstance>();
        foreach (var t_card in cards)
        {
            if (t_card == null) continue;
            t_card.ClearSynergy();  // 재적용해도 누적되지 않도록 초기화
            t_cards.Add(t_card);
        }

        if (state == null) return;

        foreach (var t_active in state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;

            foreach (var t_card in t_cards)
            {
                if (!BelongsTo(t_card, t_active.Synergy)) continue;

                foreach (var t_effect in t_active.Tier.effects)
                {
                    if (t_effect == null) continue;
                    t_effect.Apply(t_card, state);
                }
            }
        }
    }

    // 카드가 해당 시너지 소속인지(synergies 배열에 참조 동일성으로 존재하는지).
    // 배열에 중복 나열돼도 존재 여부만 보므로 카드당 1회 판정 → effect 이중적용 없음.
    private static bool BelongsTo(CardInstance _card, SynergyData _synergy)
    {
        if (_card?.data?.synergies == null || _synergy == null) return false;
        return System.Array.IndexOf(_card.data.synergies, _synergy) >= 0;
    }
}
