using UnityEngine;

// 성벽 시너지(덱 2장↑ 활성). 순수 파생 상태형 — 정적 스탯 없음.
// **공격으로 받는 피해가 필드의 라이브 성벽 아군 수만큼 감소.**
//
// "필드의 수"라 라이브 보드 집계다(무리와 같은 성격). 그런데 감소는 트리거 시점이 아니라
// 피해 계산 시점에 필요한데 CardInstance.EffectiveDamage는 필드를 모른다.
// → 흐름(flowBonus)과 같은 패턴으로 CardInstance.rampartReduction 파생 상태를 두고,
//   보드 구성이 바뀔 때마다(BoardChanged) 재동기한다.
//
// 값 규칙은 CardInstance.EffectiveDamage가 단독 소유(여기서 감산식 재구현 금지).
// 반격/가시 등 비-직격 피해는 감소 대상이 아니다(isAttackHit=false 경로).
// 결정론: RNG 미소비, 순수 카운트. 양 클라가 동일 BoardChanged 경로로 같은 값을 얻는다.
[CreateAssetMenu(fileName = "RampartSynergyEffect", menuName = "Card Battle/Synergy Effect/Rampart")]
public class RampartSynergyEffect : SynergyEffect
{
    // 피격 시 표시. BoardChanged는 배치·등장·제거마다 터지고 수치가 안 변해도 불리므로
    // 거기서 배너를 띄우면 스팸이 된다 → 감소가 실제로 일하는 **피격 순간**에만 띄운다.
    // 상태변이 없음(순수 표시). 디스패처가 self 소속만 발화하므로 소속 재판정 불필요.
    public override void OnAttacked(AttackedCtx _ctx)
    {
        if (_ctx.self == null || _ctx.self.rampartReduction <= 0) return;
        SynergyTriggers.Fire(_ctx.self, _ctx.synergy);
    }

    public override void OnBoardChanged(BoardCtx _ctx)
    {
        if (_ctx.field == null) return;

        var t_cards = _ctx.field.GetActiveCards();

        int t_count = 0;
        foreach (var t_card in t_cards)
            if (t_card != null && t_card.IsAlive && SynergyApplier.BelongsTo(t_card, _ctx.synergy))
                t_count++;

        // 성벽 카드에만 세팅. 비-성벽은 0으로 되돌린다 —
        // 교활 스왑 등으로 성벽 카드가 빠지고 다른 카드가 그 자리에 와도 값이 잔류하지 않게.
        // **이 에셋은 SynergyData 하나에만 물려라.** 대입(=)으로 리셋하므로 서로 다른 시너지 둘이
        // 같은 에셋을 공유하면 뒤에 발화한 쪽이 앞쪽 소속 카드의 값을 0으로 지운다.
        foreach (var t_card in t_cards)
            if (t_card != null)
                t_card.rampartReduction = SynergyApplier.BelongsTo(t_card, _ctx.synergy) ? t_count : 0;
    }
}
