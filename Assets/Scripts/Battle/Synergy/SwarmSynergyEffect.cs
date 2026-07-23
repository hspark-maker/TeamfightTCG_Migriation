using Cysharp.Threading.Tasks;
using UnityEngine;

// 무리 시너지(덱 4장↑ 활성). 순수 트리거형 — 정적 스탯 없음.
// 공격 개시 직전, 공격자 필드의 라이브 아군 무리 카드 수(공격자 포함)만큼 방어자에게 선피해.
// 값 규칙은 CardInstance.TakeDamage에 전량 위임(인라인 데미지 공식 금지). 결정론: RNG 미소비.
// 무리(Swarm) 효과 — OnBeforeAttack 선피해. 덱 4장↑ 활성.
[CreateAssetMenu(fileName = "SwarmSynergyEffect", menuName = "Card Battle/Synergy Effect/Swarm")]
public class SwarmSynergyEffect : SynergyEffect
{
    public override UniTask OnBeforeAttack(BeforeAttackCtx _ctx)
    {
        if (_ctx.defender == null || !_ctx.defender.IsAlive || _ctx.ownField == null) return UniTask.CompletedTask;

        int t_count = 0;
        foreach (var t_card in _ctx.ownField.GetActiveCards())   // 슬롯 라이브 카드(공격자 포함)
        {
            if (t_card == null || !t_card.IsAlive) continue;
            if (SynergyApplier.BelongsTo(t_card, _ctx.synergy)) t_count++;
        }

        t_count = Mathf.Min(t_count, BattleField.SLOT_COUNT);   // 방어적 상한(아군 슬롯 ≤3 → 자연히 만족, 회귀 가드)
        if (t_count > 0)
        {
            _ctx.defender.TakeDamage(t_count, true);   // 선피해도 공격 직격 취급: 비늘 감소 대상. 규칙 전량 TakeDamage 위임.
            SynergyTriggers.Fire(_ctx.self, _ctx.synergy);   // 선피해 발동 시에만 배너+배지 pop(스팸 방지)
        }
        return UniTask.CompletedTask;
    }
}
