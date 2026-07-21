using Cysharp.Threading.Tasks;
using UnityEngine;

public class CunningAttack : NormalAttack
{
    public override AttackResult Execute(CardInstance _attacker, CardInstance _defender,
                                         BattleField _attackerField, BattleField _defenderField,
                                         CardInstance _preSelectedSplash = null,
                                         bool? _forceCunningSwap = null)
    {
        int t_atkDmg = _attacker.AttackDamage();
        int t_ctrDmg = _defender.AttackDamage();
        bool t_takesCounter  = _attacker.TakesCounterFrom(_defender);  // 반격 자격(단일 진실원): 원거리 무반격 + 표식 무반격
        bool t_markedCounter = _defender.HasKeyword(CardKeyword.Mark);
        int t_actualAtkDmg = _defender.ClampDamage(t_atkDmg);
        int t_actualCtrDmg = _attacker.ClampDamage(t_ctrDmg);

        bool t_shouldSwap = _forceCunningSwap ?? _attackerField.CanSwapWithWaiting(_attacker);

        _defender.TakeDamage(t_atkDmg);

        if (t_takesCounter)
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
