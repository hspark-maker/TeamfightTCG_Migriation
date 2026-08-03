using System.Collections.Generic;

// 덱·카드의 파워(전력) 환산. 편성 화면 정렬과 매치 화면 표시가 같은 식을 쓰게 하는 단일 지점이다.
//
// 파워 = 저작값 maxHp + bonusHp. 이 게임엔 공격력 필드가 없고 공격력이 hp에서 파생되므로(CardInstance.AttackDamage)
// 카드의 강함은 hp 하나로 표현된다. 런타임 부여분(시너지 덩치·돌보미)은 전투에 들어가야 생기니 여기서 알 수 없다.
public static class DeckPower
{
    // null 카드는 0. 빈 칸을 호출측이 거르지 않아도 되게 한다.
    public static int Of(CardData _card) => _card != null ? _card.maxHp + _card.bonusHp : 0;

    // 덱 전체 합. 빈 칸(null)과 미배선 덱(null)은 0으로 흡수한다.
    public static int Of(IReadOnlyList<CardData> _deck)
    {
        if (_deck == null) return 0;

        int t_sum = 0;
        for (int t_i = 0; t_i < _deck.Count; t_i++)
            t_sum += Of(_deck[t_i]);

        return t_sum;
    }
}
