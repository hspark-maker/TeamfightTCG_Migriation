using UnityEngine;

// 유산 시너지(덱 2장↑ 활성). 턴종료/사망 트리거형 — 정적 스탯 없음.
// 내 턴이 끝날 때마다 legacyStack+1. 파괴 시 legacyStack만큼 살아있는 아군(자신 제외) 전원 회복.
// 스택 적립/회복 규칙은 CardInstance(legacyStack/Heal)에 위임(단일 진실원). RNG 미소비, 순수 산술.
// 디스패처가 self 소속에 대해서만 발화하므로 여기서 소속 재판정 불필요.
[CreateAssetMenu(fileName = "LegacySynergyEffect", menuName = "Card Battle/Synergy Effect/Legacy")]
public class LegacySynergyEffect : SynergyEffect
{
    public override void Apply(CardInstance card, SynergyState state) { }   // 정적 효과 없음(순수 트리거)

    // 턴 종료: 소속 카드 스택 적립. TurnRunner가 OnExit 직후 인라인 발화.
    // 스택 적립은 상시 진행이라 notify 안 함(스팸 방지) — 표시는 사망 회복 발동 시점에만.
    public override void OnTurnEnd(CardInstance self, SynergyData synergy)
    {
        if (self == null) return;
        self.legacyStack++;
    }

    // 사망: 축적한 스택만큼 아군(자신 제외 라이브) 전원 회복. 스택 0이면 no-op.
    public override void OnDeath(CardInstance self, BattleField field, SynergyData synergy)
    {
        if (self == null || field == null || self.legacyStack <= 0) return;

        bool t_healed = false;
        foreach (var t_card in field.GetActiveCards())   // ownField 아군
        {
            if (t_card == null || t_card == self || !t_card.IsAlive) continue;
            t_card.Heal(self.legacyStack);
            t_healed = true;
        }
        if (t_healed)
            SynergyTriggers.Fire(self, synergy);   // 실제 회복 발생 시에만 배너+배지 pop
    }
}
