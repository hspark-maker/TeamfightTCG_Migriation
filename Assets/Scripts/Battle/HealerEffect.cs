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

        bool t_hasHealer = false;
        foreach (var t_c in this.field.GetActiveCards())
            if (t_c.data.HasKeyword(CardKeyword.Healer)) { t_hasHealer = true; break; }
        if (!t_hasHealer) return;

        bool t_healed = false;
        foreach (var t_c in this.field.GetActiveCards())
            if (!t_c.data.HasKeyword(CardKeyword.Healer))
            {
                t_c.Heal(1);   // 회복 단일 진실원 위임(동작 동일: Min(hp+1, maxHp))
                t_healed = true;
            }

        if (!t_healed) return;

        foreach (var t_c in this.field.GetActiveCards())
            if (t_c.data.HasKeyword(CardKeyword.Healer))
                CardView.GetView(t_c)?.PlayKeywordGlow(CardKeyword.Healer).Forget();
    }
}
