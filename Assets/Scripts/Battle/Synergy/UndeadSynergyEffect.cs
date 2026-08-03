using UnityEngine;

// 언데드 시너지(덱 3장↑ 활성). 순수 사망 트리거형 — 정적 스탯 없음.
// 파괴되는 순간 체력 50%(최소 1)로 게임당 1회 부활. 부활 규칙은 CardInstance.ReviveAtHalf에 위임(단일 진실원).
// 동기 void — RemoveDead가 OnLethal 직후 IsAlive면 RemoveCard를 스킵(제자리 부활, 라이프사이클 재진입 없음).
// 디스패처가 self(죽는 카드) 소속에 대해서만 발화하므로 여기서 소속 재판정 불필요. RNG 미소비.
[CreateAssetMenu(fileName = "UndeadSynergyEffect", menuName = "Card Battle/Synergy Effect/Undead")]
public class UndeadSynergyEffect : SynergyEffect
{
    public override void OnLethal(DeathCtx _ctx)
    {
        if (_ctx.self == null) return;
        if (_ctx.self.ReviveAtHalf())   // 게임당 1회, 성공 시 제자리 hp 복구
            SynergyTriggers.Fire(_ctx.self, _ctx.synergy, _ctx.field);   // 부활 성공 시에만 배너+배지 pop(라벨=시너지 설명 통일)
    }
}
