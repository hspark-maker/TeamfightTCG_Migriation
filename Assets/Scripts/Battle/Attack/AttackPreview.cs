/// <summary>
/// 공격 프리뷰 수치 산출(순수 함수, UI 의존 0). 드래그 조준 시 표시할 숫자를 만든다.
/// 규칙은 전부 CardInstance에 위임 — 여기서 공식을 재구현하면 실제 전투와 갈라진다(과거 실제 버그).
/// 반환하는 damage는 **raw**다. 비늘 감소/보너스HP 소모는 CardInstance.PreviewAfterDamage가
/// 표시 시점에 폴딩하므로, 호출부는 이 값을 그대로 ShowAttackPreview에 넘긴다.
///
/// 의도적 미반영(프리뷰에 넣지 말 것):
/// - 무쌍 스플래시 — 대상 선정이 MatchRandom을 소비한다. 프리뷰에서 뽑으면 스트림이 오염되어 멀티 divergence.
/// 미반영이지만 넣을 수 있는 것(현재 갭, 별건):
/// - 무리(Swarm) 선피해 / 성벽(Rampart) 반격 — 둘 다 RNG 미소비 순수 산술이라 추가 가능.
/// </summary>
public readonly struct AttackPreview
{
    public readonly int  attackDamage;      // 방어자에게 표시할 raw 피해. 방어자 무적이면 0
    public readonly bool defenderWouldDie;
    public readonly bool hasCounter;        // 반격 발생 여부(false면 아래 둘은 무의미)
    public readonly int  counterDamage;     // 공격자에게 표시할 raw 반격. 공격자 무적이면 0
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
        int  t_atkRaw           = _attacker.AttackDamage();                    // 순수 함수 → 표시용·판정용 1회만 호출해 재사용
        bool t_defInvincible    = _defender.HasKeyword(CardKeyword.Invincible);
        int  t_attackDamage     = t_defInvincible ? 0 : t_atkRaw;
        bool t_defenderWouldDie = _defender.WouldDieFrom(t_atkRaw);            // 직격(공격): 비늘 감소 반영(기본 true)

        if (!_attacker.TakesCounterFrom(_defender))
            return new AttackPreview(t_attackDamage, t_defenderWouldDie, false, 0, false);

        int  t_defRaw           = _defender.AttackDamage();
        bool t_atkInvincible    = _attacker.HasKeyword(CardKeyword.Invincible);
        int  t_counterDamage    = t_atkInvincible ? 0 : t_defRaw;
        // 반격 맥락: 비늘 감소 없음(false). 실제 반격 TakeDamage(false)와 사망 프리뷰/HP표시 일치.
        bool t_attackerWouldDie = _attacker.WouldDieFrom(t_defRaw, false);

        return new AttackPreview(t_attackDamage, t_defenderWouldDie, true, t_counterDamage, t_attackerWouldDie);
    }
}
