"use strict";
/**
 * 카드 레벨·조각 성장의 슬롯 코덱. 폐기된 성장 필드는 읽거나 쓰지 않는다.
 * 클라 CardGrowthSaveData / CardGrowthManager 의 쌍둥이다. 순수(Firestore·HttpsError 모름).
 *
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.BASE_LEVEL = void 0;
exports.readGrowthEntries = readGrowthEntries;
exports.levelOfCard = levelOfCard;
exports.applyEnhanceLevel = applyEnhanceLevel;
exports.applyAcquiredCardGrowth = applyAcquiredCardGrowth;
exports.growthSlot = growthSlot;
exports.shardRequirement = shardRequirement;
exports.feedShard = feedShard;
const saveValues_1 = require("../save/saveValues");
/** 미강화 카드의 레벨. 클라 CardGrowth.BaseLevel 과 같아야 한다. */
exports.BASE_LEVEL = 1;
/**
 * 카드 성장 항목을 읽는다. 키는 카드 id 문자열이다.
 * @param {unknown} cardGrowth 문서의 cardGrowth 슬롯
 * @return {GrowthEntries} 성장 항목
 */
function readGrowthEntries(cardGrowth) {
    const source = cardGrowth?.entries ?? {};
    const entries = {};
    for (const [key, raw] of Object.entries(source)) {
        const id = (0, saveValues_1.intOf)(key);
        if (id <= 0)
            continue;
        const value = raw;
        entries[String(id)] = {
            level: (0, saveValues_1.intOf)(value?.level),
            ...(value?.shardProgress === undefined ? {} : { shardProgress: Math.max(0, (0, saveValues_1.intOf)(value.shardProgress)) }),
        };
    }
    return entries;
}
/**
 * 항목 하나를 고쳐 새 맵을 낸다. 없으면 미강화 기본값으로 신설한다.
 * @param {GrowthEntries} entries 기존 항목
 * @param {number} cardId 카드 id
 * @param {Function} edit 항목을 제자리에서 고치는 함수
 * @return {GrowthEntries} 갱신된 항목
 */
function withEntry(entries, cardId, edit) {
    const next = {};
    for (const [key, entry] of Object.entries(entries))
        next[key] = { ...entry };
    const key = String(cardId);
    const entry = next[key] ?? { level: exports.BASE_LEVEL };
    edit(entry);
    next[key] = entry;
    return next;
}
/**
 * 이 카드의 현재 강화 레벨(기록이 없으면 미강화). 바닥 아래 값은 미강화로 읽는다
 * — 레벨을 0부터 세던 시절의 세이브가 그렇다(클라 CardGrowthManager.LevelOf 와 같은 정규화).
 * @param {GrowthEntries} entries 성장 항목
 * @param {number} cardId 카드 id
 * @return {number} 강화 레벨
 */
function levelOfCard(entries, cardId) {
    const level = entries[String(cardId)]?.level ?? exports.BASE_LEVEL;
    return level < exports.BASE_LEVEL ? exports.BASE_LEVEL : level;
}
/**
 * 강화 성공을 반영하고 해당 레벨의 조각 진행도를 초기화한다.
 * @param {GrowthEntries} entries 기존 항목
 * @param {number} cardId 카드 id
 * @param {number} level 도달 레벨
 * @return {GrowthEntries} 갱신된 항목
 */
function applyEnhanceLevel(entries, cardId, level) {
    return withEntry(entries, cardId, (entry) => {
        entry.level = level < exports.BASE_LEVEL ? exports.BASE_LEVEL : level;
        if (entry.shardProgress !== undefined)
            entry.shardProgress = 0;
    });
}
/**
 * 최초 획득한 서사·신화 카드는 1성(저장 레벨 2)으로 시작한다.
 * 호출자가 신규 소유 카드만 넘긴다. 기존의 더 높은 성장값은 보존한다.
 * @param {GrowthEntries} entries 기존 성장 항목
 * @param {number[]} cardIds 최초 획득한 카드
 * @param {ReadonlyMap<number, string>} grades 카드별 등급
 * @return {GrowthEntries} 최초 성장값을 반영한 항목
 */
function applyAcquiredCardGrowth(entries, cardIds, grades) {
    for (const cardId of cardIds) {
        const grade = grades.get(cardId);
        if ((grade === "Arcane" || grade === "Mythic") && levelOfCard(entries, cardId) < exports.BASE_LEVEL + 1) {
            entries = applyEnhanceLevel(entries, cardId, exports.BASE_LEVEL + 1);
        }
    }
    return entries;
}
/**
 * 세이브의 cardGrowth 슬롯 **전체 값**. 기본값뿐인 항목은 버린다
 * — 클라 CardGrowthManager.FlushToData 가 같은 가지치기를 하므로, 안 맞추면 다음 저장에서 문서가 흔들린다.
 * @param {GrowthEntries} entries 성장 항목
 * @return {object} cardGrowth 슬롯
 */
function growthSlot(entries) {
    const pruned = {};
    for (const [key, entry] of Object.entries(entries)) {
        if (entry.level <= exports.BASE_LEVEL && (entry.shardProgress ?? 0) <= 0)
            continue;
        pruned[key] = { level: entry.level,
            ...(entry.shardProgress === undefined ? {} : { shardProgress: entry.shardProgress }) };
    }
    return { entries: pruned };
}
/** Keep the evolution threshold compatible with the client integer range.
 * @param {number} cost Authored total cost
 * @return {number} Positive shard threshold
 */
function shardRequirement(cost) {
    return Math.max(1, Math.min(2147483647, cost));
}
/** Feed shards up to the next evolution; the tutorial grant fills the remainder.
 * @param {GrowthEntries} entries Current growth
 * @param {number} cardId Card identity
 * @param {number} required Evolution threshold
 * @param {boolean} freeShot Free tutorial evolution
 * @param {number} amount Requested shard count, validated by the caller
 * @return {object} Updated growth and evolution result
 */
function feedShard(entries, cardId, required, freeShot = false, amount = 1) {
    const currentLevel = levelOfCard(entries, cardId);
    const target = shardRequirement(required);
    const currentProgress = Math.min(target - 1, Math.max(0, entries[String(cardId)]?.shardProgress ?? 0));
    const appliedShards = freeShot ? target - currentProgress : Math.min(amount, target - currentProgress);
    const progress = currentProgress + appliedShards;
    const evolved = freeShot || progress >= target;
    const level = currentLevel + (evolved ? 1 : 0);
    const shardProgress = evolved ? 0 : progress;
    return { entries: withEntry(entries, cardId, (entry) => {
            entry.level = level;
            entry.shardProgress = shardProgress;
        }), level, shardProgress, evolved, appliedShards };
}
//# sourceMappingURL=cardGrowth.js.map