import {CardSnapshot} from "../deckValidation";
import {MissionDef} from "./catalog";
import {MissionState, progressKey} from "./missionStore";

export const SYNERGY_BATTLE_MISSION_ID = "guide.16";
export const SYNERGY_BATTLE_EVENT = "Guide.CompleteSynergyBattle";

/**
 * 최초 승인된 덱과 당시 활성 미션으로 시너지 실전 집계 자격을 고정한다.
 * @param {MissionState} state 서버 미션 상태
 * @param {Array} catalog 발행된 미션 정의
 * @param {Array} snapshots 서버 성장 검증을 통과한 덱
 * @param {Array} cards 카드 표
 * @param {Array} tiers 시너지 티어 표
 * @return {boolean} 실전 완료를 집계할 자격
 */
export function qualifiesGuideSynergyBattle(
  state: MissionState, catalog: readonly MissionDef[], snapshots: readonly CardSnapshot[],
  cards: readonly Record<string, unknown>[], tiers: readonly Record<string, unknown>[],
): boolean {
  const active = catalog.filter((mission) => mission.enabled && mission.period === "guide" &&
    state.claimed[mission.id] !== true).sort((a, b) => a.sortOrder - b.sortOrder)[0];
  if (active?.id !== SYNERGY_BATTLE_MISSION_ID || active.event !== SYNERGY_BATTLE_EVENT) return false;

  const eligible = new Set(snapshots.filter((card) => card.synergyUnlocked).map((card) => card.cardId));
  const counts = new Map<string, Set<number>>();
  for (const card of cards) {
    const id = Number(card.id);
    if (!eligible.has(id) || typeof card.synergies !== "string") continue;
    for (const authored of card.synergies.split("/")) {
      const synergy = authored.trim().replace(/^Data_Synergy_/, "");
      if (!synergy) continue;
      const members = counts.get(synergy) ?? new Set<number>();
      members.add(id);
      counts.set(synergy, members);
    }
  }
  return tiers.some((tier) => {
    const required = Number(tier.requiredCount);
    return Number.isSafeInteger(required) && required > 0 &&
      (counts.get(String(tier.synergyId))?.size ?? 0) >= required;
  });
}

/**
 * 정상 정산 트랜잭션에서 승인 당시 자격만으로 영구 진행도를 반영한다.
 * @param {MissionState} state 정산할 미션 상태
 * @param {unknown} approval 참가자 최초 덱 승인 기록
 */
export function completeGuideSynergyBattle(state: MissionState, approval: unknown): void {
  if ((approval as {guideSynergyBattleEligible?: unknown} | null)?.guideSynergyBattleEligible !== true) return;
  state.progress[progressKey("guide", SYNERGY_BATTLE_EVENT)] = 1;
}
