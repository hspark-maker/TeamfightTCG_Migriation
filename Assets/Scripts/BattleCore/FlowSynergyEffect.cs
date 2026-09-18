using TeamfightTCG.BattleCore;


// 흐름 시너지(덱 4장↑ 활성). 순수 스폰 트리거형 — 정적 스탯 없음.
// 흐름 카드가 등장할 때마다 field.FlowStack+1(무제한 성장, Cunning 재진입/사망 교체 refill 포함).
// flowBonus는 **흐름 카드에만** FlowStack으로 세팅 → CardInstance.AttackDamage에 가산.
// 스택 1당 "흐름 카드가 공격으로 주는 데미지 +1"(비흐름 카드는 flowBonus=0, 영향 없음).
// 값 규칙은 CardInstance에 위임. RNG 미소비, 순수 산술.
// "등장" = 오프닝 배치(Placed) + 런타임 스폰(Entered) 전부 — 필드에 서는 모든 순간이 스택을 쌓는다
// (멀리건 스왑-인도 Placed 경로라 동일 발화. 스왑-아웃 카드가 나중에 Entered로 재입장하면 또 쌓인다 —
//  교활 재진입과 같은 "매 등장" 규칙).
public class FlowSynergyEffect : SynergyEffect
{
    int amount = 1;

    public override bool TrySetParam(string _key, string _value)
    {
        if (_key != nameof(amount)) return false;
        this.amount = ParseInt(_value);
        return true;
    }

    public override bool TryGetParam(string _key, out int _value)
    {
        _value = this.amount;
        return _key == nameof(amount);
    }

    // [Placed] 오프닝 배치(멀리건 스왑-인 포함). 디스패처가 self 소속만 발화하지만
    // 공용 몸통이 소속을 재판정하므로 그대로 태운다(멱등 아님 — 스택형이라 발화 1회 = +1).
    public override void OnPlaced(SpawnCtx _ctx) => AddStackAndResync(_ctx);

    // [Entered] 런타임 등장. **이 디스패처는 BelongsTo 필터를 안 걸므로** 소속을 직접 판정해야 한다.
    public override void OnEntered(SpawnCtx _ctx) => AddStackAndResync(_ctx);

    // 동기 완결: 메서드가 반환되기 전에 상태변이를 모두 끝낸다.
    void AddStackAndResync(SpawnCtx _ctx)
    {
        if (_ctx.self == null || _ctx.field == null) return;

        // 흐름 카드 등장일 때만 발동. 비흐름 카드 등장은 무시(flowBonus 상속 없음).
        if (!SynergyApplier.BelongsTo(_ctx.self, _ctx.synergy)) return;

        // 매 등장마다 스택 +1(Cunning 재진입 포함, 무제한 성장).
        _ctx.field.AddFlowStack(amount);
        // 흐름 카드들만 현재 스택으로 재동기(비흐름 카드는 건드리지 않음).
        foreach (var t_card in _ctx.field.GetActiveCards())
            if (t_card != null && SynergyApplier.BelongsTo(t_card, _ctx.synergy))
                t_card.flowBonus = _ctx.field.FlowStack;
        BattleEventStream.Emit(new BattleEvent(BattleEventKind.SynergyFired,
            _ctx.self.ownerIndex, _ctx.self.slotIndex));
        SynergyPresentationStream.Emit(new SynergyFirePlan
        {
            self = _ctx.self,
            synergy = _ctx.synergy,
            field = _ctx.field,
        }); // 흐름 카드 등장 시 배너+배지 pop
        // 등장 바람은 여기서 띄우지 않는다 — 배치 연출이 끝나는 뷰 시점(FlowSynergyVfxConfig.PlayPlaced)이
        // 유일한 발화점이다. 여기서도 띄우면 런타임 등장에서 같은 바람이 두 번 분다.
    }

    // 공격 시에도 표시. flowBonus가 AttackDamage에 실제로 가산되는 순간이라 여기가 체감 지점이다.
    // (등장 트리거만 있으면 초기 3장이 안 바뀌는 판에서는 한 번도 안 보인다.)
    // 상태변이 없음 — 순수 표시. 스택 0이면 가산이 없으므로 스킵.
    public override void OnBeforeAttack(BeforeAttackCtx _ctx)
    {
        if (_ctx.self == null || _ctx.self.flowBonus <= 0) return;
        BattleEventStream.Emit(new BattleEvent(BattleEventKind.SynergyFired,
            _ctx.self.ownerIndex, _ctx.self.slotIndex));
        SynergyPresentationStream.Emit(new FlowAttackPresentationPlan
        {
            self = _ctx.self,
            synergy = _ctx.synergy,
            field = _ctx.ownField,
        });
    }
}
