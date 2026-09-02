"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.STARTER_DECK_NAME = exports.STARTER_DECK_SIZE = exports.DECK_SLOT_COUNT = exports.STARTER_GOLD = void 0;
exports.buildFreshAccountBalances = buildFreshAccountBalances;
exports.buildFreshAccountSlots = buildFreshAccountSlots;
const wallet_1 = require("../currency/wallet");
const generateNickname_1 = require("../profile/generateNickname");
/**
 * 신규 계정 최초 지급 골드. 이 상수 하나가 진실원이다 — 클라 쪽 쌍둥이였던
 * CurrencyManager.STARTING_GOLD 는 C6.4 에서 삭제됐고, 잔액은 서버 지갑만 정한다.
 */
exports.STARTER_GOLD = 100;
/** 덱 슬롯 개수. 클라 DeckSaveManager.SLOT_COUNT 와 같아야 한다 — NormalizedSlots 가 항상 이 길이로 패딩한다. */
exports.DECK_SLOT_COUNT = 6;
/** 덱 한 벌의 장수. 클라 DeckSaveManager.DECK_SIZE. */
exports.STARTER_DECK_SIZE = 6;
/** 스타터 덱 이름. 클라 StarterDeck.DECK_NAME. */
exports.STARTER_DECK_NAME = "스타터 덱";
/**
 * 신규 계정 지갑의 최초 잔액. 세이브 문서를 만드는 **그 트랜잭션**에서 같이 서야 한다
 * — 갈라지면 초기화의 ensureWallet 이 0 잔액 지갑을 먼저 세워 스타터 골드가 영영 사라진다.
 * @return {Balances} 4키 잔액
 */
function buildFreshAccountBalances() {
    return (0, wallet_1.grant)({}, [{ currency: "Gold", amount: exports.STARTER_GOLD }]);
}
/**
 * 신규 계정 문서의 슬롯 9개. 메타 5키(schemaVersion/revision/updatedAt/deviceId/appVersion)는
 * ensureSaveDocument 가 얹는다. 재화는 여기 없다 — v8 부터 잔액은 지갑 문서의 것이고,
 * 최초 지급은 buildFreshAccountBalances 가 같은 트랜잭션에서 낸다.
 *
 * 모양의 진실원은 Tools/firestore-rules-tests/fixtures/saveDocument.js 의
 * serverFreshAccountDocument() 다 — 저기와 갈리면 신규 계정의 첫 클라 저장이 룰에 막힌다.
 * @param {number[]} starterCardIds 지급할 카드 id (STARTER_DECK_SIZE 장)
 * @param {string} nickname 문서에 굳힐 기본 닉네임 (기본: 낱말표에서 한 벌 추첨)
 * @return {SlotPatch} 슬롯 9개
 */
function buildFreshAccountSlots(starterCardIds, nickname = (0, generateNickname_1.generateNickname)()) {
    const slots = [];
    for (let i = 0; i < exports.DECK_SLOT_COUNT; i++) {
        slots.push(i === 0 ?
            // imageKey 는 빈 문자열로 둔다 — 덱 이미지 카탈로그는 클라 SO 라 서버가 키를 모른다.
            // DeckImages.ResolveForSlot 이 빈 키를 첫 카드 아트로 폴백하는 정상 경로다.
            { name: exports.STARTER_DECK_NAME, cardIds: [...starterCardIds], imageKey: "" } :
            { name: "", cardIds: [], imageKey: "" });
    }
    return {
        ownership: { cardIds: [...starterCardIds] },
        deck: { slots },
        cardGrowth: { entries: {} },
        keywordGrowth: { levels: {} },
        rank: { points: 0, claimedTiers: [] },
        albumReward: { claimedKeys: [] },
        tournament: { clearedNodeIds: [], claimedChapterIds: [], pendingRewardNodeId: "" },
        tutorial: {
            outgameCompleted: false,
            chapterIndex: 0,
            chapterStepIndex: 0,
            stepId: 0,
            // -1 은 "아직 초기화 좌표를 본 적 없다"는 뜻이다. 0 으로 두면 클라 TutorialSaveData 초기값과 갈린다.
            lastBootChapterIndex: -1,
            lastBootStepIndex: -1,
            sameCoordBootCount: 0,
            completedTriggers: [],
        },
        // 닉네임만 값이 실린다 — 계정이 생기는 이 자리에서 한 번 뽑아 굳힌다. 클라가 폴백으로 만들면
        // 저장 전 세션마다 이름이 달라지고, 서버(매칭·랭킹)가 이름을 쓸 때 빈 값을 보게 된다.
        // 아바타·프레임은 null 이 설계다 — 기본 id 를 세이브에 굳히지 않고 ProfileManager 가 폴백한다.
        profile: { nickname, avatarId: null, frameId: null },
    };
}
//# sourceMappingURL=freshAccount.js.map