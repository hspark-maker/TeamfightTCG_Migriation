using System.Collections.Generic;
using Cysharp.Threading.Tasks;

// 낙인 시너지(덱 3/5장 활성). 순수 트리거형 — 정적 스탯 없음.
// 공격 개시 직전, 공격자 필드의 라이브 아군 낙인 카드 수(공격자 포함) × 티어 배율만큼 방어자에게 선피해.
// 값 규칙은 CardInstance.TakeDamage에 전량 위임(인라인 데미지 공식 금지). 결정론: RNG 미소비.
// 낙인(Brand) 효과 — OnBeforeAttack 선피해.
public class BrandSynergyEffect : SynergyEffect, IBeforeAttackPresentation
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

    public System.Func<UniTask> CaptureBeforeAttackPresentation(BeforeAttackCtx _ctx)
    {
        BrandAttackPlan t_plan = BuildPlan(_ctx);
        return t_plan == null ? null : () => PlayPresentation(t_plan);
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

        int t_count = System.Math.Min(t_brand.Count, BattleField.SLOT_COUNT);
        if (t_count <= 0) return null;
        int t_totalDamage = t_count * System.Math.Max(1, this.damagePerMember);

        return new BrandAttackPlan
        {
            ctx = _ctx,
            brandCards = t_brand,
            defender = _ctx.defender,
            totalDamage = t_totalDamage,
            appliedDamage = _ctx.defender.ClampDamage(t_totalDamage),
            hpBefore = _ctx.defender.hp,
            bonusHpBefore = _ctx.defender.bonusHp
        };
    }

    async UniTask PlayPresentation(BrandAttackPlan _plan)
    {
        BeforeAttackCtx t_ctx = _plan.ctx;
        // 선피해 발동 시에만 배너+배지 pop(스팸 방지). ownField를 넘기는 이유: 낙인 엠블럼은
        // AllMembers 범위라 "쏘는 낙인 전원"에게 떠야 한다 — 범위 해석에 필드가 필요하다.
        bool t_emblem = SynergyTriggers.Fire(t_ctx.self, t_ctx.synergy, t_ctx.ownField);
        if (!CardCatalog.TryGetSynergyData(t_ctx.synergy, out SynergyData t_presentation)) return;

        // 엠블럼이 다 뜨고 나서 볼리 → 본 공격. 겹쳐 돌리면 "낙인이 모였다"는 신호와 발사가 뭉쳐
        // 둘 다 안 읽힌다. 여기 대기는 표시 전용이다 — 상태는 위에서 이미 확정됐고 RNG도 안 쓴다.
        float t_wait = t_emblem
            ? SynergyEmblemVfx.DurationOf(t_presentation, SynergyEmblemTiming.Triggered) : 0f;
        if (t_wait > 0f)
            await UniTask.Delay((int)(t_wait * 1000)).SuppressCancellationThrow();

        // 낙인 카드들이 하나씩 투사체를 쏘고, 다 맞은 뒤에 본 공격 연출이 이어진다.
        // 이 await가 곧 "선피해 먼저, 공격 나중"의 표시 순서다 — 호출부(AttackFlow.RunBeforeAttack)가
        // AttackSequence.Play 앞에서 await 하므로 별도 배관이 필요 없다.
        var t_views = new List<CardView>(_plan.brandCards.Count);
        for (int i = 0; i < _plan.brandCards.Count; i++)
        {
            CardView t_view = CardView.GetView(_plan.brandCards[i]);
            if (t_view != null) t_views.Add(t_view);
        }
        // 연출 스펙은 그 시너지의 연출 에셋이 소유한다. 타입이 안 맞게 꽂혔으면 null → 볼리만 생략된다
        // (피해는 이미 적용된 뒤라 안전하다). 이 캐스트가 "낙인 데이터 ↔ 낙인 연출"의 유일한 접점이다.
        await BrandVolleyVfx.PlayVolley(t_views, CardView.GetView(_plan.defender),
                                  SplitDamage(_plan.appliedDamage, t_views.Count), _plan.hpBefore, _plan.bonusHpBefore,
                                  t_presentation.vfx as BrandSynergyVfxConfig);
    }

    sealed class BrandAttackPlan
    {
        public BeforeAttackCtx ctx;
        public List<CardInstance> brandCards;
        public CardInstance defender;
        public int totalDamage;
        public int appliedDamage;
        public int hpBefore;
        public int bonusHpBefore;
    }

    /// <summary>실제 적용된 총 피해를 발수만큼 정수로 쪼갠다. 나머지는 <b>앞쪽 발</b>에 얹는다 —
    /// 합이 총량과 정확히 같아야 숫자와 체력 감소가 맞물린다(발당 1 고정으로 두면 감소 적용 시 어긋난다).
    /// 총량이 발수보다 적으면 뒤쪽 발은 0 → 숫자 없이 파티클만 뜬다.</summary>
    static int[] SplitDamage(int _total, int _shots)
    {
        var t_out = new int[System.Math.Max(0, _shots)];
        if (t_out.Length == 0) return t_out;

        int t_base = _total / t_out.Length;
        int t_rem  = _total % t_out.Length;
        for (int i = 0; i < t_out.Length; i++)
            t_out[i] = t_base + (i < t_rem ? 1 : 0);
        return t_out;
    }
}
