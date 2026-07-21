using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class PeerlessAttack : NormalAttack
{
    public static CardInstance PreSelect(int _targetSlot, BattleField _field)
        => PickSplash(_targetSlot, _field);

    public override AttackResult Execute(CardInstance _attacker, CardInstance _defender,
                                         BattleField _attackerField, BattleField _defenderField,
                                         CardInstance _preSelectedSplash = null,
                                         bool? _forceCunningSwap = null)
    {
        int t_origHp = _attacker.hp;
        int t_ctrDmg = GetDamage(_defender);  // 도발 시 50%

        int t_atkDmg = GetDamage(_attacker);
        int t_actualAtkDmg = Mathf.Min(t_atkDmg, _defender.hp + _defender.bonusHp);
        _defender.TakeDamage(t_atkDmg);
        _defender.data.passive?.OnAttackedBy(_defender, _attacker).Forget();
        if (!_defender.HasKeyword(CardKeyword.Mark))
            _attacker.TakeDamage(t_ctrDmg);
        _attacker.data.passive?.OnDealDamage(_attacker, t_actualAtkDmg).Forget();

        CardInstance t_splash = _preSelectedSplash ?? PickSplash(_defender.slotIndex, _defenderField);
        if (t_splash != null)
        {
            int t_dmg = Mathf.FloorToInt(t_origHp * 0.5f);
            if (t_dmg > 0)
                t_splash.TakeDamage(t_dmg);
        }

        bool t_defKilled = _defender.hp == 0;
        RemoveDead(_attackerField, _attackerField);
        RemoveDead(_defenderField, _defenderField);

        var t_result = MakeResult(_attacker, t_defKilled);
        t_result.splashDefender = t_splash;
        return t_result;
    }

    static CardInstance PickSplash(int _targetSlot, BattleField _field)
    {
        var t_adj = new List<int>();
        if (_targetSlot > 0 && _field.GetSlot(_targetSlot - 1) != null)
            t_adj.Add(_targetSlot - 1);
        if (_targetSlot < BattleField.SLOT_COUNT - 1 && _field.GetSlot(_targetSlot + 1) != null)
            t_adj.Add(_targetSlot + 1);
        if (t_adj.Count == 0) return null;
        return _field.GetSlot(t_adj[MatchRandom.Range(t_adj.Count)]);
    }
}
