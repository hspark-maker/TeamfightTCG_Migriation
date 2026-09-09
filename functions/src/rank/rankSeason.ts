import {
  currentPassSeason,
  parsePassSeasons,
  PassSeasonDef,
} from "../pass/passSpec";

export type RankSeasonDef = Pick<
  PassSeasonDef,
  "seasonId" | "displayName" | "startAtMs" | "endAtMs"
>;

/**
 * Rank seasons intentionally share the authored PassSeason boundary.
 * This keeps one live-service calendar and avoids another generated sheet schema.
 * @param {Record<string, unknown>[]} rows PassSeason spec rows.
 * @param {number} nowMs Current epoch milliseconds.
 * @return {RankSeasonDef | null} Active season, or null when none is live.
 */
export function currentRankSeason(
  rows: Record<string, unknown>[], nowMs: number,
): RankSeasonDef | null {
  return currentPassSeason(parsePassSeasons(rows), nowMs);
}
