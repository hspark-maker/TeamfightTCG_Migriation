using Cysharp.Threading.Tasks;
using UnityEngine;

public class RangedAttack : NormalAttack
{
    public override AttackResult Execute(CardInstance _attacker, CardInstance _defender,
                                         BattleField _attackerField, BattleField _defenderField,
                                         CardInstance _preSelectedSplash = null,
                                         bool? _forceCunningSwap = null)
    {
        int t_dmg = GetDamage(_attacker);
        int t_actualDmg = Mathf.Min(t_dmg, _defender.hp + _defender.bonusHp);
        _defender.TakeDamage(t_dmg);
        _defender.data.passive?.OnAttackedBy(_defender, _attacker).Forget();
        _attacker.data.passive?.OnDealDamage(_attacker, t_actualDmg).Forget();
        CardPassive.Notify(_attacker, CardKeyword.Ranged);
        bool t_defKilled = _defender.hp == 0;
        RemoveDead(_attackerField, _attackerField);
        RemoveDead(_defenderField, _defenderField);
        var t_result = MakeResult(_attacker, t_defKilled);
        t_result.attackerKeywords |= CardKeyword.Ranged;
        return t_result;
    }
}
