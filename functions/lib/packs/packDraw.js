"use strict";
/**
 * 카드팩 추첨. 클라 OutGame/CardPack/CardPackOpener.Draw · PickWeightedCandidate 와
 * OutGame/CardPack/PackSpec.ResolveDrops 의 축자 이식이다.
 *
 * **순서를 바꾸면 확률이 바뀐다.** 아래 계약을 지키지 않으면 고지 확률(PackOdds)과 실제 추첨이 갈린다:
 * - 풀 순서는 CardPackDrop 행의 id 오름차순이다(호출부가 정렬해서 넘긴다)
 * - 만족하는 등급 중 가장 높은 하나만 쓴다 — 하위 등급과 합산하지 않는다
 * - 유효 가중치는 weight > 0 ? weight : 1 (0·음수는 균등 1로 읽는다)
 * - 뽑을 때마다 잔여 후보 전체를 다시 순회해 가중치 합을 구한다(캐시·증분 감산 금지)
 * - 카탈로그에 없는 카드는 뽑힌 **뒤** 버린다 — 뽑기 1회는 소비되고 장수가 줄 수 있다
 *
 * Firestore 도 HttpsError 도 모른다(scripts/test-open-pack.js 가 직접 부른다).
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.SNACK_PER_DUPLICATE = void 0;
exports.resolveDropPool = resolveDropPool;
exports.drawPack = drawPack;
const rankGrade_1 = require("./rankGrade");
/** 중복 1장이 주는 간식 수. 클라 CardPackOpener.SnackPerDuplicate 와 같아야 한다. */
exports.SNACK_PER_DUPLICATE = 1;
/**
 * 가중치 0·음수를 균등 1로 읽는다. 클라 WeightedCard.EffectiveWeight 와 같다.
 * @param {number} weight 저작 가중치
 * @return {number} 유효 가중치
 */
function effectiveWeight(weight) {
    return weight > 0 ? weight : 1;
}
/**
 * 이 팩에서 뽑을 수 있는 카드와 가중치. 클라 PackSpec.ResolveDrops 재현이다.
 * @param {DropRow[]} rows 이 팩의 CardPackDrop 행 전부(id 오름차순)
 * @param {number} gradeIndex 유저의 랭크 등급 순번
 * @param {ReadonlySet<number>} catalogIds 이 env 에서 노출되는 카드 id
 * @return {WeightedCard[]} 추첨 풀(빈 배열이면 EmptyPool)
 */
function resolveDropPool(rows, gradeIndex, catalogIds) {
    let best = -1;
    for (const row of rows) {
        const grade = (0, rankGrade_1.parsePoolGrade)(row.minGrade);
        if (grade > gradeIndex)
            continue;
        if (grade > best)
            best = grade;
    }
    if (best < 0)
        return [];
    const pool = [];
    for (const row of rows) {
        if ((0, rankGrade_1.parsePoolGrade)(row.minGrade) !== best)
            continue;
        if (!catalogIds.has(row.cardId))
            continue;
        pool.push({ cardId: row.cardId, weight: Math.max(1, row.weight) });
    }
    return pool;
}
/**
 * 잔여 후보 중 하나를 가중치로 고른다. 반환은 pool 인덱스가 아니라 **candidates 인덱스**다
 * — 비복원 제거가 이 자리를 지워야 하기 때문이다.
 * @param {WeightedCard[]} pool 추첨 풀
 * @param {number[]} candidates 잔여 후보의 pool 인덱스 목록
 * @param {RollFn} roll 난수원
 * @return {number} candidates 안에서의 위치
 */
function pickWeightedCandidate(pool, candidates, roll) {
    let sum = 0;
    for (const index of candidates)
        sum += effectiveWeight(pool[index].weight);
    // 유효 가중치가 최소 1이라 구조상 도달하지 않는다. 도달했다면 풀이 비었다는 뜻이다.
    if (sum <= 0)
        return candidates.length - 1;
    let remaining = roll(sum);
    for (let i = 0; i < candidates.length; i++) {
        remaining -= effectiveWeight(pool[candidates[i]].weight);
        if (remaining < 0)
            return i;
    }
    return candidates.length - 1;
}
/**
 * 신규면 소유만, 중복이면 간식. 클라 CardPackOpener.GrantAndReward 재현이다.
 * ownedIds 를 그 자리에서 늘린다 — 한 팩 안에서 같은 카드를 두 번 뽑으면 두 번째는 중복이어야 한다.
 * @param {number} cardId 카드 번호
 * @param {Set<number>} ownedIds 소유 집합(갱신된다)
 * @return {DrawnCard} 뽑힌 카드
 */
function grantAndReward(cardId, ownedIds) {
    if (!ownedIds.has(cardId)) {
        ownedIds.add(cardId);
        return { cardId, isNew: true, snack: 0 };
    }
    return { cardId, isNew: false, snack: exports.SNACK_PER_DUPLICATE };
}
/**
 * 팩 한 개를 뽑는다. ownedIds 는 뽑는 도중 갱신되며, 호출부는 이 집합을 그대로 새 ownership 으로 쓴다.
 * @param {WeightedCard[]} pool 추첨 풀
 * @param {number} drawCount 뽑을 장수(1 이상)
 * @param {boolean} uniqueDraw 한 팩 안 중복 없음(비복원)
 * @param {ReadonlySet<number>} catalogIds 이 env 에서 노출되는 카드 id
 * @param {Set<number>} ownedIds 소유 집합(갱신된다)
 * @param {RollFn} roll 난수원
 * @return {DrawnCard[]} 뽑힌 카드 목록
 */
function drawPack(pool, drawCount, uniqueDraw, catalogIds, ownedIds, roll) {
    if (pool.length === 0)
        return [];
    let count = drawCount;
    if (uniqueDraw && count > pool.length)
        count = pool.length;
    const candidates = [];
    for (let i = 0; i < pool.length; i++)
        candidates.push(i);
    const drawn = [];
    for (let i = 0; i < count; i++) {
        const pick = pickWeightedCandidate(pool, candidates, roll);
        const cardId = pool[candidates[pick]].cardId;
        if (uniqueDraw)
            candidates.splice(pick, 1);
        // 카탈로그에 없는 카드는 여기서 버린다. 뽑기 전에 거르면 클라와 결과가 갈린다.
        if (!catalogIds.has(cardId))
            continue;
        drawn.push(grantAndReward(cardId, ownedIds));
    }
    return drawn;
}
//# sourceMappingURL=packDraw.js.map