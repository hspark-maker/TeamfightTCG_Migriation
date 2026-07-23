using Cysharp.Threading.Tasks;
using UnityEngine;

// 청소부 시너지(덱 2장↑ 활성). 순수 트리거형 — 정적 스탯 없음.
// 각 청소부 카드가 공격 후 생존 시 준 피해의 절반(정수)만큼 체력 회복.
// 회복 규칙은 CardInstance.Heal에 위임(단일 진실원). 결정론: RNG 미소비, 순수 산술.
[CreateAssetMenu(fileName = "CleanerSynergyEffect", menuName = "Card Battle/Synergy Effect/Cleaner")]
public class CleanerSynergyEffect : SynergyEffect
{
    public override void OnDeckResolved(CardInstance _card, SynergyState _state) { }   // 정적 효과 없음(순수 트리거)

    public override UniTask OnAfterAttack(AfterAttackCtx _ctx, SynergyData _synergy)
    {
        int t_heal = _ctx.damageDealt / 2;   // 준 피해의 절반(정수 내림)
        if (t_heal <= 0) return UniTask.CompletedTask;
        _ctx.self.Heal(t_heal);
        SynergyTriggers.Fire(_ctx.self, _synergy);   // 회복 발동 시에만 배너+배지 pop(스팸 방지)
        return UniTask.CompletedTask;
    }
}
