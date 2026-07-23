using Cysharp.Threading.Tasks;
using UnityEngine;

// 돌보미 시너지(덱 4장↑ 활성). 순수 스폰 트리거형 — 정적 스탯 없음.
// 돌보미 카드가 전장에 나올 때, 필드의 모든 돌보미(자신 포함)에게 amount만큼 Heal + bonusHp 부여.
// 회복/보너스HP 규칙은 CardInstance(Heal/GrantBonusHp)에 위임(단일 진실원). 결정론: RNG 미소비, 순수 산술.
[CreateAssetMenu(fileName = "CaretakerSynergyEffect", menuName = "Card Battle/Synergy Effect/Caretaker")]
public class CaretakerSynergyEffect : SynergyEffect
{
    [SerializeField] private int amount = 1;   // 스폰 시 돌보미 1인당 Heal량 + bonusHp 부여량

    // 동기 완결: 본문에 await 없이 상태변이 끝내고 CompletedTask 반환(디스패처 .Forget이라도 즉시 확정).
    public override UniTask OnEntered(SpawnCtx _ctx)
    {
        // 디스패처는 비소속 카드에도 발화하므로 스폰 주체가 돌보미일 때만 동작(소속 자기판정).
        if (_ctx.self == null || !_ctx.self.IsAlive || _ctx.field == null) return UniTask.CompletedTask;
        if (!SynergyApplier.BelongsTo(_ctx.self, _ctx.synergy)) return UniTask.CompletedTask;

        foreach (var t_card in _ctx.field.GetActiveCards())   // 자신 포함 라이브 슬롯 카드
        {
            if (t_card == null || !SynergyApplier.BelongsTo(t_card, _ctx.synergy)) continue;
            t_card.Heal(amount);
            t_card.GrantBonusHp(amount);
        }
        SynergyTriggers.Fire(_ctx.self, _ctx.synergy);   // 스폰 주체(self) 기준 1회 배너+배지 pop(동료 전원 반복 금지)
        return UniTask.CompletedTask;
    }
}
