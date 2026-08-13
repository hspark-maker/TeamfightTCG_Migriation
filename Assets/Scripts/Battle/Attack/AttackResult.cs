public struct AttackResult
{
    public bool         defenderKilled;
    public bool         canAttackAgain;   // 처형: 처치 시 재공격
    public bool         attackerSwapped;  // 교활: 카드 교체 발생
    public CardInstance splashDefender;   // 무쌍: 광역 피해 대상
    public CardKeyword  attackerKeywords; // 발동된 공격자 키워드
    public CardKeyword  defenderKeywords; // 발동된 수비자 키워드 (e.g. 표식)
    public int          damageDealt;      // 주 대상 기본타의 실제 HP+보너스HP 감소량. 트리거(청소부 회복 등)용. struct 기본 0.
    public int          enhanceDamage;    // 일반 강화 추가타로 주 대상에 실제 적용된 데미지. 0 = 미발동.

    /// <summary>주 대상이 이 공격으로 받은 총 피해(기본타 + 강화 추가타). 표시·예측용.
    /// <see cref="damageDealt"/>를 합산으로 바꾸지 않는 이유: 그 값은 이미 [DamageDealt] 트리거가
    /// 기본타 시점에 소비한 뒤라, 합산으로 바꾸면 트리거가 본 값과 결과가 어긋난다.</summary>
    public int TotalDamage => this.damageDealt + this.enhanceDamage;
}
