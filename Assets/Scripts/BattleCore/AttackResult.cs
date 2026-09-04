using TeamfightTCG.BattleCore;

public struct AttackResult
{
    public bool defenderKilled;
    public bool canAttackAgain;
    public bool attackerSwapped;
    public CardInstance splashDefender;
    public CardKeyword attackerKeywords;
    public CardKeyword defenderKeywords;
    public int damageDealt;
    public int enhanceDamage;
    public BattleEvent[] events;

    /// <summary>주 대상이 이 공격으로 받은 총 피해(기본타 + 강화 추가타).</summary>
    public int TotalDamage => this.damageDealt + this.enhanceDamage;
}
