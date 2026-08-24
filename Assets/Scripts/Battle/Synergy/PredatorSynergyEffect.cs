using Cysharp.Threading.Tasks;
using UnityEngine;

// 포식자 시너지. 각 포식자 카드가 공격 후 생존 시 실제로 준 기본 공격 피해의 일정 비율만큼 회복.
// 회복 규칙은 CardInstance.Heal에 위임(단일 진실원). 결정론: RNG 미소비, 순수 산술.
[CreateAssetMenu(fileName = "PredatorSynergyEffect", menuName = "Card Battle/Synergy Effect/Predator")]
public class PredatorSynergyEffect : SynergyEffect
{
    [SerializeField, Range(0, 100)] int lifestealPercent = 50;

    public override UniTask OnAfterAttack(AfterAttackCtx _ctx)
    {
        int t_heal = Mathf.FloorToInt(_ctx.damageDealt * (this.lifestealPercent / 100f));
        if (t_heal <= 0) return UniTask.CompletedTask;
        _ctx.self.Heal(t_heal);
        SynergyTriggers.Fire(_ctx.self, _ctx.synergy, _ctx.ownField);   // 회복 발동 시에만 배너+배지 pop(스팸 방지)
        return UniTask.CompletedTask;
    }
}
