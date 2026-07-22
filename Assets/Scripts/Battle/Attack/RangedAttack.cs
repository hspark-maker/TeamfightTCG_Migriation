using Cysharp.Threading.Tasks;
using UnityEngine;

public class RangedAttack : NormalAttack
{
    public override AttackResult Execute(CardInstance _attacker, CardInstance _defender,
                                         BattleField _attackerField, BattleField _defenderField,
                                         CardInstance _preSelectedSplash = null,
                                         bool? _forceCunningSwap = null)
    {
        int t_dmg = _attacker.AttackDamage();
        int t_actualDmg = _defender.ClampDamage(t_dmg);
        _defender.TakeDamage(t_dmg, true);   // 직격: 비늘 감소 대상(원거리는 무반격이라 반격 케이스 없음)
        AttackFlow.RunAttackedBy(_defender, _attacker, _defenderField);   // 패시브 OnAttackedBy + 성벽 반격(동기)
        _attacker.data.passive?.OnDealDamage(_attacker, t_actualDmg).Forget();
        CardPassive.Notify(_attacker, CardKeyword.Ranged);
        bool t_defKilled = _defender.hp == 0;
        RemoveDead(_attackerField, _attackerField);
        RemoveDead(_defenderField, _defenderField);
        var t_result = MakeResult(_attacker, t_defKilled);
        t_result.damageDealt = t_actualDmg;   // 주 대상 실제 적용 데미지(트리거용)
        t_result.attackerKeywords |= CardKeyword.Ranged;
        return t_result;
    }
}
