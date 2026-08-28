export type RewardRow = {
  id: number;
  ownerType: string;
  ownerId: string;
  order: number;
  rewardType: string;
  rewardId: string;
  amount: number;
};

/** 지급 한 줄. 배열 순서가 곧 표시 순서다(클라 RewardLine 과 같은 뜻). */
export type RewardGain = {currency: CurrencyKey; amount: number};

/** 버려진 보상 줄. 저작 실수를 조용히 삼키지 않으려고 사유를 들고 나온다. */
export type DroppedReward = {
  id: number;
  reason: string;
  rewardType: string;
  rewardId: string;
  amount: number;
};

/** 한 소유자의 보상 해석 결과. */
export type RewardResolution = {gains: RewardGain[]; dropped: DroppedReward[]};

import {CURRENCY_KEYS, CurrencyKey} from "./currency/currencyKeys";

export type RankGradeRow = {
  id: number;
  entryPoints: number;
  pointsPerDivision: number;
  winPoints: number;
  losePoints: number;
};

export type RankPayout = {
  before: number;
  after: number;
  delta: number;
  beforeTierIndex: number;
  afterTierIndex: number;
  promoBattle: boolean;
};

export type CurrencyPayout = {currency: string; amount: number};

function finiteInteger(value: unknown, field: string): number {
  if (typeof value !== "number" || !Number.isSafeInteger(value)) throw new Error(`invalid ${field}`);
  return value;
}

/**
 * 정수로 읽고 못 읽으면 0. finiteInteger 와 달리 던지지 않는다 —
 * id·order 가 비었다고 Battle 행 파싱까지 죽으면 submitMatchResult 가 통째로 멈춘다.
 * @param {unknown} value 표 값
 * @return {number} 정수
 */
function looseInteger(value: unknown): number {
  const numeric = Number(value ?? 0);
  return Number.isFinite(numeric) ? Math.trunc(numeric) : 0;
}

/**
 * 재화 이름을 **엄격하게** 읽는다. currency/currencyKeys 의 parseCurrency 와 달리 Gold 로 떨어지지 않는다
 * — 보상은 못 읽으면 그 줄을 버리는 것이 규약이다(클라 RewardSpec.TryConvert 와 같은 축).
 * @param {string} value rewardId 열 값
 * @return {CurrencyKey | null} 재화 키, 못 읽으면 null
 */
function strictCurrency(value: string): CurrencyKey | null {
  const lowered = value.trim().toLowerCase();
  return CURRENCY_KEYS.find((key) => key.toLowerCase() === lowered) ?? null;
}

export function parseRewardRows(rows: Record<string, unknown>[]): RewardRow[] {
  return rows.map((row) => ({
    id: looseInteger(row.id),
    ownerType: String(row.ownerType ?? ""),
    ownerId: String(row.ownerId ?? ""),
    order: looseInteger(row.order),
    rewardType: String(row.rewardType ?? ""),
    rewardId: String(row.rewardId ?? ""),
    amount: finiteInteger(row.amount, "Reward.amount"),
  }));
}

/**
 * 한 소유자(ownerType + ownerId)에 걸린 보상을 지급 목록으로 해석한다.
 * 클라 RewardSpec.EnsureLoaded 와 같은 규칙이다 — 두 쪽이 갈리면 화면에 보인 것과 받은 것이 달라진다.
 *
 * 규칙: order 오름차순(동률은 id) · rewardType 은 "Currency" 만(대소문자 구분) ·
 * 같은 order 중복 줄은 버림 · 모르는 재화와 0 이하 지급량은 버림.
 * @param {RewardRow[]} rows Reward 표 전량
 * @param {string} ownerType Album | Tournament | Rank | Battle
 * @param {string} ownerId 소유자 키(정점 nodeId · 랭크 티어 인덱스 문자열 등)
 * @return {RewardResolution} 지급 목록과 버린 줄
 */
export function resolveRewards(rows: RewardRow[], ownerType: string, ownerId: string): RewardResolution {
  const gains: RewardGain[] = [];
  const dropped: DroppedReward[] = [];
  const seenOrders = new Set<number>();

  // 축이 다르면 절대 섞이지 않는다 — Rank/"1" 과 Tournament/"1" 은 남남이다.
  const owned = rows
    .filter((row) => row.ownerType === ownerType && row.ownerId === ownerId)
    .sort((a, b) => (a.order - b.order) || (a.id - b.id));

  for (const row of owned) {
    const drop = (reason: string) => dropped.push({
      id: row.id, reason, rewardType: row.rewardType, rewardId: row.rewardId, amount: row.amount,
    });

    // 카드 보상이 저작되면 여기서 드러나야 한다. 조용히 재화로 바꾸지 않는다.
    if (row.rewardType !== "Currency") {
      drop("UnknownRewardType");
      continue;
    }
    if (seenOrders.has(row.order)) {
      drop("DuplicateOrder");
      continue;
    }
    seenOrders.add(row.order);

    const currency = strictCurrency(row.rewardId);
    if (currency === null) {
      drop("UnknownCurrency");
      continue;
    }
    if (row.amount <= 0) {
      drop("NonPositiveAmount");
      continue;
    }

    gains.push({currency, amount: row.amount});
  }

  return {gains, dropped};
}

