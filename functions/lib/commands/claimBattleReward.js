"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.claimBattleReward = void 0;
const node_crypto_1 = require("node:crypto");
const firestore_1 = require("firebase-admin/firestore");
const https_1 = require("firebase-functions/v2/https");
const logger = __importStar(require("firebase-functions/logger"));
const firebaseApp_1 = require("../firebaseApp");
const saveDocument_1 = require("../save/saveDocument");
const domainReject_1 = require("../save/domainReject");
const packSpecReader_1 = require("../packs/packSpecReader");
const payout_1 = require("../payout");
const rewardTable_1 = require("../rewardTable");
const currencyKeys_1 = require("../currency/currencyKeys");
const wallet_1 = require("../currency/wallet");
const walletStore_1 = require("../currency/walletStore");
const walletTransaction_1 = require("../currency/walletTransaction");
const receiptId_1 = require("../save/receiptId");
const payloadGuards_1 = require("../match/payloadGuards");
const deckValidation_1 = require("../deckValidation");
/**
 * 도메인 거절. 던지기와 로그는 save/domainReject 한 곳이고, 여기 남은 것은 사유 오타를 막는 타입 관문이다.
 * @param {BattleRewardReject} reason 사유 코드
 * @param {string} message 로그용 설명
 * @param {Record<string, unknown>} context 어느 값에 막혔는지
 */
function reject(reason, message, context) {
    (0, domainReject_1.rejectDomain)(reason, message, context);
}
/**
 * 생존 카드 수를 [0, LOCKED_DECK_SIZE] 로 자른다. **거절하지 않는다** —
 * 클라가 이상한 수를 하나 보냈다고 세션을 끊으면 전투를 이기고도 진행이 막힌다.
 * @param {number} remaining 클라가 보낸 생존 수
 * @return {number} 잘라낸 생존 수
 */
function clampRemaining(remaining) {
    if (!Number.isFinite(remaining))
        return 0;
    return Math.min(Math.max(Math.trunc(remaining), 0), deckValidation_1.LOCKED_DECK_SIZE);
}
/**
 * Battle 지급량을 구한다. computeCurrencyPayout 은 표가 비었거나 win.perCard·win.floor·lose.flat 행이
 * 없거나 승/패 재화가 갈리면 던진다 — 그대로 새면 internal 이 되어 클라 분류가 흐려지므로 여기서 거절로 바꾼다.
 * @param {boolean} won 승리 여부
 * @param {number} remaining 생존 카드 수
 * @param {RewardRow[]} rows Reward 표 전량
 * @param {Record<string, unknown>} context 로그 맥락
 * @return {CurrencyPayout} 지급 재화와 수량
 */
function resolvePayout(won, remaining, rows, context) {
    try {
        return (0, payout_1.computeCurrencyPayout)(won, remaining, rows);
    }
    catch (error) {
        // 저작 사고라 유저가 할 수 있는 것이 없다 — 어느 ownerId 가 깨졌는지가 유일한 단서다.
        logger.error("Battle reward rows are unusable", { ...context, error: String(error) });
        reject("RewardUnavailable", `Battle reward rows are unusable: ${String(error)}`, context);
    }
}
/**
 * 요청이 지목한 매치 id. **없거나 형식을 벗어나면 여기서 접는다** — matchId 없이는
 * 자격을 소진시킬 문서가 없어서 `won`·`remaining` 이 클라 자유값이 되고, 새 txId 를 붙인
 * 반복 호출이 그대로 무제한 지급이 된다(영수증은 같은 txId 만 막는다).
 *
 * invalid-argument 가 아니라 도메인 거절인 이유는 domainReject 의 계약이다 —
 * 보상 지급 실패로 세션을 끊지 않는다. 클라는 경고 한 줄로 삼키고 획득 연출을 세우지 않는다.
 * @param {unknown} raw 요청의 matchId
 * @param {Record<string, unknown>} context 로그 맥락
 * @return {string} 매치 id
 */
