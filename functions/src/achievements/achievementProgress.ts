import type {CardSnapshot} from "../deckValidation";
import {AchievementDef, achievementProgressKey, SYNERGY_ID} from "./achievementCatalog";

export interface AchievementState {
  revision: number;
  progress: Record<string, number>;
  claimed: Record<string, boolean>;
  currentWinStreak: number;
}

export function achievementCount(value: unknown): number {
  return typeof value === "number" && Number.isSafeInteger(value) && value > 0 ? value : 0;
}

// Compute active starting-deck synergies from server-approved growth and match-pinned tables.
export function activeAchievementSynergies(
  snapshots: readonly CardSnapshot[], cards: readonly Record<string, unknown>[],
  tiers: readonly Record<string, unknown>[],
): string[] {
  const eligible = new Set(snapshots.filter((card) => card.synergyUnlocked === true).map((card) => card.cardId));
  const counts = new Map<string, Set<number>>();
  for (const row of cards) {
    const id = Number(row.id);
    if (!eligible.has(id) || typeof row.synergies !== "string") continue;
    for (const authored of row.synergies.split("/")) {
      const synergy = authored.trim().replace(/^Data_Synergy_/, "");
      if (!SYNERGY_ID.test(synergy)) continue;
      const members = counts.get(synergy) ?? new Set<number>();
      members.add(id);
      counts.set(synergy, members);
    }
  }
  return [...new Set(tiers.filter((tier) => Number.isSafeInteger(Number(tier.requiredCount)) &&
    Number(tier.requiredCount) > 0 && (counts.get(String(tier.synergyId))?.size ?? 0) >= Number(tier.requiredCount))
    .map((tier) => String(tier.synergyId)))];
}

export function judgeAchievementClaim(
  id: string, state: AchievementState, catalog: readonly AchievementDef[],
): "AchievementNotFound" | "AlreadyClaimed" | "NotEligible" | null {
  const definition = catalog.find((entry) => entry.id === id);
  if (!definition) return "AchievementNotFound";
  if (state.claimed[id] === true) return "AlreadyClaimed";
  if (achievementCount(state.progress[achievementProgressKey(definition)]) < definition.target ||
      catalog.some((entry) => entry.groupId === definition.groupId && entry.stage < definition.stage &&
        state.claimed[entry.id] !== true)) return "NotEligible";
  return null;
}
