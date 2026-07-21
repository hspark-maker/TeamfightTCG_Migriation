using Cysharp.Threading.Tasks;
using UnityEngine;

public class CunningAttack : NormalAttack
{
    public override AttackResult Execute(CardInstance _attacker, CardInstance _defender,
                                         BattleField _attackerField, BattleField _defenderField,
                                         CardInstance _preSelectedSplash = null,
                                         bool? _forceCunningSwap = null)
    {
        int t_atkDmg = GetDamage(_attacker);
        int t_ctrDmg = GetDamage(_defender);
        bool t_markedCounter = _defender.HasKeyword(CardKeyword.Mark);
        int t_actualAtkDmg = Mathf.Min(t_atkDmg, _defender.hp + _defender.bonusHp);
        int t_actualCtrDmg = Mathf.Min(t_ctrDmg, _attacker.hp + _attacker.bonusHp);

        bool t_shouldSwap = _forceCunningSwap ?? _attackerField.CanSwapWithWaiting(_attacker);

        _defender.TakeDamage(t_atkDmg);

        if (!t_markedCounter && !_attacker.HasKeyword(CardKeyword.Ranged))
        {
            _attacker.TakeDamage(t_ctrDmg);
            _defender.data.passive?.OnDealDamage(_defender, t_actualCtrDmg, true).Forget();
        }

        _defender.data.passive?.OnAttackedBy(_defender, _attacker).Forget();
        _attacker.data.passive?.OnDealDamage(_attacker, t_actualAtkDmg).Forget();

        bool t_defKilled = _defender.hp == 0;
        CardInstance t_incoming = t_shouldSwap && _attacker.IsAlive
            ? _attackerField.SwapWithWaiting(_attacker)
            : null;
        bool t_swapped = t_incoming != null;
        if (t_swapped)
            _attacker.data.passive?.OnSwapOut(_attacker, t_incoming).Forget();

        RemoveDead(_attackerField, _attackerField);
        RemoveDead(_defenderField, _defenderField);

        var t_result = MakeResult(_attacker, t_defKilled);
        t_result.attackerSwapped = t_swapped;
        if (t_swapped)
            t_result.attackerKeywords |= CardKeyword.Cunning;
        if (t_markedCounter)
            t_result.defenderKeywords |= CardKeyword.Mark;
        return t_result;
    }
}