function requireMatchId(raw, context) {
    if (typeof raw === "string" && payloadGuards_1.HEX_32.test(raw))
        return raw;
    reject("MatchUnverified", `Battle reward requires a match id: ${String(raw)}`, context);
}
/**
 * 지급 자격을 매치 문서에 묶는다. 이 명령은 전투를 재시뮬하지 않으므로 `won`·`remaining` 의
 * 진위는 여전히 클라 신고다 — 여기서 막는 것은 **같은 자격의 반복 청구**다.
 * findAiMatch 가 연 solo 매치 하나당 지급도 하나로 끊는다.
 *
 * `phase === "locked"` 를 요구하는 이유: lockDeck 이 소유·성장을 세이브와 대조해 승인한
 * 매치만 전투로 넘어간다. 봉인만 되고(pairing) 전투를 시작하지 않은 매치로는 청구할 수 없다.
 * @param {string} env 환경 id
 * @param {string} uid 유저 uid
 * @param {string} matchId 매치 id
 * @param {Record<string, unknown>} context 로그 맥락
 * @return {WalletGuard} 지갑 트랜잭션이 같은 커밋에서 검사·낙인할 자격 문서
 */
function matchGuard(env, uid, matchId, context) {
    const ref = firebaseApp_1.db.doc(`envs/${env}/matches/${matchId}`);
    return {
        ref,
        verify: (snapshot) => {
            const match = snapshot.exists ? snapshot.data() ?? {} : null;
            if (match === null) {
                reject("MatchUnverified", `Battle reward match is missing: ${matchId}`, context);
            }
            // 이미 받아 갔다. 사유를 MatchUnverified 와 가르는 이유는 로그다 —
            // 하나는 위조 시도이고 하나는 응답을 잃은 정상 클라의 재청구라 대응이 다르다.
            if (match.rewardClaimed === true) {
                reject("AlreadyClaimed", `Battle reward was already claimed: ${matchId}`, context);
            }
            const participants = Array.isArray(match.participantUids) ? match.participantUids : [];
            if (match.mode !== "solo" || match.resultProtocol === 1 ||
                !participants.includes(uid) || match.phase !== "locked") {
                reject("MatchUnverified", `Battle reward match is not a locked solo match: ${matchId}`, {
                    ...context, mode: match.mode, phase: match.phase, resultProtocol: match.resultProtocol ?? 0,
                    participants: participants.length,
                });
            }
        },
        stamp: {
            rewardClaimed: true,
            rewardClaimedBy: uid,
            rewardClaimedAt: firestore_1.FieldValue.serverTimestamp(),
            updatedAt: firestore_1.FieldValue.serverTimestamp(),
        },
    };
}
/**
 * 싱글 전투 1판의 보상 지급. 금액 공식(perCard × 생존 수 vs floor · 패배는 flat)은 payout.ts 가 갖고,
 * 여기서는 표 읽기 · 클램프 · 지급만 한다.
 *
 * **자격은 matchId 하나로 끊는다.** findAiMatch 가 연 solo 매치 문서를 지갑과 같은 트랜잭션에서
 * 낙인하므로(matchGuard) 매치 한 판당 지급은 한 번뿐이다. 영수증(txId)만으로는 못 막는다 —
 * 클라가 새 txId 를 붙이면 매번 새 지급이 성립한다.
 *
 * 다만 `won`·`remaining` 자체는 아직 클라 신고다. 이 명령은 전투를 재시뮬하지 않는다.
 * resultProtocol=1 싱글은 submitMatchResult로 이동했고, 이 레거시 경로에서는 "무한 반복"만 막히며
 * "한 판에 대해 부풀린 신고" 는 남아 있다 — 그건 서버 재시뮬을 solo 로 넓혀야 닫힌다.
 *
 * 세이브 문서는 건드리지 않는다 — 이 명령이 움직이는 것은 잔액뿐이라 진행도 슬롯도 revision 도 오를 이유가 없다.
 * 지급량이 0 이하로 나오면 **아무것도 쓰지 않는다** — 빈 지급으로 지갑 rev 만 올리면
 * 클라가 잔액을 갈아끼우고도 달라진 것이 없어 사고를 못 알아챈다.
 */
