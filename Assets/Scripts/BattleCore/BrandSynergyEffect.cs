using System.Collections.Generic;

// 낙인 시너지(덱 3/5장 활성). 순수 트리거형 — 정적 스탯 없음.
// 공격 개시 직전, 공격자 필드의 라이브 아군 낙인 카드 수(공격자 포함) × 티어 배율만큼 방어자에게 선피해.
// 값 규칙은 CardInstance.TakeDamage에 전량 위임(인라인 데미지 공식 금지). 결정론: RNG 미소비.
// 낙인(Brand) 효과 — OnBeforeAttack 선피해.
public class BrandSynergyEffect : SynergyEffect, IBeforeAttackPlanSource
{
    int damagePerMember = 1;

    public override bool TrySetParam(string _key, string _value)
    {
        if (_key != nameof(damagePerMember)) return false;
        this.damagePerMember = ParseInt(_value);
        return true;
    }

    public override bool TryGetParam(string _key, out int _value)
    {
        _value = this.damagePerMember;
        return _key == nameof(damagePerMember);
    }

    public override void OnBeforeAttack(BeforeAttackCtx _ctx)
    {
        BrandAttackPlan t_plan = BuildPlan(_ctx);
        if (t_plan == null) return;
        t_plan.defender.TakeDamage(t_plan.totalDamage);
    }

    public ISynergyPresentationPlan CaptureBeforeAttackPlan(BeforeAttackCtx _ctx)
    {
        return BuildPlan(_ctx);
    }

    BrandAttackPlan BuildPlan(BeforeAttackCtx _ctx)
    {
        if (_ctx.defender == null || !_ctx.defender.IsAlive || _ctx.ownField == null) return null;

        var t_brand = new List<CardInstance>();
        foreach (var t_card in _ctx.ownField.GetActiveCards())
        {
            if (t_card == null || !t_card.IsAlive) continue;
            if (SynergyApplier.BelongsTo(t_card, _ctx.synergy)) t_brand.Add(t_card);
        }

        int t_count = System.Math.Min(t_brand.Count, BattleFieldState.SlotCount);
        if (t_count <= 0) return null;
        int t_totalDamage = t_count * System.Math.Max(1, this.damagePerMember);

        return new BrandAttackPlan
        {
            self = _ctx.self,
            synergy = _ctx.synergy,
            ownField = _ctx.ownField,
            brandCards = t_brand,
            defender = _ctx.defender,
            totalDamage = t_totalDamage,
            appliedDamage = _ctx.defender.ClampDamage(t_totalDamage),
            hpBefore = _ctx.defender.hp,
            bonusHpBefore = _ctx.defender.bonusHp
        };
    }

}
