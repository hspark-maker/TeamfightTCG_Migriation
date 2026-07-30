using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class HealerEffect
{
    readonly BattleField field;

    public HealerEffect(BattleField _field)
    {
        this.field = _field;
        TurnEvents.TurnStarted += Handle;
    }

    public void Unsubscribe() => TurnEvents.TurnStarted -= Handle;

    void Handle(BattleField _field)
    {
        if (_field != this.field) return;

        // 필드의 힐러 전부(슬롯 오름차순 = 양 클라 동일 순서). **힐러 하나당 1 회복** —
        // 예전엔 첫 힐러만 골라 총 1만 회복해서 2장을 깔아도 효과가 하나였다.
        var t_healers = new List<CardInstance>();
        foreach (var t_c in this.field.GetActiveCards())
            if (t_c.data.HasKeyword(CardKeyword.Healer)) t_healers.Add(t_c);
        if (t_healers.Count == 0) return;

        // 힐러별로 순차 적용. 회복 적용은 지금 즉시(상태 = 동기), 표시는 투사체가 닿을 때(연출 = 비동기).
        // 상태 변경을 연출에 묶으면 프레임레이트 차이가 그대로 멀티 divergence가 된다.
        // 순회 순서(슬롯 순 힐러 × 슬롯 순 대상)가 고정이라 양 클라 결과가 같다. RNG 미소비.
        foreach (var t_healer in t_healers)
        {
            var t_healed = new List<(CardView view, int amount)>();
            foreach (var t_c in this.field.GetActiveCards())
            {
                if (t_c.data.HasKeyword(CardKeyword.Healer)) continue;   // 힐러는 회복 대상 아님(종전 규칙 유지)
                int t_amount = t_c.Heal(1, _showEffect: false);   // 표기는 HealVfx가 도착 시점에 재생
                if (t_amount <= 0) continue;                      // 이미 만피 = 연출 대상 아님
                t_healed.Add((CardView.GetView(t_c), t_amount));
            }

            // 아무도 회복되지 않았으면(전원 만피) 글로우·투사체 모두 생략 — 빈 연출이 번쩍이지 않게.
            if (t_healed.Count == 0) continue;

            CardView.GetView(t_healer)?.PlayKeywordGlow(CardKeyword.Healer).Forget();
            HealVfx.PlayHealBurst(CardView.GetView(t_healer), t_healed);
        }
    }
}
