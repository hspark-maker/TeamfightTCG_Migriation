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
                t_c.hp = Mathf.Min(t_c.hp + 1, t_c.data.maxHp);
                t_healed = true;
            }

        if (!t_healed) return;

        foreach (var t_c in this.field.GetActiveCards())
            if (t_c.data.HasKeyword(CardKeyword.Healer))
                CardView.GetView(t_c)?.PlayKeywordGlow(CardKeyword.Healer).Forget();
    }
}
