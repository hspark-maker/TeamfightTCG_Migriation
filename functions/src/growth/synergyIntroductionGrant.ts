import {BASE_LEVEL, GrowthEntries, levelOfCard} from "./cardGrowth";
import {MissionDef} from "../missions/catalog";
import {MissionState, progressKey} from "../missions/missionStore";

export const SYNERGY_INTRODUCTION_LEVEL = BASE_LEVEL + 2;

/** Only the first selected card may receive the remaining introduction evolutions.
 * @param {MissionState} state Server mission state
 * @param {Array} catalog Published mission definitions
 * @param {number[]} owned Server-owned cards
 * @param {GrowthEntries} entries Committed growth
 * @param {number} cardId Requested card
 * @param {unknown} grant Previously committed introduction grant
 * @return {boolean} Free evolution is authorized
 */
export function canEnhanceSynergyIntroduction(
  state: MissionState, catalog: readonly MissionDef[], owned: readonly number[],
  entries: GrowthEntries, cardId: number, grant: unknown,
): boolean {
  const active = catalog.filter((mission) => mission.enabled && mission.period === "guide" &&
    state.claimed[mission.id] !== true).sort((a, b) => a.sortOrder - b.sortOrder)[0];
  if (active?.id !== "guide.05" || active.event !== "Guide.EvolveCompleted" ||
    (state.progress[progressKey("guide", active.event)] ?? 0) >= active.target) return false;
  if (!owned.includes(cardId) || owned.some((id) => levelOfCard(entries, id) >= SYNERGY_INTRODUCTION_LEVEL)) {
    return false;
  }
  if (grant === undefined) return true;
  if (grant === null || typeof grant !== "object" || Array.isArray(grant)) return false;
  const previous = grant as {cardId?: unknown; level?: unknown};
  return previous.cardId === cardId && typeof previous.level === "number" &&
    Number.isInteger(previous.level) && previous.level > BASE_LEVEL &&
    previous.level < SYNERGY_INTRODUCTION_LEVEL && levelOfCard(entries, cardId) >= previous.level;
}
