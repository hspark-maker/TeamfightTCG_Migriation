using System.Collections.Generic;
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

        // 회복/보너스는 **여기서 전부 즉시**(상태 = 동기), 표시는 투사체가 닿을 때(연출 = 비동기).
        // 상태 변경을 연출에 묶으면 프레임레이트 차이가 그대로 멀티 divergence가 된다(HealerEffect와 같은 규약).
        // _showEffect: false — "+N"은 HealVfx가 도착 시점에 재생한다(즉시 재생하면 두 번 뜬다).
        var t_healed = new List<(CardView view, int amount)>();
        foreach (var t_card in _ctx.field.GetActiveCards())   // 자신 포함 라이브 슬롯 카드
        {
            if (t_card == null || !SynergyApplier.BelongsTo(t_card, _ctx.synergy)) continue;

            int t_amount = t_card.Heal(amount, _showEffect: false);
            t_card.GrantBonusHp(amount);

            // 만피라 회복량이 0이어도 bonusHp는 붙었다 → 연출 대상에 남긴다(숫자는 0이면 자동으로 숨는다).
            CardView t_view = CardView.GetView(t_card);
            if (t_view != null) t_healed.Add((t_view, t_amount));
        }

        SynergyTriggers.Fire(_ctx.self, _ctx.synergy, _ctx.field);   // 스폰 주체(self) 기준 1회 배너+배지 pop(동료 전원 반복 금지)

        // 힐러와 같은 연출을 재사용 — 회복이면 경로 불문 같은 그림이어야 한다.
        // 발사 주체는 스폰한 돌보미(self). 자기 자신도 대상이라 짧은 호를 그리며 되돌아온다.
        if (t_healed.Count > 0)
            HealVfx.PlayHealBurst(CardView.GetView(_ctx.self), t_healed);

        return UniTask.CompletedTask;
    }
}
