using System.Collections.Generic;

// 덱·카드의 파워(전력) 환산. 편성 화면 정렬과 매치 화면 표시가 같은 식을 쓰게 하는 단일 지점이다.
//
// 파워 = 강화 반영 최대 체력 + bonusHp. 이 게임엔 공격력 필드가 없고 공격력이 hp에서 파생되므로(CardInstance.AttackDamage)
// 카드의 강함은 hp 하나로 표현된다. 런타임 부여분(시너지 덩치·돌보미)은 전투에 들어가야 생기니 여기서 알 수 없다.
public static class DeckPower
{
    /// <summary>강화 반영 최대 체력(= 전투의 CardInstance.maxHp와 같은 식). 아웃게임 표시는 전부 여기를 부른다 —
    /// 화면마다 maxHp + HpBonusOf를 각자 더하면 강화가 반영된 화면과 안 된 화면이 갈린다.
    /// _applyGrowth=false는 상대 덱처럼 **내 성장이 붙으면 안 되는** 표시용(전투도 AI 적은 마스터 스탯 그대로다).</summary>
    public static int MaxHpOf(CardData _card, bool _applyGrowth = true)
    {
        if (_card == null) return 0;

        return _applyGrowth ? _card.maxHp + CardGrowthManager.HpBonusOf(_card) : _card.maxHp;
    }

    // null 카드는 0. 빈 칸을 호출측이 거르지 않아도 되게 한다.
    public static int Of(CardData _card, bool _applyGrowth = true)
        => _card != null ? MaxHpOf(_card, _applyGrowth) + _card.bonusHp : 0;

    // 덱 전체 합. 빈 칸(null)과 미배선 덱(null)은 0으로 흡수한다.
    public static int Of(IReadOnlyList<CardData> _deck, bool _applyGrowth = true)
    {
        if (_deck == null) return 0;

        int t_sum = 0;
        for (int t_i = 0; t_i < _deck.Count; t_i++)
            t_sum += Of(_deck[t_i], _applyGrowth);

        return t_sum;
    }
}
