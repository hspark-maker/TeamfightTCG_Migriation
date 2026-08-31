using Cysharp.Threading.Tasks;
using UnityEngine;

// 흐름 시너지(덱 4장↑ 활성). 순수 스폰 트리거형 — 정적 스탯 없음.
// 흐름 카드가 등장할 때마다 field.FlowStack+1(무제한 성장, Cunning 재진입/사망 교체 refill 포함).
// flowBonus는 **흐름 카드에만** FlowStack으로 세팅 → CardInstance.AttackDamage에 가산.
// 스택 1당 "흐름 카드가 공격으로 주는 데미지 +1"(비흐름 카드는 flowBonus=0, 영향 없음).
// 값 규칙은 CardInstance에 위임. RNG 미소비, 순수 산술.
// "등장"은 런타임 스폰(NotifyEntered)만 — 오프닝 배치(Placed)는 미발화(BattleField 스폰 경로가 게이팅).
[CreateAssetMenu(fileName = "FlowSynergyEffect", menuName = "Card Battle/Synergy Effect/Flow")]
public class FlowSynergyEffect : SynergyEffect
{
    [SerializeField] int amount = 1;

    public override bool TrySetParam(string _key, string _value)
    {
        if (_key != nameof(amount)) return false;
        this.amount = ParseInt(_value);
        return true;
    }


    // 동기 완결: 본문에 await 없이 상태변이 끝내고 CompletedTask 반환.
    public override UniTask OnEntered(SpawnCtx _ctx)
    {
        if (_ctx.self == null || _ctx.field == null) return UniTask.CompletedTask;

        // 흐름 카드 등장일 때만 발동. 비흐름 카드 등장은 무시(flowBonus 상속 없음).
        if (!SynergyApplier.BelongsTo(_ctx.self, _ctx.synergy)) return UniTask.CompletedTask;

        // 매 등장마다 스택 +1(Cunning 재진입 포함, 무제한 성장).
        _ctx.field.AddFlowStack(amount);
        // 흐름 카드들만 현재 스택으로 재동기(비흐름 카드는 건드리지 않음).
        foreach (var t_card in _ctx.field.GetActiveCards())
            if (t_card != null && SynergyApplier.BelongsTo(t_card, _ctx.synergy))
                t_card.flowBonus = _ctx.field.FlowStack;
        SynergyTriggers.Fire(_ctx.self, _ctx.synergy, _ctx.field); // 흐름 카드 등장 시 배너+배지 pop
        // 바람 스펙은 흐름 시너지의 연출 에셋이 소유. 타입 불일치면 null → 바람만 생략된다.
        SynergyVfx.PlayFlowWind(_ctx.field, _ctx.synergy?.vfx as FlowSynergyVfxConfig);
        return UniTask.CompletedTask;
    }

    // 공격 시에도 표시. flowBonus가 AttackDamage에 실제로 가산되는 순간이라 여기가 체감 지점이다.
    // (등장 트리거만 있으면 초기 3장이 안 바뀌는 판에서는 한 번도 안 보인다.)
    // 상태변이 없음 — 순수 표시. 스택 0이면 가산이 없으므로 스킵.
    public override UniTask OnBeforeAttack(BeforeAttackCtx _ctx)
    {
        if (_ctx.self == null || _ctx.self.flowBonus <= 0) return UniTask.CompletedTask;
        SynergyTriggers.Fire(_ctx.self, _ctx.synergy, _ctx.ownField);
        // 가산이 실제로 붙는 순간에도 바람. await 하지 않는다 — 여기서 기다리면 공격 개시가 밀린다.
        SynergyVfx.PlayFlowWind(_ctx.ownField, _ctx.synergy?.vfx as FlowSynergyVfxConfig);
        return UniTask.CompletedTask;
    }
}