using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 시너지 효과 1건. 활성 조건 = "덱에 이 시너지 카드가 requiredCount 이상"(덱 확정 시 1회 판정, 전투 중 불변).
/// 효과=에셋으로 추가하는 개방-폐쇄 구조 — 서브클래스/에셋만 늘리고 엔진층은 수정하지 않는다.
///
/// 훅 이름·컨텍스트·계약은 <see cref="BattleTimings"/>의 타이밍 택소노미를 따른다 — 새 이름을 만들지 마라.
/// CardPassive와 **같은 컨텍스트 타입**을 쓴다. 추가로 받는 SynergyData는 발동 표시용(effectDescription)이고
/// 게임 규칙과 무관하다 — SynergyTriggers.Fire에 그대로 넘긴다.
///
/// 카드 고유·상시 효과는 이쪽이 아니라 CardPassive다.
/// </summary>
public abstract class SynergyEffect : ScriptableObject
{
    /// <summary>[DeckResolved] 덱 확정 시 1회 정적 스탯 적용. 동기.
    /// SynergyApplier가 ClearSynergy 선행 후 호출 → 멱등. 순수 트리거형 효과는 빈 몸통으로 둔다.</summary>
    public abstract void OnDeckResolved(CardInstance _card, SynergyState _state);

    /// <summary>[BeforeAttack] 공격 개시 직전(무리 선피해 등). self=공격자.
    /// 데미지 해결(AttackProcessor.Execute) 전에 원자 완료돼야 함 → await 계약.
    /// **MatchRandom 소비 금지** — 스플래시 선-소비 스트림을 교란한다.</summary>
    public virtual UniTask OnBeforeAttack(BeforeAttackCtx _ctx, SynergyData _synergy) => UniTask.CompletedTask;

    /// <summary>[Attacked] 피격 반응(성벽 반격 등). self=방어자.
    /// **동기 void 계약** — 반격이 치사 래치보다 먼저 hp에 반영돼야 한다(.Forget/UniTask 금지).</summary>
    public virtual void OnAttacked(AttackedCtx _ctx, SynergyData _synergy) { }

    /// <summary>[Entered] 런타임 등장(돌보미 힐 / 흐름 스택). **오프닝 배치엔 발화하지 않는다**(등장=런타임 스폰만).
    /// 동기 완결 계약 — 본문에서 await 없이 상태변이를 끝낸다(.Forget 발화라도 시점 확정).
    /// 디스패처가 BelongsTo 필터를 **걸지 않고** 발화한다 → 각 효과가 소속을 스스로 판정할 것.
    /// (흐름은 비소속 신규 카드 등장에도 flowBonus 상속을 걸어야 하므로 이 설계가 필수.)</summary>
    public virtual UniTask OnEntered(SpawnCtx _ctx, SynergyData _synergy) => UniTask.CompletedTask;

    /// <summary>[Lethal] 치사 확정 — **취소 가능 창**(언데드 ReviveAtHalf).
    /// **동기 void 계약** — 디스패치 직후 IsAlive로 부활 게이팅하므로 여기서 await하면 게이트가 헛돈다.
    /// 여기서 부활시키면 passive의 Removed와 슬롯 제거가 통째로 스킵된다.</summary>
    public virtual void OnLethal(DeathCtx _ctx, SynergyData _synergy) { }

    /// <summary>[TurnEnded] 턴 종료(유산 스택 적립). 동기 void 계약 — TurnRunner가 OnExit 직후 인라인 발화.</summary>
    public virtual void OnTurnEnded(TurnCtx _ctx, SynergyData _synergy) { }

    /// <summary>[AfterAttack] 공격 완료(청소부 회복 등). self=공격자. await 계약. 처치 판정은 ctx.defenderKilled.
    /// **주의: defenderKilled는 치사 래치값(Lethal 전에 확정)이라 언데드가 부활해도 true다.**
    /// "hp를 0으로 만들었나"이지 "실제로 사라졌나"가 아니다. 후자가 필요하면 ctx.target.IsAlive를 따로 봐라.</summary>
    public virtual UniTask OnAfterAttack(AfterAttackCtx _ctx, SynergyData _synergy) => UniTask.CompletedTask;
}
