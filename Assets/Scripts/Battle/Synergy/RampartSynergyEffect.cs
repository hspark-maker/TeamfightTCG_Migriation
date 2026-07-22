using UnityEngine;

// 성벽 시너지(덱 2장↑ 활성). 순수 트리거형 — 정적 스탯 없음.
// 피격 시 공격자 체력이 방어자보다 클 때만, (attacker.hp - self.hp)/2(정수 내림)만큼 공격자에게 반격(hp만, bonusHp 제외).
// 산출식만 소유, 적용은 CardInstance.TakeDamage에 위임(단일 진실원). 반격이라 비늘 감소 없음(기본 false).
// 결정론: RNG 미소비, 순수 산술.
// 동기 void — SynergyTriggers.OnAttackedBy가 behavior의 counter 해결 직후 RemoveDead 전에 인라인 발화.
[CreateAssetMenu(fileName = "RampartSynergyEffect", menuName = "Card Battle/Synergy Effect/Rampart")]
public class RampartSynergyEffect : SynergyEffect
{
    public override void Apply(CardInstance card, SynergyState state) { }   // 정적 효과 없음(순수 트리거)

    public override void OnAttackedBy(CardInstance self, CardInstance attacker, SynergyData synergy)
    {
        if (self == null || !self.IsAlive || attacker == null || !attacker.IsAlive) return;   // self 사망 시 무반격

        if (attacker.hp <= self.hp) return;   // 방향성: 공격자 체력이 더 클 때만 반격(hp만, bonusHp 제외)
        int t_raw = (attacker.hp - self.hp) / 2;   // 체력차 절반(정수 내림)
        if (t_raw <= 0) return;

        attacker.TakeDamage(t_raw);   // 반격 적용 규칙 전량 TakeDamage 위임(기본 false → 비늘 감소 없음).
        SynergyTriggers.Fire(self, synergy);   // 반격 발동 시에만 배너+배지 pop(스팸 방지)
    }
}
