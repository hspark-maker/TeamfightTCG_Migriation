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

        // 연출 발사점 = 슬롯 순 첫 힐러(양 클라 동일 순서). 힐러가 여럿이어도 회복량은 1로 고정(종전 규칙).
        CardInstance t_healer = null;
        foreach (var t_c in this.field.GetActiveCards())
            if (t_c.data.HasKeyword(CardKeyword.Healer)) { t_healer = t_c; break; }
        if (t_healer == null) return;

        // 회복 적용은 지금 즉시(상태 = 동기), 표시는 투사체가 닿을 때(연출 = 비동기).
        // 상태 변경을 연출에 묶으면 프레임레이트 차이가 그대로 멀티 divergence가 된다.
        var t_healed = new List<(CardView view, int amount)>();
        foreach (var t_c in this.field.GetActiveCards())
        {
            if (t_c.data.HasKeyword(CardKeyword.Healer)) continue;
            int t_amount = t_c.Heal(1, _showEffect: false);   // 표기는 HealVfx가 도착 시점에 재생
            if (t_amount <= 0) continue;                      // 이미 만피 = 연출 대상 아님
            t_healed.Add((CardView.GetView(t_c), t_amount));
        }
        if (t_healed.Count == 0) return;

        foreach (var t_c in this.field.GetActiveCards())
            if (t_c.data.HasKeyword(CardKeyword.Healer))
                CardView.GetView(t_c)?.PlayKeywordGlow(CardKeyword.Healer).Forget();

        HealVfx.PlayHealBurst(CardView.GetView(t_healer), t_healed);
    }
}
