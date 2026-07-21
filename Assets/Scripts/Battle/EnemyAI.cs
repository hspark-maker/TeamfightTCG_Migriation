using System.Collections.Generic;
using UnityEngine;

public static class EnemyAI
{
    public static (CardInstance attacker, CardInstance target) PickAction(
        BattleField _aiField, BattleField _playerField)
    {
        List<CardInstance> t_attackers = _aiField.GetActiveCards();
        List<CardInstance> t_targets   = _playerField.GetActiveCards();
        if (t_attackers.Count == 0 || t_targets.Count == 0) return (null, null);

        CardInstance t_atk = t_attackers[Random.Range(0, t_attackers.Count)];
        CardInstance t_def = t_targets[Random.Range(0, t_targets.Count)];
        return (t_atk, t_def);
    }
}
