export interface PassSeasonDef {
  id: number;
  seasonId: string;
  displayName: string;
  startAtMs: number;
  endAtMs: number;
  maxLevel: number;
}

export interface PassLevelDef {
  id: number;
  seasonId: string;
  level: number;
  requiredExp: number;
}

/**
 * 정수 열 하나를 읽는다. **지수·소수 표기는 거절한다** — 시트가 큰 정수를 `1.79E+12` 로
 * 내보내면 `Number()` 는 반올림된 값(1790000000000)을 순순히 돌려주고 `isSafeInteger` 도 통과해,
 * 시즌 경계가 몇 주씩 밀린 채로 검증을 다 지나간다. 블롭 파서(coerce)는 `^-?\d+$` 가 아닌 값을
 * 문자열로 남기므로, 그 문자열 형태를 여기서 다시 본다.
 * @param {unknown} value 표 셀 값
 * @param {string} field 오류 메시지에 실을 열 이름
 * @return {number} 안전 정수
 */
function integer(value: unknown, field: string): number {
  if (typeof value === "number") {
    if (!Number.isSafeInteger(value)) throw new Error(`PASS_SPEC_INVALID ${field}`);
    return value;
  }
  if (typeof value === "string" && /^-?\d+$/.test(value.trim())) {
    const parsed = Number(value.trim());
    if (Number.isSafeInteger(parsed)) return parsed;
  }
  throw new Error(`PASS_SPEC_INVALID ${field}`);
}

/**
 * Parses and validates authored pass seasons.
 * @param {Record<string, unknown>[]} rows PassSeason spec rows.
 * @return {PassSeasonDef[]} Valid seasons ordered by start time.
 */
export function parsePassSeasons(rows: Record<string, unknown>[]): PassSeasonDef[] {
  const seasons = rows.map((row) => {
    const season: PassSeasonDef = {
      id: integer(row.id, "PassSeason.id"),
      seasonId: String(row.seasonId ?? "").trim(),
      displayName: String(row.displayName ?? "").trim(),
      startAtMs: integer(row.startAtMs, "PassSeason.startAtMs"),
      endAtMs: integer(row.endAtMs, "PassSeason.endAtMs"),
      maxLevel: integer(row.maxLevel, "PassSeason.maxLevel"),
    };
    if (season.seasonId.length === 0 || season.seasonId.length > 32 ||
        season.startAtMs < 0 || season.endAtMs <= season.startAtMs ||
        season.maxLevel <= 0 || season.maxLevel > 200) {
      throw new Error(`PASS_SPEC_INVALID season=${season.seasonId || season.id}`);
    }
    return season;
  }).sort((a, b) => a.startAtMs - b.startAtMs || a.id - b.id);

  const ids = new Set<string>();
  for (let i = 0; i < seasons.length; i++) {
    const season = seasons[i];
    if (ids.has(season.seasonId)) {
      throw new Error(`PASS_SPEC_INVALID duplicate season=${season.seasonId}`);
    }
    ids.add(season.seasonId);
    if (i > 0 && seasons[i - 1].endAtMs > season.startAtMs) {
      throw new Error(`PASS_SPEC_INVALID overlapping season=${season.seasonId}`);
    }
  }
  return seasons;
}

/**
 * Finds the season containing the supplied instant. Season gaps are valid.
 * @param {PassSeasonDef[]} seasons Authored seasons.
 * @param {number} nowMs Current Unix time in milliseconds.
 * @return {PassSeasonDef | null} Active season, if any.
 */
export function currentPassSeason(
  seasons: PassSeasonDef[], nowMs: number,
): PassSeasonDef | null {
  return seasons.find((season) => season.startAtMs <= nowMs && nowMs < season.endAtMs) ?? null;
}

/**
 * Parses one season's cumulative experience curve and enforces a contiguous level range.
 * @param {Record<string, unknown>[]} rows PassLevel spec rows.
 * @param {PassSeasonDef} season Active season.
 * @return {PassLevelDef[]} Levels ordered from 1 through maxLevel.
 */
export function parsePassLevels(
  rows: Record<string, unknown>[], season: PassSeasonDef,
): PassLevelDef[] {
  const levels = rows
    .filter((row) => String(row.seasonId ?? "").trim() === season.seasonId)
    .map((row) => ({
      id: integer(row.id, "PassLevel.id"),
      seasonId: season.seasonId,
      level: integer(row.level, "PassLevel.level"),
      requiredExp: integer(row.requiredExp, "PassLevel.requiredExp"),
    }))
    .sort((a, b) => a.level - b.level || a.id - b.id);

  if (levels.length !== season.maxLevel) {
    throw new Error(`PASS_SPEC_INVALID season=${season.seasonId} levels=${levels.length}/${season.maxLevel}`);
  }
  let previousExp = -1;
  for (let i = 0; i < levels.length; i++) {
    const level = levels[i];
    if (level.level !== i + 1 || level.requiredExp <= previousExp) {
      throw new Error(`PASS_SPEC_INVALID season=${season.seasonId} level=${level.level}`);
    }
    previousExp = level.requiredExp;
  }
  return levels;
}

/**
 * Resolves the highest level reached by cumulative experience.
 * @param {number} exp Current season experience.
 * @param {PassLevelDef[]} levels Valid cumulative curve.
 * @return {number} Reached level, or zero before the first threshold.
 */
export function passLevelOf(exp: number, levels: PassLevelDef[]): number {
  let reached = 0;
  for (const level of levels) {
    if (exp < level.requiredExp) break;
    reached = level.level;
  }
  return reached;
}

/**
 * Builds the Reward table owner id for the free pass track.
 * @param {string} seasonId Season key.
 * @param {number} level Pass level.
 * @return {string} Reward owner id.
 */
export function passRewardOwnerId(seasonId: string, level: number): string {
  return `${seasonId}:${level}`;
}
