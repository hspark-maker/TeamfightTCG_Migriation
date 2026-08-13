using System.Collections.Generic;

// 카드팩 등장 확률 고지용 계산. 표시 전용이며 추첨에는 관여하지 않는다.
// 추첨 자체는 CardPackOpener가 소유하고, 여기는 그 추첨이 쓰는 풀(ResolvePool)을 같은 기준으로 읽어
// 비율만 낸다 — 고지와 실제가 갈리지 않게 풀 조회 창구를 하나로 유지한다.
public static class PackOdds
{
    // 한 팩의 카드별 등장 확률. 랭크 오버라이드가 걸린 팩은 현재 랭크 기준으로 해석된다(실제 추첨과 동일).
    // uniqueDraw 팩도 여기 값은 **1회 추첨 기준**이다 — 비복원은 뽑을 때마다 남은 풀이 줄어 매회 확률이
    // 달라지므로, 고지에는 첫 추첨 확률을 쓴다.
    public static List<PackOddsEntry> Resolve(CardPackData _pack)
    {
        var t_result = new List<PackOddsEntry>();
        if (_pack == null) return t_result;

        var t_pool = _pack.ResolvePool(RankManager.GetInfo().Grade);

        int t_sum = 0;
        for (int t_i = 0; t_i < t_pool.Count; t_i++)
            if (t_pool[t_i].card != null) t_sum += t_pool[t_i].EffectiveWeight;

        if (t_sum <= 0) return t_result;

        for (int t_i = 0; t_i < t_pool.Count; t_i++)
        {
            CardData t_card = t_pool[t_i].card;
            if (t_card == null) continue;

            int t_weight = t_pool[t_i].EffectiveWeight;
            t_result.Add(new PackOddsEntry(t_card, t_weight, (float)t_weight / t_sum));
        }

        // 높은 확률부터. 같으면 카드 id 순 — 랭크나 풀 순서가 바뀌어도 목록이 흔들리지 않게.
        t_result.Sort((a, b) => b.Rate != a.Rate
            ? b.Rate.CompareTo(a.Rate)
            : a.Card.id.CompareTo(b.Card.id));
        return t_result;
    }

    // 고지 문구용 백분율 문자열(소수점 2자리). 0.05% 미만도 0.00%로 죽지 않게 하한을 둔다.
    public static string FormatRate(float _rate)
    {
        float t_percent = _rate * 100f;
        if (t_percent > 0f && t_percent < 0.01f) return "0.01% 미만";
        return t_percent.ToString("0.00") + "%";
    }
}

public readonly struct PackOddsEntry
{
    public readonly CardData Card;
    public readonly int Weight;
    public readonly float Rate;   // 0~1

    public PackOddsEntry(CardData _card, int _weight, float _rate)
    {
        this.Card = _card;
        this.Weight = _weight;
        this.Rate = _rate;
    }
}
