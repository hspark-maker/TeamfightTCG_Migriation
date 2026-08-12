/// <summary>
/// 공격 프리뷰 수치 산출(순수 함수, UI 의존 0). 드래그 조준 시 표시할 숫자를 만든다.
/// 규칙은 전부 CardInstance에 위임 — 여기서 공식을 재구현하면 실제 전투와 갈라진다(과거 실제 버그).
/// 반환하는 damage는 피해 감소·무적·보너스HP를 모두 반영한 **실제 적용 예상값**이다.
/// 호출부는 다시 Clamp하지 않고 그대로 표시한다.
///
/// 의도적 미반영(프리뷰에 넣지 말 것):
/// - 무쌍 스플래시 — 대상 선정이 MatchRandom을 소비한다. 프리뷰에서 뽑으면 스트림이 오염되어 멀티 divergence.
/// 미반영이지만 넣을 수 있는 것(현재 갭, 별건):
/// - 무리(Swarm) 선피해 / 성벽(Rampart) 반격 — 둘 다 RNG 미소비 순수 산술이라 추가 가능.
/// </summary>
public readonly struct AttackPreview
{
    public readonly int  attackDamage;      // 방어자에게 표시할 실제 적용 예상 피해
    public readonly bool defenderWouldDie;
    public readonly bool hasCounter;        // 반격 발생 여부(false면 아래 둘은 무의미)
    public readonly int  counterDamage;     // 공격자에게 표시할 실제 적용 예상 반격
    public readonly bool attackerWouldDie;

    AttackPreview(int _attackDamage, bool _defenderWouldDie,
                  bool _hasCounter, int _counterDamage, bool _attackerWouldDie)
    {
        this.attackDamage     = _attackDamage;
        this.defenderWouldDie = _defenderWouldDie;
        this.hasCounter       = _hasCounter;
        this.counterDamage    = _counterDamage;
        this.attackerWouldDie = _attackerWouldDie;
    }

    /// <summary>_attacker가 _defender를 칠 때의 프리뷰 수치. 부작용 없음.</summary>
    public static AttackPreview Compute(CardInstance _attacker, CardInstance _defender)
    {
        int  t_atkRaw       = _attacker.AttackDamage();
        bool t_hasCounter   = _attacker.TakesCounterFrom(_defender);
        int  t_counterRaw   = t_hasCounter ? _defender.AttackDamage() : 0;
        int  t_thornRaw     = _defender.data?.passive?.ThornDamage ?? 0;

        // 실제 순서가 기본타 → 반격 → [Attacked](가시) → 강화 추가타이므로,
        // 공격자가 반격/가시에 쓰러지는지 먼저 계산해야 추가타를 정확히 켜고 끌 수 있다.
        (int t_counterApplied, _) = _attacker.PreviewDamageChain(t_counterRaw, 0, false);
        (_, bool t_attackerWouldDie) = _attacker.PreviewDamageChain(t_counterRaw, t_thornRaw, false);

        int t_enhanceRaw = _attacker.HasVanillaEnhance && !t_attackerWouldDie
            ? _attacker.VanillaEnhanceDamage()
            : 0;
        (int t_attackDamage, bool t_defenderWouldDie) =
            _defender.PreviewAttackChain(t_atkRaw, t_enhanceRaw);

        return new AttackPreview(t_attackDamage, t_defenderWouldDie,
                                 t_hasCounter, t_counterApplied, t_attackerWouldDie);
    }
}
