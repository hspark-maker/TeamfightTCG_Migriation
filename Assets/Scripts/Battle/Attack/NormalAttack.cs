using Cysharp.Threading.Tasks;
using UnityEngine;

public class NormalAttack : IAttackBehavior
{
    public virtual AttackResult Execute(CardInstance _attacker, CardInstance _defender,
                                        BattleField _attackerField, BattleField _defenderField,
                                        CardInstance _preSelectedSplash = null,
                                        bool? _forceCunningSwap = null)
    {
        int t_atkDmg = _attacker.AttackDamage();
        int t_ctrDmg = _defender.AttackDamage();  // 동시 해결: 공격 전 수치로 반격 (도발 시 50%)
        bool t_takesCounter  = _attacker.TakesCounterFrom(_defender);  // 반격 자격(단일 진실원)
        bool t_markedCounter = _defender.HasKeyword(CardKeyword.Mark);

        int t_actualAtkDmg = _defender.ClampDamage(t_atkDmg);          // 직격(공격): 비늘 감소 반영(기본 true)
        int t_actualCtrDmg = _attacker.ClampDamage(t_ctrDmg, false);   // 반격: 비늘 감소 없음(실제 TakeDamage(false)와 일치)

        _defender.TakeDamage(t_atkDmg, true);   // 직격: 비늘 감소 대상
        if (t_takesCounter)
            _attacker.TakeDamage(t_ctrDmg);   // 반격: 비늘 감소 없음(기본 false)

        AttackFlow.RunAttackedBy(_defender, _attacker, _defenderField);   // 패시브 OnAttackedBy + 성벽 반격(동기)
        if (t_takesCounter)
            _defender.data.passive?.OnDealDamage(_defender, t_actualCtrDmg, true).Forget();
        _attacker.data.passive?.OnDealDamage(_attacker, t_actualAtkDmg).Forget();

        bool t_defKilled = _defender.hp == 0;
        RemoveDead(_attackerField, _attackerField);
        RemoveDead(_defenderField, _defenderField);

        var t_result = MakeResult(_attacker, t_defKilled);
        t_result.damageDealt = t_actualAtkDmg;   // 주 대상 실제 적용 데미지(트리거용)
        if (t_markedCounter)
            t_result.defenderKeywords |= CardKeyword.Mark;
        return t_result;
    }

    protected static AttackResult MakeResult(CardInstance _attacker, bool _defKilled)
    {
        bool t_canAttack = _defKilled && _attacker.IsAlive && _attacker.HasKeyword(CardKeyword.Execution);
        return new AttackResult
        {
            defenderKilled = _defKilled,
            canAttackAgain = t_canAttack,
            attackerKeywords = t_canAttack ? CardKeyword.Execution : CardKeyword.None,
        };
    }

    protected static void RemoveDead(BattleField _field, BattleField _ownField)
    {
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            CardInstance t_c = _field.GetSlot(i);
            if (t_c != null && !t_c.IsAlive)
            {
                // 시너지 사망 트리거 먼저(언데드 부활 / 유산 아군 회복). 동기 완결 — 부활은 제자리 hp 복구.
                SynergyTriggers.OnDeath(t_c, _ownField);
                // 부활(언데드)했으면 슬롯 유지 → passive OnDeath/RemoveCard 스킵(라이프사이클 재진입 없음).
                if (t_c.IsAlive) continue;
                t_c.data.passive?.OnDeath(t_c, _ownField).Forget();
                _field.RemoveCard(i);
            }
        }
    }
}
