using Cysharp.Threading.Tasks;
using UnityEngine;

// 시너지 효과 1건. CardPassive와 동형(추상 SO) — 효과=에셋으로 추가하는 개방-폐쇄 구조.
// 서브클래스/에셋을 새로 만들어 확장하며, 엔진 층(Applier)은 수정하지 않는다.
public abstract class SynergyEffect : ScriptableObject
{
    // 덱 확정 시 1회 스탯 적용(정적 효과). 순수 트리거형 효과는 no-op으로 두고 훅만 구현한다.
    public abstract void Apply(CardInstance card, SynergyState state);

    // 공격 직후 트리거(청소부 회복 등). 기본 no-op. SynergyTriggers.OnAfterAttack가 활성 시너지에 대해 발화.
    // synergy: 발동 notify 표시용(effectDescription). 게임규칙 무관 — UI/오디오 전용.
    public virtual UniTask OnAfterAttack(CardInstance self, int damageDealt, SynergyData synergy) => UniTask.CompletedTask;

    // 공격 개시 직전 트리거(무리 선피해 등). 데미지 해결(Execute) 전에 원자 완료돼야 함 → await 계약.
    // SynergyTriggers.OnBeforeAttack가 self(공격자) 소속 활성 시너지에 대해 발화.
    public virtual UniTask OnBeforeAttack(CardInstance self, CardInstance defender, BattleField field, SynergyData synergy) => UniTask.CompletedTask;

    // 피격 시 트리거(성벽 반격 등). 동기 void 계약 — RemoveDead 전 인라인 완료 필수(.Forget/UniTask 금지).
    // SynergyTriggers.OnAttackedBy가 self(방어자) 소속 활성 시너지에 대해 발화.
    // synergy: 발동 notify 표시용(effectDescription). 게임규칙 무관 — UI/오디오 전용.
    public virtual void OnAttackedBy(CardInstance self, CardInstance attacker, SynergyData synergy) { }

    // 스폰 시 트리거(돌보미 힐/흐름 스택). 동기 완결 계약 — 본문 await 없이 상태변이 완료(UniTask.CompletedTask 반환).
    // synergy: 자기 소속 판정용(BelongsTo). SynergyTriggers.OnSpawn은 field 활성 시너지 전체에 대해 발화(비소속 필터 안 함) →
    // 각 효과가 소속을 스스로 판정한다. 흐름은 비소속 신규 카드에도 flowBonus 상속을 걸어야 하므로 이 설계가 필수.
    public virtual UniTask OnSpawn(CardInstance self, BattleField field, SynergyData synergy) => UniTask.CompletedTask;

    // 사망 시 트리거(언데드 부활/유산 회복). 동기 void 계약 — RemoveDead가 이 발화 직후 IsAlive로 부활 게이팅(.Forget/UniTask 금지).
    // SynergyTriggers.OnDeath가 self(죽는 카드) 소속 활성 시너지에 대해서만 발화.
    // synergy: 발동 notify 표시용(effectDescription). 게임규칙 무관 — UI/오디오 전용.
    public virtual void OnDeath(CardInstance self, BattleField field, SynergyData synergy) { }

    // 턴 종료 시 트리거(유산 스택 적립). 동기 void 계약 — TurnRunner가 OnExit 직후 인라인 발화.
    // SynergyTriggers.OnTurnEnd가 self 소속 활성 시너지에 대해서만 발화.
    // synergy: 발동 notify 표시용(effectDescription). 게임규칙 무관 — UI/오디오 전용.
    public virtual void OnTurnEnd(CardInstance self, SynergyData synergy) { }
}
