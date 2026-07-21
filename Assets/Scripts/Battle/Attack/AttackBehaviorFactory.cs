public static class AttackBehaviorFactory
{
    static readonly IAttackBehavior normal   = new NormalAttack();
    static readonly IAttackBehavior ranged   = new RangedAttack();
    static readonly IAttackBehavior peerless = new PeerlessAttack();
    static readonly IAttackBehavior cunning  = new CunningAttack();

    public static IAttackBehavior Create(CardInstance _attacker)
    {
        if (_attacker.HasKeyword(CardKeyword.Cunning))  return cunning;
        if (_attacker.HasKeyword(CardKeyword.Ranged))   return ranged;
        if (_attacker.HasKeyword(CardKeyword.Peerless)) return peerless;
        return normal;
    }
}
