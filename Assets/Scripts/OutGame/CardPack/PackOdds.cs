using System.Collections.Generic;

// 카드팩 등장 확률 고지용 계산. 표시 전용이며, 추첨(CardPackOpener)과 같은 풀 조회 창구를 써서 고지와 실제가 갈리지 않게 한다.
public static class PackOdds
{
    // 한 팩의 카드별 등장 확률(현재 랭크 기준). uniqueDraw 팩은 비복원이라 매회 잔여 풀이 줄지만, 고지 값은 첫 추첨 기준이다.
    public static List<PackOddsEntry> Resolve(CardPackData _pack)
    {
        var t_result = new List<PackOddsEntry>();
        if (_pack == null) return t_result;

        IReadOnlyList<WeightedCard> t_pool = _pack.ResolvePool(RankManager.CurrentGrade);

        int t_sum = 0;
        for (int t_i = 0; t_i < t_pool.Count; t_i++)
            if (t_pool[t_i].card != null) t_sum += t_pool[t_i].EffectiveWeight;

        if (t_sum <= 0) return t_result;

        for (int t_i = 0; t_i < t_pool.Count; t_i++)
        {
            CardData t_card = t_pool[t_i].card;
            if (t_card == null) continue;

            t_result.Add(new PackOddsEntry(t_card, (float)t_pool[t_i].EffectiveWeight / t_sum));
        }

        // 랭크나 풀 순서가 바뀌어도 목록이 흔들리지 않게 동률은 카드 id로 가른다.
        t_result.Sort((a, b) => b.Rate != a.Rate
            ? b.Rate.CompareTo(a.Rate)
            : a.Card.id.CompareTo(b.Card.id));
        return t_result;
    }

    // 고지 문구용 백분율 문자열(소수점 2자리). 0.005% 미만도 0.00%로 죽지 않게 하한을 둔다.
    public static string FormatRate(float _rate)
    {
        float t_percent = _rate * 100f;
        if (t_percent > 0f && t_percent < 0.01f) return "0.01% 미만";
        return t_percent.ToString("0.00") + "%";
    }
}

// 카드 1장의 등장 확률 한 줄
public readonly struct PackOddsEntry
{
    public readonly CardData Card;
    public readonly float Rate;   // 0~1

    public PackOddsEntry(CardData _card, float _rate)
    {
        Card = _card;
        Rate = _rate;
    }
}
