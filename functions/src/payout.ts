export type RewardRow = {
  ownerType: string;
  ownerId: string;
  rewardType: string;
  rewardId: string;
  amount: number;
};

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

export function parseRewardRows(rows: Record<string, unknown>[]): RewardRow[] {
  return rows.map((row) => ({
    ownerType: String(row.ownerType ?? ""),
    ownerId: String(row.ownerId ?? ""),
    rewardType: String(row.rewardType ?? ""),
    rewardId: String(row.rewardId ?? ""),
    amount: finiteInteger(row.amount, "Reward.amount"),
  }));
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
