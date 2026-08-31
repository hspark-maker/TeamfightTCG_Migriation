"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.DIVISIONS_PER_GRADE = void 0;
exports.parseRankGradeRows = parseRankGradeRows;
exports.rankTierCount = rankTierCount;
exports.requiredPointsForTier = requiredPointsForTier;
exports.computeDrawRankPayout = computeDrawRankPayout;
exports.computeRankPayout = computeRankPayout;
exports.computeCurrencyPayout = computeCurrencyPayout;
function finiteInteger(value, field) {
    if (typeof value !== "number" || !Number.isSafeInteger(value))
        throw new Error(`invalid ${field}`);
    return value;
}
function parseRankGradeRows(rows) {
    return rows.map((row) => ({
        id: finiteInteger(row.id, "RankGrade.id"),
        entryPoints: finiteInteger(row.entryPoints, "RankGrade.entryPoints"),
        pointsPerDivision: finiteInteger(row.pointsPerDivision, "RankGrade.pointsPerDivision"),
        winPoints: finiteInteger(row.winPoints, "RankGrade.winPoints"),
        losePoints: finiteInteger(row.losePoints, "RankGrade.losePoints"),
    })).sort((a, b) => a.id - b.id);
}
/** 등급 하나를 나누는 단계 수. 클라 RankConfig.DivisionsPerGrade 와 같은 값이어야 한다. */
exports.DIVISIONS_PER_GRADE = 4;
/**
 * 전체 티어 수(등급 수 x 단계 수). 클라 RankConfig.TierCount 와 같은 파생이다.
 * @param {RankGradeRow[]} grades 등급 표
 * @return {number} 티어 수
 */
function rankTierCount(grades) {
    return grades.length * exports.DIVISIONS_PER_GRADE;
}
/**
 * 티어 하나의 요구 점수. 클라 RankConfig.TryGetTier 의 RequiredPoints 와 같은 식이다
 * (entryPoints + 단계 * pointsPerDivision). resolveTierIndex 가 이 식의 역함수다.
 * @param {number} tierIndex 티어 인덱스
 * @param {RankGradeRow[]} grades 등급 표
 * @return {number | null} 요구 점수, 범위 밖이면 null
 */
function requiredPointsForTier(tierIndex, grades) {
    if (!Number.isInteger(tierIndex) || tierIndex < 0 || tierIndex >= rankTierCount(grades))
        return null;
    const grade = grades[Math.floor(tierIndex / exports.DIVISIONS_PER_GRADE)];
    return grade.entryPoints + (tierIndex % exports.DIVISIONS_PER_GRADE) * grade.pointsPerDivision;
}
function resolveTierIndex(points, grades) {
    for (let gradeIndex = grades.length - 1; gradeIndex >= 0; gradeIndex--) {
        const grade = grades[gradeIndex];
        for (let division = 3; division >= 0; division--) {
            if (grade.entryPoints + division * grade.pointsPerDivision <= points)
                return gradeIndex * 4 + division;
        }
    }
    return 0;
}
function divisionFloor(points, grades) {
    const tier = resolveTierIndex(points, grades);
    const grade = grades[Math.floor(tier / 4)];
    return grade == null ? 0 : grade.entryPoints + (tier % 4) * grade.pointsPerDivision;
}
function gradeCeiling(points, grades) {
    const nextGrade = Math.floor(resolveTierIndex(points, grades) / 4) + 1;
    return nextGrade < grades.length ? grades[nextGrade].entryPoints : null;
}
/**
 * 무승부 랭크 정산. 점수를 움직이지 않는다 — 승자가 없으므로 올리거나 내릴 근거가 없다.
 * computeRankPayout 은 승패 인자를 요구해 어느 쪽으로든 점수를 바꾸므로 여기서 따로 만든다.
 * @param {number} before 정산 전 랭크 점수.
 * @param {RankGradeRow[]} grades 등급 표.
 * @return {RankPayout} 점수·티어가 그대로이고 delta 가 0인 정산 결과.
 */
function computeDrawRankPayout(before, grades) {
    if (!Number.isSafeInteger(before) || before < 0 || grades.length === 0) {
        throw new Error("invalid rank payout input");
    }
    const tierIndex = resolveTierIndex(before, grades);
    return { before, after: before, delta: 0,
        beforeTierIndex: tierIndex, afterTierIndex: tierIndex, promoBattle: false };
}
function computeRankPayout(before, won, grades) {
    if (!Number.isSafeInteger(before) || before < 0 || grades.length === 0)
        throw new Error("invalid rank payout input");
    const beforeTierIndex = resolveTierIndex(before, grades);
    const ceiling = gradeCeiling(before, grades);
    const promoBattle = before >= grades[0].entryPoints && ceiling != null && before === ceiling - 1;
    let after;
    if (promoBattle) {
        const floor = divisionFloor(before, grades);
        after = won ? ceiling : floor + Math.trunc((ceiling - floor) / 2);
    }
    else {
        const floor = before >= grades[0].entryPoints ? divisionFloor(before, grades) : 0;
        const max = ceiling == null ? Number.MAX_SAFE_INTEGER : ceiling - 1;
        // C# RankConfig의 winPoints/losePoints는 등급별이 아니라 전역 한 쌍이다.
        const delta = won ? grades[0].winPoints : -grades[0].losePoints;
        after = Math.min(Math.max(before + delta, floor), max);
    }
    return { before, after, delta: after - before, beforeTierIndex,
        afterTierIndex: resolveTierIndex(after, grades), promoBattle };
}
function battleReward(rows, ownerId) {
    const matches = rows.filter((row) => row.ownerType === "Battle" && row.ownerId === ownerId &&
        row.rewardType === "Currency" && row.amount >= 0);
    if (matches.length !== 1 || !matches[0].rewardId)
        throw new Error(`invalid Battle reward row: ${ownerId}`);
    return matches[0];
}
function computeCurrencyPayout(won, remaining, rows) {
    if (!Number.isInteger(remaining) || remaining < 0)
        throw new Error("invalid remaining cards");
    const row = battleReward(rows, won ? "win.perCard" : "lose.flat");
    let amount = row.amount;
    if (won) {
        const floor = battleReward(rows, "win.floor");
        if (floor.rewardId !== row.rewardId)
            throw new Error("Battle win reward currency mismatch");
        amount = Math.max(remaining * row.amount, floor.amount);
    }
    return { currency: row.rewardId, amount };
}
//# sourceMappingURL=payout.js.map