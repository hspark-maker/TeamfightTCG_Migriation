using UnityEngine;

/// <summary>
/// 시너지 효과 1건. 활성 조건 = "덱에 이 시너지 카드가 requiredCount 이상"(덱 확정 시 1회 판정, 전투 중 불변).
/// 효과=에셋으로 추가하는 개방-폐쇄 구조 — 서브클래스/에셋만 늘리고 엔진층은 수정하지 않는다.
///
/// **훅은 <see cref="BattleEffect"/>에 선언돼 있다** — 여기엔 아무것도 추가하지 않는다.
/// CardPassive와 훅 목록·시그니처가 완전히 동일하고, 다른 건 활성 조건뿐이다.
/// 어느 타이밍이 있는지·계약이 뭔지는 <see cref="BattleTimings"/>를 봐라.
///
/// 시너지 효과가 쓰는 것:
/// - `_ctx.synergy` — 이 발화를 일으킨 시너지. SynergyApplier.BelongsTo 자기판정 +
///   SynergyTriggers.Fire 배너 태그에 쓴다. (passive 발화면 null이라 시너지 효과엔 항상 값이 있다.)
/// - 디스패처가 BelongsTo 필터를 걸어주는 훅과 그렇지 않은 훅이 섞여 있다 —
///   SynergyTriggers 쪽 주석을 확인하고, 필터가 없으면 효과가 스스로 판정할 것.
/// </summary>
public abstract class SynergyEffect : BattleEffect
{
}
