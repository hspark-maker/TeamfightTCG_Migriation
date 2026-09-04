using System.Collections.Generic;

// 돌보미 시너지(덱 4장↑ 활성). 순수 스폰 트리거형 — 정적 스탯 없음.
// 돌보미 카드가 전장에 나올 때, 필드의 모든 돌보미(자신 포함)에게 amount만큼 Heal + bonusHp 부여.
// 회복/보너스HP 규칙은 CardInstance(Heal/GrantBonusHp)에 위임(단일 진실원). 결정론: RNG 미소비, 순수 산술.
public class CaretakerSynergyEffect : SynergyEffect
{
    private int amount = 1;   // 스폰 시 돌보미 1인당 Heal량 + bonusHp 부여량

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

    // 동기 완결: 메서드가 반환되기 전에 상태변이를 모두 끝낸다.
    public override void OnEntered(SpawnCtx _ctx)
    {
        // 디스패처는 비소속 카드에도 발화하므로 스폰 주체가 돌보미일 때만 동작(소속 자기판정).
        if (_ctx.self == null || !_ctx.self.IsAlive || _ctx.field == null) return;
        if (!SynergyApplier.BelongsTo(_ctx.self, _ctx.synergy)) return;

        // 회복/보너스는 **여기서 전부 즉시**(상태 = 동기), 표시는 엠블럼과 같은 순간에(연출 = 비동기).
        // 상태 변경을 연출에 묶으면 프레임레이트 차이가 그대로 멀티 divergence가 된다(HealerEffect와 같은 규약).
        // _showEffect: false — "+N"과 HP 굴림은 아래 표시 묶음이 낸다(즉시 재생하면 두 번 뜬다).
        var t_healed = new List<SynergyHealTarget>();
        foreach (var t_card in _ctx.field.GetActiveCards())   // 자신 포함 라이브 슬롯 카드
        {
            if (t_card == null || !SynergyApplier.BelongsTo(t_card, _ctx.synergy)) continue;

            int t_amount = t_card.Heal(amount, _showEffect: false);
            t_card.GrantBonusHp(amount);

            // 만피라 회복량이 0이어도 bonusHp는 붙었다 → 연출 대상에 남긴다(숫자는 0이면 자동으로 숨는다).
            t_healed.Add(new SynergyHealTarget(t_card, t_amount));
        }

        // 표시는 전부 **등장 카드가 슬롯에 내려앉은 뒤**다. 규칙(NotifyEntered)은 뷰가 덱에서 날아오기 전에
        // 끝나므로, 여기서 바로 내면 카드가 아직 중앙을 날고 있는데 엠블럼과 숫자만 슬롯에서 터진다.
        // 엠블럼(Fire)과 회복 표기를 같은 예약에 담아 순서(엠블럼 → 숫자)와 시점을 함께 고정한다.
        //
        // 회복 투사체(HealVfx)는 쓰지 않는다 — 돌보미는 날아가 닿는 그림이 아니라 전원이 동시에 돌봄을
        // 받는 그림이라 도착을 기다릴 대상이 없다.
        SynergyPresentationStream.Emit(new CaretakerPresentationPlan
        {
            self = _ctx.self,
            synergy = _ctx.synergy,
            field = _ctx.field,
            targets = t_healed,
        });
    }
}