exports.claimBattleReward = (0, https_1.onCall)(async (request) => {
    const uid = (0, saveDocument_1.requireUid)(request.auth);
    const env = String(request.data?.env ?? "");
    const won = request.data?.won === true;
    if (!(0, saveDocument_1.isKnownEnv)(env)) {
        throw new https_1.HttpsError("invalid-argument", `Unknown env: ${env}`);
    }
    const matchId = requireMatchId(request.data?.matchId, { uid, env, won });
    const requested = Number(request.data?.remaining ?? 0);
    const remaining = clampRemaining(requested);
    if (remaining !== requested) {
        logger.warn("claimBattleReward remaining clamped", { uid, env, won, requested: request.data?.remaining, remaining });
    }
    // 스펙 읽기는 트랜잭션 밖이다 — 유저 문서와 무관하고, 재실행마다 다시 읽으면 비용만 는다.
    const rows = (0, rewardTable_1.parseRewardRows)(await (0, packSpecReader_1.readSpecRows)(env, "Reward"));
    const context = {
        uid, env, won, remaining, matchId,
        rowCount: rows.length,
        battleOwnerIds: rows.filter((r) => r.ownerType === "Battle").map((r) => r.ownerId),
    };
    const payout = resolvePayout(won, remaining, rows, context);
    const currency = currencyKeys_1.CURRENCY_KEYS.find((key) => key === payout.currency);
    if (currency === undefined) {
        // parseCurrency 의 Gold 폴백을 쓰지 않는다 — 저작 오타를 조용히 금화로 바꾸면 표가 틀린 채로 굳는다.
        // 그냥 흘려보내도 안 된다: wallet 의 changeBalances 가 NaN 을 만들고 normalize 가 그 키를 버려
        // **0 지급 + 성공 응답**이 된다.
        logger.error("Battle reward currency is not a known key", { ...context, currency: payout.currency });
        reject("RewardUnavailable", `Battle reward currency is unknown: ${payout.currency}`, context);
    }
    if (payout.amount <= 0) {
        // error 가 아니라 warn 이다 — 표가 깨진 것이 아니라 "이번엔 줄 것이 없다" 일 수 있다(예: lose.flat 0 저작).
        // 그래도 거절로 접는다: 지급 0으로 지갑을 쓰면 rev 만 오르고 클라는 달라진 것 없는 잔액을 채택한다.
        // 클라는 이 거절을 경고 한 줄로 삼키고 캐리어를 세우지 않는다(획득 연출 없음) — 0 지급의 옳은 표면이다.
        logger.warn("Battle reward amount is not positive", { ...context, amount: payout.amount });
        reject("RewardUnavailable", `Battle reward amount is not positive: ${payout.amount}`, context);
    }
    const amount = payout.amount;
    // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
    // finalize 안에서 뒤집는다 — 트랜잭션 재실행마다 다시 돌아도 결과가 같다.
    let replayed = true;
    // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
    const txId = (0, receiptId_1.clientReceiptId)(request.data?.txId, (0, node_crypto_1.randomUUID)());
    const result = await (0, walletTransaction_1.mutateWallet)(env, uid, "claimBattleReward", { kind: "client", txId }, (current) => (0, walletStore_1.nextWallet)(current, (0, wallet_1.grant)(current.balances, [{ currency, amount }]), "claimBattleReward"), (wallet) => {
        replayed = false;
        return { wallet, granted: { currency, amount } };
    }, matchGuard(env, uid, matchId, context));
    if (replayed) {
        logger.info("receipt replay", { uid, env, source: "claimBattleReward", txId, matchId, rev: result.wallet.rev });
    }
    else {
        logger.info("claimBattleReward", {
            uid, env, won, remaining, matchId, currency, amount, rev: result.wallet.rev,
            txIdSource: (0, receiptId_1.isClientReceiptId)(request.data?.txId) ? "client" : "server",
        });
    }
    return result;
});
//# sourceMappingURL=claimBattleReward.js.map