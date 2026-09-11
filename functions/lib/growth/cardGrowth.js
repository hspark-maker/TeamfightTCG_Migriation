"use strict";
/**
 * 카드 성장 진행도(강화 레벨 · 먹이 · 한계돌파)의 슬롯 코덱과 먹이 소비 판정.
 * 클라 CardGrowthSaveData / CardGrowthManager 의 쌍둥이다. 순수(Firestore·HttpsError 모름).
 *
 * 먹이는 그 카드에만 쓰는 재료라 전역 잔액이 아니라 카드 id 로 갈린 항목에 얹혀 있다
 * — 그래서 currency/wallet 과 합치지 않는다(키 집합·상한·슬롯 모양이 전부 다르다).
 *
 * packs/ 를 import 하지 않는다. 간식 적립 입력은 DrawnCard 타입이 아니라 카드 id·수량으로 받는다.
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.SNACK_MAX = exports.BASE_LEVEL = void 0;
exports.readGrowthEntries = readGrowthEntries;
exports.levelOfCard = levelOfCard;
exports.applyEnhanceLevel = applyEnhanceLevel;
exports.applyAcquiredCardGrowth = applyAcquiredCardGrowth;
exports.addSnack = addSnack;
exports.canAffordSnack = canAffordSnack;
exports.spendSnack = spendSnack;
exports.applyLimitBreak = applyLimitBreak;
exports.addSnackAndGrow = addSnackAndGrow;
exports.growthSlot = growthSlot;
exports.shardRequirement = shardRequirement;
exports.feedShard = feedShard;
const saveValues_1 = require("../save/saveValues");
const limitBreakTable_1 = require("./limitBreakTable");
/** 미강화 카드의 레벨. 클라 CardGrowth.BaseLevel 과 같아야 한다. */
exports.BASE_LEVEL = 1;
/** 먹이 보유 상한. 클라 CardGrowthEntry.Snack 이 int 이고 AddSnack 이 여기서 자른다. */
exports.SNACK_MAX = 2147483647;
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
            snack: (0, saveValues_1.intOf)(value?.snack),
            limitBreak: (0, saveValues_1.intOf)(value?.limitBreak),
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
    const entry = next[key] ?? { level: exports.BASE_LEVEL, snack: 0, limitBreak: 0 };
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
 * 강화 성공을 반영한다. 먹이·한계돌파는 그대로 둔다 — 슬롯 **전체 값**을 되쓰므로
 * 여기서 흘리면 그 카드의 나머지 진행도가 지워진다.
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
 * 보유 먹이. 음수 세이브는 0으로 읽는다(클라 SnackOf 와 같다).
 * @param {GrowthEntries} entries 성장 항목
 * @param {number} cardId 카드 id
 * @return {number} 보유량
 */
function snackOf(entries, cardId) {
    const snack = entries[String(cardId)]?.snack ?? 0;
    return snack > 0 ? snack : 0;
}
/**
 * 먹이를 적립한다. 신규 카드에는 안 붙는다(수량 0 이하는 무변경).
 * @param {GrowthEntries} entries 기존 항목
 * @param {number} cardId 카드 id
 * @param {number} amount 적립량
 * @return {GrowthEntries} 갱신된 항목
 */
function addSnack(entries, cardId, amount) {
    if (cardId <= 0 || amount <= 0)
        return entries;
    const current = snackOf(entries, cardId);
    return withEntry(entries, cardId, (entry) => {
        entry.snack = Math.min(current + amount, exports.SNACK_MAX);
    });
}
/**
 * 먹이가 충분한가.
 * @param {GrowthEntries} entries 성장 항목
 * @param {number} cardId 카드 id
 * @param {number} cost 소모량
 * @return {boolean} 보유량이 소모량 이상이면 true
 */
function canAffordSnack(entries, cardId, cost) {
    return snackOf(entries, cardId) >= cost;
}
/**
 * 먹이를 차감한다. 여력 검사는 호출부가 이미 끝냈다 — 여기서는 하한 0 으로만 자른다.
 * @param {GrowthEntries} entries 기존 항목
 * @param {number} cardId 카드 id
 * @param {number} cost 소모량
 * @return {GrowthEntries} 갱신된 항목
 */
function spendSnack(entries, cardId, cost) {
    const left = snackOf(entries, cardId) - cost;
    return withEntry(entries, cardId, (entry) => {
        entry.snack = left < 0 ? 0 : left;
    });
}
/**
 * 한계돌파 1단계. **먹이 차감과 단계 증가는 반드시 함께 간다**
 * — 클라 TryLimitBreak 이 한 몸으로 저장하므로 갈라 두면 반쪽 문서가 생긴다.
 * @param {GrowthEntries} entries 기존 항목
 * @param {number} cardId 카드 id
 * @param {number} stage 도달 단계
 * @param {number} snackCost 소모 먹이
 * @return {GrowthEntries} 갱신된 항목
 */
function applyLimitBreak(entries, cardId, stage, snackCost) {
    return withEntry(spendSnack(entries, cardId, snackCost), cardId, (entry) => {
        entry.limitBreak = stage;
    });
}
/**
 * 간식을 적립하고 가능한 모든 한계돌파를 수동 명령과 같은 차감 규칙으로 적용한다.
 * @param {GrowthEntries} entries 현재 성장
 * @param {number} cardId 적립할 카드
 * @param {number} amount 이번 중복 간식
 * @param {LimitBreakCurve} curve 트랜잭션 밖에서 검증한 단계표
 * @return {object} 갱신할 성장과 이번 카드에만 귀속되는 연출 정보
 */
function addSnackAndGrow(entries, cardId, amount, curve) {
    if (cardId <= 0 || amount <= 0)
        return { entries };
    let next = addSnack(entries, cardId, amount);
    const fromStage = Math.max(0, next[String(cardId)].limitBreak);
    let toStage = fromStage;
    let hpGain = 0;
    let snackCost = 0;
    while (toStage < curve.maxStage) {
        const step = (0, limitBreakTable_1.limitBreakStep)(curve, toStage + 1);
        if (step === null)
            throw new Error(`Missing limit break stage: ${toStage + 1}`);
        if (!canAffordSnack(next, cardId, step.snackCost))
            break;
        next = applyLimitBreak(next, cardId, step.stage, step.snackCost);
        toStage = step.stage;
        hpGain += step.hpGain;
        snackCost += step.snackCost;
    }
    return toStage === fromStage ? { entries: next } : {
        entries: next,
        snackGrowth: { fromStage, toStage, hpGain, snackCost, snackLeft: snackOf(next, cardId) },
    };
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
        if (entry.level <= exports.BASE_LEVEL && entry.snack <= 0 && entry.limitBreak <= 0 && (entry.shardProgress ?? 0) <= 0)
            continue;
        pruned[key] = { ...entry };
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