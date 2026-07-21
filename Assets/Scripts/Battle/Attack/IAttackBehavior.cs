public interface IAttackBehavior
{
    AttackResult Execute(CardInstance _attacker, CardInstance _defender,
                         BattleField _attackerField, BattleField _defenderField,
                         CardInstance _preSelectedSplash = null,
                         bool? _forceCunningSwap = null);
}

public struct AttackResult
{
    public bool         defenderKilled;
    public bool         canAttackAgain;   // 처형: 처치 시 재공격
    public bool         attackerSwapped;  // 교활: 카드 교체 발생
    public CardInstance splashDefender;   // 무쌍: 광역 피해 대상
    public CardKeyword  attackerKeywords; // 발동된 공격자 키워드
    public CardKeyword  defenderKeywords; // 발동된 수비자 키워드 (e.g. 표식)
}