export function parseRankGradeRows(rows: Record<string, unknown>[]): RankGradeRow[] {
  return rows.map((row) => ({
    id: finiteInteger(row.id, "RankGrade.id"),
    entryPoints: finiteInteger(row.entryPoints, "RankGrade.entryPoints"),
    pointsPerDivision: finiteInteger(row.pointsPerDivision, "RankGrade.pointsPerDivision"),
    winPoints: finiteInteger(row.winPoints, "RankGrade.winPoints"),
    losePoints: finiteInteger(row.losePoints, "RankGrade.losePoints"),
  })).sort((a, b) => a.id - b.id);
}

/** 등급 하나를 나누는 단계 수. 클라 RankConfig.DivisionsPerGrade 와 같은 값이어야 한다. */
export const DIVISIONS_PER_GRADE = 4;

/**
 * 전체 티어 수(등급 수 x 단계 수). 클라 RankConfig.TierCount 와 같은 파생이다.
 * @param {RankGradeRow[]} grades 등급 표
 * @return {number} 티어 수
 */
export function rankTierCount(grades: RankGradeRow[]): number {
  return grades.length * DIVISIONS_PER_GRADE;
}

/**
 * 티어 하나의 요구 점수. 클라 RankConfig.TryGetTier 의 RequiredPoints 와 같은 식이다
 * (entryPoints + 단계 * pointsPerDivision). resolveTierIndex 가 이 식의 역함수다.
 * @param {number} tierIndex 티어 인덱스
 * @param {RankGradeRow[]} grades 등급 표
 * @return {number | null} 요구 점수, 범위 밖이면 null
 */
export function requiredPointsForTier(tierIndex: number, grades: RankGradeRow[]): number | null {
  if (!Number.isInteger(tierIndex) || tierIndex < 0 || tierIndex >= rankTierCount(grades)) return null;

  const grade = grades[Math.floor(tierIndex / DIVISIONS_PER_GRADE)];
  return grade.entryPoints + (tierIndex % DIVISIONS_PER_GRADE) * grade.pointsPerDivision;
}

function resolveTierIndex(points: number, grades: RankGradeRow[]): number {
  for (let gradeIndex = grades.length - 1; gradeIndex >= 0; gradeIndex--) {
    const grade = grades[gradeIndex];
    for (let division = 3; division >= 0; division--) {
      if (grade.entryPoints + division * grade.pointsPerDivision <= points) return gradeIndex * 4 + division;
    }
  }
  return 0;
}

function divisionFloor(points: number, grades: RankGradeRow[]): number {
  const tier = resolveTierIndex(points, grades);
  const grade = grades[Math.floor(tier / 4)];
  return grade == null ? 0 : grade.entryPoints + (tier % 4) * grade.pointsPerDivision;
}

function gradeCeiling(points: number, grades: RankGradeRow[]): number | null {
  const nextGrade = Math.floor(resolveTierIndex(points, grades) / 4) + 1;
  return nextGrade < grades.length ? grades[nextGrade].entryPoints : null;
}

export function computeRankPayout(before: number, won: boolean, grades: RankGradeRow[]): RankPayout {
  if (!Number.isSafeInteger(before) || before < 0 || grades.length === 0) throw new Error("invalid rank payout input");
  const beforeTierIndex = resolveTierIndex(before, grades);
  const ceiling = gradeCeiling(before, grades);
  const promoBattle = before >= grades[0].entryPoints && ceiling != null && before === ceiling - 1;
  let after: number;
  if (promoBattle) {
    const floor = divisionFloor(before, grades);
    after = won ? ceiling as number : floor + Math.trunc(((ceiling as number) - floor) / 2);
  } else {
    const floor = before >= grades[0].entryPoints ? divisionFloor(before, grades) : 0;
    const max = ceiling == null ? Number.MAX_SAFE_INTEGER : ceiling - 1;
    // C# RankConfig의 winPoints/losePoints는 등급별이 아니라 전역 한 쌍이다.
    const delta = won ? grades[0].winPoints : -grades[0].losePoints;
    after = Math.min(Math.max(before + delta, floor), max);
  }
  return {before, after, delta: after - before, beforeTierIndex,
    afterTierIndex: resolveTierIndex(after, grades), promoBattle};
}

function battleReward(rows: RewardRow[], ownerId: string): RewardRow {
  const matches = rows.filter((row) => row.ownerType === "Battle" && row.ownerId === ownerId &&
    row.rewardType === "Currency" && row.amount >= 0);
  if (matches.length !== 1 || !matches[0].rewardId) throw new Error(`invalid Battle reward row: ${ownerId}`);
  return matches[0];
}

export function computeCurrencyPayout(won: boolean, remaining: number, rows: RewardRow[]): CurrencyPayout {
  if (!Number.isInteger(remaining) || remaining < 0) throw new Error("invalid remaining cards");
  const row = battleReward(rows, won ? "win.perCard" : "lose.flat");
  let amount = row.amount;
  if (won) {
    const floor = battleReward(rows, "win.floor");
    if (floor.rewardId !== row.rewardId) throw new Error("Battle win reward currency mismatch");
    amount = Math.max(remaining * row.amount, floor.amount);
  }
  return {currency: row.rewardId, amount};
}
