using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 전투 효과 공용 베이스. **훅 선언은 여기에만 있다** — <see cref="CardPassive"/>와
/// <see cref="SynergyEffect"/>는 이 클래스를 상속만 하고 훅을 다시 선언하지 않는다.
///
/// 두 시스템의 차이는 훅 목록이 아니라 **활성 조건**이다:
///   CardPassive   — 카드 고유, 카드당 1개, 항상 켜짐
///   SynergyEffect — 덱 조합, requiredCount 임계로 활성, BelongsTo 그룹
/// 예전엔 타이밍마다 한쪽만 구독할 수 있었는데 그건 원칙이 아니라 이력이었다
/// (TurnBegan은 passive만, TurnEnded는 synergy만인 식). 지금은 **아무 효과나 아무 타이밍이나** 쓴다.
///
/// 타이밍 의미·발화 위치·계약은 <see cref="BattleTimings"/>가 단독 소유한다 — 여기서 재설명하지 마라.
///
/// **계약은 반환 타입이 말한다.** 동기 void = "await 없이 그 자리에서 끝나야 한다"(순서 보장이 걸린 것),
/// UniTask = "연출을 await해도 된다". 단 .Forget으로 발화되는 것은 **첫 await 전에 상태변이 완결**.
/// **전 훅 MatchRandom 소비 금지.**
///
/// ctx.synergy = 이 발화를 일으킨 시너지(passive 발화면 null). 시너지 효과는 이걸로
/// SynergyApplier.BelongsTo 자기판정 + SynergyTriggers.Fire 배너 태그를 한다.
/// </summary>
public abstract class BattleEffect : ScriptableObject
{
    // ── 매치 / 배치 ──
    public virtual void    OnDeckResolved(DeckCtx _ctx) { }
    public virtual UniTask OnPlaced(SpawnCtx _ctx)   => UniTask.CompletedTask;
    public virtual UniTask OnEntered(SpawnCtx _ctx)  => UniTask.CompletedTask;

    // ── 턴 ──
    public virtual UniTask OnTurnBegan(TurnCtx _ctx) => UniTask.CompletedTask;
    public virtual void    OnTurnEnded(TurnCtx _ctx) { }

    // ── 공격 ──
    public virtual UniTask OnBeforeAttack(BeforeAttackCtx _ctx) => UniTask.CompletedTask;
    /// <summary>동기 void — 반격이 치사 래치보다 먼저 hp에 반영돼야 한다(BattleTimings ★ 참조).</summary>
    public virtual void    OnAttacked(AttackedCtx _ctx) { }
    public virtual UniTask OnDamageDealt(DamageDealtCtx _ctx) => UniTask.CompletedTask;
    public virtual UniTask OnSwappedOut(SwapOutCtx _ctx)      => UniTask.CompletedTask;
    public virtual UniTask OnAfterAttack(AfterAttackCtx _ctx) => UniTask.CompletedTask;

    // ── 사망 ──
    /// <summary>동기 void — 직후 IsAlive 게이트가 부활 여부를 본다. 여기서만 사망을 취소할 수 있다.</summary>
    public virtual void    OnLethal(DeathCtx _ctx) { }
    public virtual UniTask OnRemoved(DeathCtx _ctx) => UniTask.CompletedTask;

    // ── 보드 ──
    /// <summary>동기 void — 라이브 카운트 파생 상태 재동기용. 발화가 잦고 피해 계산 직전에도 걸린다.
    /// **여기서 보드를 바꾸지 마라(읽기 + 파생값 쓰기 전용).** RemoveCard가 이 훅을 부르는데,
    /// 그 호출은 AttackProcessor.RemoveDead의 슬롯 루프 안이다 — 여기서 스폰/제거를 하면
    /// 그 루프가 방금 들어온 카드를 이어서 지운다.</summary>
    public virtual void    OnBoardChanged(BoardCtx _ctx) { }

    // ── 조회 (훅 아님) ────────────────────────────────────────────────────
    // 아래 둘은 발화가 아니라 **질문**이다. <see cref="BattleOverForecast"/>가 공격 전에
    // "이 한 방으로 판이 끝나는가"를 계산하려면 각 효과가 뭘 할지 알아야 하는데,
    // 예측기가 그걸 직접 알아내려면 규칙을 복제해야 하고 그 순간 진실원이 둘이 된다.
    // 값의 주인은 여전히 각 효과다 — 예측기는 물어보기만 한다.

    /// <summary>이 효과가 이번 공격의 <b>치사 결과를 뒤집을 수 있는가</b>(부활, 사망 시 아군 회복 등).
    /// true면 예측기는 계산을 포기하고 "안 끝난다"로 답한다 — 헛나오는 연출보다 안 나오는 게 낫다.
    /// 사망을 취소하거나 죽을 카드를 살릴 수 있는 효과만 덮어쓴다.</summary>
    public virtual bool CanAlterLethalOutcome => false;

    /// <summary>이 카드가 <b>피격될 때</b> 공격자에게 되돌리는 추가 피해(가시). 0 = 없음.
    /// 실제 적용은 <see cref="OnAttacked"/>가 한다 — 여기는 같은 값을 예측이 읽어가는 창구일 뿐이라
    /// 두 곳이 같은 필드를 봐야 한다(숫자를 여기 새로 적지 마라).</summary>
    public virtual int ThornDamage => 0;
}
