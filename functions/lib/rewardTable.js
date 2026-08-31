"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.MAX_CLAIMED_TIERS = exports.CHAPTER_OWNER_PREFIX = void 0;
exports.parseRewardRows = parseRewardRows;
exports.resolveRewards = resolveRewards;
exports.isChapterOwnerId = isChapterOwnerId;
exports.judgeRewardClaim = judgeRewardClaim;
exports.appendClaimedTier = appendClaimedTier;
// Reward 스펙 표(시트) 해석. **재화 축 전용**이다 - 랭크 점수 계산(payout.ts)과 소유를 나눈다.
//
// 순수 모듈 제약: firebase-admin · HttpsError 를 들이지 마라. functions/scripts 의 회귀가
// lib/ 를 직접 require 하고 돈다.
const currencyKeys_1 = require("./currency/currencyKeys");
function finiteInteger(value, field) {
    if (typeof value !== "number" || !Number.isSafeInteger(value))
        throw new Error(`invalid ${field}`);
    return value;
}
/**
 * 정수로 읽고 못 읽으면 0. finiteInteger 와 달리 던지지 않는다 —
 * id·order 가 비었다고 Battle 행 파싱까지 죽으면 submitMatchResult 가 통째로 멈춘다.
 * @param {unknown} value 표 값
 * @return {number} 정수
 */
function looseInteger(value) {
    const numeric = Number(value ?? 0);
    return Number.isFinite(numeric) ? Math.trunc(numeric) : 0;
}
/**
 * 재화 이름을 **엄격하게** 읽는다. currency/currencyKeys 의 parseCurrency 와 달리 Gold 로 떨어지지 않는다
 * — 보상은 못 읽으면 그 줄을 버리는 것이 규약이다(클라 RewardSpec.TryConvert 와 같은 축).
 * @param {string} value rewardId 열 값
 * @return {CurrencyKey | null} 재화 키, 못 읽으면 null
 */
function strictCurrency(value) {
    const lowered = value.trim().toLowerCase();
    return currencyKeys_1.CURRENCY_KEYS.find((key) => key.toLowerCase() === lowered) ?? null;
}
function parseRewardRows(rows) {
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
function resolveRewards(rows, ownerType, ownerId) {
    const gains = [];
    const dropped = [];
    const seenOrders = new Set();
    // 축이 다르면 절대 섞이지 않는다 — Rank/"1" 과 Tournament/"1" 은 남남이다.
    const owned = rows
        .filter((row) => row.ownerType === ownerType && row.ownerId === ownerId)
        .sort((a, b) => (a.order - b.order) || (a.id - b.id));
    for (const row of owned) {
        const drop = (reason) => dropped.push({
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
        gains.push({ currency, amount: row.amount });
    }
    return { gains, dropped };
}
/**
 * 챕터 완주 보상의 ownerId 접두사. 챕터는 ownerType 을 정점과 공유하고(둘 다 "Tournament")
 * 이 접두사로만 갈린다 — 새 ownerType 을 만들면 Reward 표의 챕터 행까지 다시 저작해야 한다.
 */
exports.CHAPTER_OWNER_PREFIX = "chapter_";
/**
 * ownerType "Tournament" 안에서 챕터 완주인가(아니면 정점이다). 판정 표와 명령이 같은
 * 술어를 봐야 두 쪽이 다른 분기로 갈리지 않는다.
 * @param {string} ownerId 소유자 키
 * @return {boolean} 챕터 완주 키면 true
 */
function isChapterOwnerId(ownerId) {
    return ownerId.startsWith(exports.CHAPTER_OWNER_PREFIX);
}
/**
 * 수령을 허용할지 판정한다. **표가 비었으면 소유자 축과 무관하게 거절**한다 —
 * 표를 못 읽은 것과 저작이 없는 것을 함께 삼키면 토너먼트는 클리어 낙인만 남고
 * 재수령이 AlreadyClaimed 로 막혀 보상을 영영 못 받는다.
 *
 * 표는 읽혔는데 그 ownerId 행만 없는 경우는 저작 규약이다 — **토너먼트 정점만** 통과시켜 해금을
 * 넘긴다(미저작 정점이 RewardPending 으로 굳으면 진행이 끊긴다). 랭크 티어 · 도감 완성 · 챕터 완주는
 * 넘길 진행이 없고 낙인만 남으므로 거절한다 — 통과시키면 나중에 보상을 저작해도 AlreadyClaimed 로 막힌다.
 * @param {RewardRow[]} rows Reward 표 전량
 * @param {string} ownerType Tournament | Rank | Album
 * @param {string} ownerId 소유자 키
 * @return {RewardClaimJudgement} 허용 여부와 지급 목록
 */
function judgeRewardClaim(rows, ownerType, ownerId) {
    const { gains, dropped } = resolveRewards(rows, ownerType, ownerId);
    if (rows.length === 0) {
        return { allow: false, reason: "NotEligible", specEmpty: true, gains: [], dropped };
    }
    const carriesProgress = ownerType === "Tournament" && !isChapterOwnerId(ownerId);
    if (gains.length === 0 && !carriesProgress) {
        return { allow: false, reason: "RewardNotFound", specEmpty: false, gains, dropped };
    }
    return { allow: true, authored: gains.length > 0, gains, dropped };
}
/**
 * 룰이 claimedTiers 에 거는 상한. **firestore.rules:98 의
 * `request.resource.data.rank.claimedTiers.size() <= 20` 과 같이 움직여야 한다.**
 * 여기만 늘리면 서버가 룰이 거부하는 문서를 쓰고, 그 순간부터 그 계정의 모든 클라 저장이
 * PERMISSION_DENIED 로 막힌다(delete 도 룰에 막혀 복구 경로가 없다).
 */
exports.MAX_CLAIMED_TIERS = 20;
/**
 * 수령 낙인에 티어 하나를 더한다. 상한을 넘기면 null — 부르는 쪽은 문서를 쓰지 말고 거절해야 한다.
 * 계정이 벽돌이 되는 것보다 수령 하나가 거부되는 편이 낫다.
 * @param {number[]} claimed 이미 수령한 티어
 * @param {number} tier 새로 수령하는 티어
 * @return {number[] | null} 오름차순 낙인 목록, 상한 초과면 null
 */
function appendClaimedTier(claimed, tier) {
    const next = [...claimed, tier].sort((a, b) => a - b);
    return next.length > exports.MAX_CLAIMED_TIERS ? null : next;
}
//# sourceMappingURL=rewardTable.js.map