using System.Collections.Generic;

// 덱·카드의 파워(전력) 환산 단일 지점
public static class DeckPower
{
    // 강화 반영 최대 체력 — _applyGrowth=false는 상대 덱처럼 내 성장이 붙으면 안 되는 표시용
    public static int MaxHpOf(CardData _card, bool _applyGrowth = true)
    {
        if (_card == null) return 0;

        return _applyGrowth ? _card.maxHp + CardGrowthManager.HpBonusOf(_card) : _card.maxHp;
    }

    // 카드 1장의 파워(null은 0)
    public static int Of(CardData _card, bool _applyGrowth = true)
        => _card != null ? MaxHpOf(_card, _applyGrowth) + _card.bonusHp : 0;

    // 덱 전체 파워 합
    public static int Of(IReadOnlyList<CardData> _deck, bool _applyGrowth = true)
    {
        if (_deck == null) return 0;

        int t_sum = 0;
        for (int t_i = 0; t_i < _deck.Count; t_i++)
            t_sum += Of(_deck[t_i], _applyGrowth);

        return t_sum;
    }
}
