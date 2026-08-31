"use strict";
/**
 * 랭크 등급 판정. 클라 OutGame/Rank/RankConfig.cs 의 쌍둥이다.
 *
 * 서버가 등급을 알아야 팩 잠금(CardPack.minRankGrade)과 풀 해석(CardPackDrop.minGrade)이 선다.
 * 근거는 스펙 표 RankGrade 이고, 표를 못 읽었을 때만 FALLBACK_ENTRY_POINTS 로 떨어진다.
 *
 * Firestore 를 모른다 — 표 읽기는 packSpecReader 가 하고 여기는 행을 받아 판정만 한다.
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.FALLBACK_ENTRY_POINTS = exports.GRADE_KEYS = void 0;
exports.entryPointsFromRows = entryPointsFromRows;
exports.gradeOf = gradeOf;
exports.isRanked = isRanked;
exports.parsePoolGrade = parsePoolGrade;
exports.parseRequiredGrade = parseRequiredGrade;
/** ERankGrade 의 이름과 순서. 배열 인덱스가 곧 enum 값이라 비교는 이 순번으로 한다. */
exports.GRADE_KEYS = ["Bronze", "Silver", "Gold", "Platinum", "Diamond"];
/**
 * RankGrade 표를 못 읽었을 때의 임계치.
 * 진실원은 Assets/SO/Rank/RankConfig.asset 의 grades[].entryPoints — 저기가 바뀌면 여기도 바꾼다.
 * scripts/test-open-pack.js 가 두 값을 대조한다.
 */
exports.FALLBACK_ENTRY_POINTS = [100, 260, 420, 580, 740];
/**
 * 표 행에서 등급별 진입 임계치를 뽑는다. 5등급이 다 있어야 하고, 하나라도 없으면 null.
 * 순서는 행 순서가 아니라 GRADE_KEYS 순번을 따른다 — 비교 축이 enum 값이기 때문이다.
 * @param {RankGradeRow[]} rows RankGrade 전 행
 * @return {number[] | null} 등급 순번별 임계치, 못 채우면 null
 */
function entryPointsFromRows(rows) {
    const points = [];
    for (const key of exports.GRADE_KEYS) {
        const row = rows.find((r) => r.gradeKey === key);
        if (row === undefined || !Number.isFinite(row.entryPoints))
            return null;
        points.push(row.entryPoints);
    }
    return points;
}
/**
 * 점수가 속한 등급 순번. 클라 RankConfig.ResolveTierIndex / DivisionsPerGrade 와 같은 답을 낸다
 * — 첫 등급에 미도달해도 0(Bronze)으로 폴백하는 것까지 같다.
 * @param {number[]} entryPoints 등급 순번별 임계치
 * @param {number} points rank.points
 * @return {number} 등급 순번
 */
function gradeOf(entryPoints, points) {
    for (let i = entryPoints.length - 1; i >= 0; i--) {
        if (entryPoints[i] <= points)
            return i;
    }
    return 0;
}
/**
 * 랭크에 도달했는가. 클라 RankManager.IsRanked = Points >= Config.FirstTierPoints 다.
 * gradeOf 가 미도달도 Bronze 로 답하므로 잠금 판정에는 이 값이 따로 필요하다.
 * @param {number[]} entryPoints 등급 순번별 임계치
 * @param {number} points rank.points
 * @return {boolean} 첫 등급 진입 여부
 */
function isRanked(entryPoints, points) {
    return entryPoints.length > 0 && points >= entryPoints[0];
}
/**
 * 이름이 아닌 정수로 저작된 등급 값. C# Enum.TryParse 가 정수 문자열도 받아들이므로 시트에 "0"·"1" 이
 * 적혀 있어도 클라는 등급으로 읽는다 — 범위 밖 정수까지 그대로 통과시키는 것도 같다.
 * @param {string} value 열 값
 * @return {number | null} 정수 등급, 정수가 아니면 null
 */
function parseNumericGrade(value) {
    if (!/^[+-]?\d+$/.test(value))
        return null;
    return Number.parseInt(value, 10);
}
/**
 * CardPackDrop.minGrade 를 등급 순번으로. 클라 PackSpec.ParseGrade 재현이다
 * — **대소문자를 가리고**, 못 읽으면 최하위(Bronze)로 떨어진다.
 *
 * 범위 밖 정수("99")를 0 으로 접지 않는 것이 중요하다. 클라는 그대로 99 를 들고
 * `minGrade > 현재등급` 에 걸려 그 행을 풀에서 제외한다 — 여기서 0 으로 접으면 뽑히면 안 될 카드가 섞인다.
 * @param {string} value minGrade 열 값
 * @return {number} 등급 순번
 */
function parsePoolGrade(value) {
    const index = exports.GRADE_KEYS.indexOf(value);
    if (index >= 0)
        return index;
    const numeric = parseNumericGrade(value);
    return numeric === null ? 0 : numeric;
}
/**
 * CardPack.minRankGrade 를 등급 순번으로. 클라 CardPackData.TryGetMinRankGrade 재현이다
 * — **대소문자를 안 가리고**, 비어 있거나 못 읽거나 정의 범위 밖이면 **잠금 없음**(null)이다.
 * @param {string} value minRankGrade 열 값
 * @return {number | null} 필요 등급 순번, 잠금이 없으면 null
 */
function parseRequiredGrade(value) {
    const trimmed = value.trim();
    if (trimmed.length === 0)
        return null;
    const lowered = trimmed.toLowerCase();
    const index = exports.GRADE_KEYS.findIndex((k) => k.toLowerCase() === lowered);
    if (index >= 0)
        return index;
    // Enum.IsDefined 에 걸려 떨어지는 갈래 — 클라는 여기서 경고만 내고 잠금을 풀어 준다.
    const numeric = parseNumericGrade(trimmed);
    if (numeric === null || numeric < 0 || numeric >= exports.GRADE_KEYS.length)
        return null;
    return numeric;
}
//# sourceMappingURL=rankGrade.js.map