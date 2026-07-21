public static class AttackProcessor
{
    public static AttackResult Execute(CardInstance _attacker, CardInstance _defender,
        BattleField _attackerField, BattleField _defenderField,
        CardInstance _preSelectedSplash = null,
        bool? _forceCunningSwap = null)
    {
        return AttackBehaviorFactory.Create(_attacker)
            .Execute(_attacker, _defender, _attackerField, _defenderField, _preSelectedSplash, _forceCunningSwap);
    }
}
