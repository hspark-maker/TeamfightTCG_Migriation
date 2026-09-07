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
exports.enhanceCard = void 0;
const https_1 = require("firebase-functions/v2/https");
const logger = __importStar(require("firebase-functions/logger"));
const node_crypto_1 = require("node:crypto");
const firestore_1 = require("firebase-admin/firestore");
const firebaseApp_1 = require("../firebaseApp");
const saveDocument_1 = require("../save/saveDocument");
const domainReject_1 = require("../save/domainReject");
const receiptId_1 = require("../save/receiptId");
const packSpecReader_1 = require("../packs/packSpecReader");
const wallet_1 = require("../currency/wallet");
const walletStore_1 = require("../currency/walletStore");
const cardGrowth_1 = require("../growth/cardGrowth");
const enhanceRules_1 = require("../growth/enhanceRules");
const tutorialGrants_1 = require("../growth/tutorialGrants");
/** 무료 한 방이 걸린 축. 키워드 강화와 다른 축이라 따로 소진된다. */
const FREE_SHOT_AXIS = "enhanceCard";
/**
 * 도메인 거절. 던지기와 로그는 save/domainReject 한 곳이고, 여기 남은 것은 사유 오타를 막는 타입 관문이다.
 * @param {EnhanceReject} reason 사유 코드
 * @param {string} message 로그용 설명
 * @param {Record<string, unknown>} context 어느 값에 막혔는지
 */
function reject(reason, message, context) {
    (0, domainReject_1.rejectDomain)(reason, message, context);
}
/**
 * 카드 강화 1회. 비용 곡선·차감·성공 판정을 서버가 소유한다.
 *
 * 실패해도 비용은 나가고 레벨은 내려가지 않는다(클라 CardGrowthManager.TryEnhance 와 같은 규칙).
 * 무료 한 방은 **비용만 0으로** 만들고 성공률은 건드리지 않으며, 성공했을 때만 소진으로 찍는다
 * — 실패로 닫으면 온보딩이 시킨 성장을 유저가 제 돈으로 다시 해야 한다.
 */
exports.enhanceCard = (0, https_1.onCall)(async (request) => {
    const uid = (0, saveDocument_1.requireUid)(request.auth);
    const env = String(request.data?.env ?? "");
    const cardId = Number(request.data?.cardId ?? 0);
    const freeShotRequested = request.data?.freeShot === true;
    if (!(0, saveDocument_1.isKnownEnv)(env)) {
        throw new https_1.HttpsError("invalid-argument", `Unknown env: ${env}`);
    }
    if (!Number.isInteger(cardId) || cardId <= 0) {
        throw new https_1.HttpsError("invalid-argument", "cardId must be a positive integer.");
    }
    // 스펙 읽기는 트랜잭션 밖이다 — 유저 문서와 무관하고, 재실행마다 다시 읽으면 비용만 는다.
    const [ruleRows, overrideRows] = await Promise.all([
        (0, packSpecReader_1.readSpecRows)(env, "CardEnhanceRule"),
        (0, packSpecReader_1.readSpecRows)(env, "CardEnhance"),
    ]);
    const rule = (0, enhanceRules_1.parseCardEnhanceRule)(ruleRows);
    if (rule === null) {
        // 곡선 없이 차감할 수는 없다. 이 로그가 뜨면 스펙 업로드가 빠진 것이고 강화가 통째로 막힌다.
        logger.error("CardEnhanceRule spec is unusable", { uid, env, rowCount: ruleRows.length });
        reject("RuleUnavailable", "Card enhance rule is not authored.", { uid, env, rowCount: ruleRows.length });
    }
    const overrides = (0, enhanceRules_1.parseCardEnhanceOverrides)(overrideRows);
    let outcome = "Failed";
    let level = 0;
    let currency = "";
    let cost = 0;
    let freeShotUsed = false;
    // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
    // finalize 안에서 뒤집는다 — 트랜잭션 재실행마다 다시 돌아도 결과가 같다.
    let replayed = true;
    // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
    const txId = (0, receiptId_1.clientReceiptId)(request.data?.txId, (0, node_crypto_1.randomUUID)());
    const result = await (0, saveDocument_1.mutateSave)(env, uid, "enhanceCard", { kind: "client", txId }, async (current, transaction, wallet) => {
        // 트랜잭션이 재실행되면 이전 판정을 버리고 다시 굴린다 — 잔액·레벨과 정합해야 한다.
        const entries = (0, cardGrowth_1.readGrowthEntries)(current.cardGrowth);
        const currentLevel = (0, cardGrowth_1.levelOfCard)(entries, cardId);
        const step = (0, enhanceRules_1.cardEnhanceStep)(rule, overrides, currentLevel + 1);
        if (step === null) {
            reject("MaxLevel", `Card ${cardId} is already at the max level.`, { uid, env, cardId, level: currentLevel, maxLevel: rule.maxLevel });
        }
        // freeShot 이 false 면 문서를 읽지도 쓰지도 않는다 — 매 강화마다 왕복을 더할 이유가 없다.
        // 읽기는 반드시 트랜잭션 안이다 — 동시 호출 둘이 같은 "미사용"을 보면 한 방이 두 번 나간다.
        const grantsReference = freeShotRequested ? (0, tutorialGrants_1.grantsRef)(firebaseApp_1.db, env, uid) : null;
        let freeShot = null;
        if (grantsReference !== null) {
            const grants = (0, tutorialGrants_1.readGrants)(await transaction.get(grantsReference));
            if ((0, tutorialGrants_1.hasFreeShot)(grants, FREE_SHOT_AXIS))
                freeShot = grants;
        }
        const charged = freeShot === null ? step.cost : 0;
        const balances = wallet.balances;
        if (!(0, wallet_1.canAfford)(balances, step.currency, charged)) {
            reject("NotAffordable", `Not enough ${step.currency} to enhance card ${cardId}.`, { uid, env, cardId, level: currentLevel, currency: step.currency, cost: charged,
                balance: balances[step.currency] });
        }
        const succeeded = (0, enhanceRules_1.rollSucceeded)(step.successPermille, node_crypto_1.randomInt);
        if (succeeded && grantsReference !== null && freeShot !== null) {
            (0, tutorialGrants_1.writeGrantUsed)(transaction, grantsReference, FREE_SHOT_AXIS, firestore_1.FieldValue.serverTimestamp());
        }
        outcome = succeeded ? "Success" : "Failed";
        level = succeeded ? step.level : currentLevel;
        currency = step.currency;
        cost = charged;
        freeShotUsed = succeeded && freeShot !== null;
        return {
            slots: {
                cardGrowth: (0, cardGrowth_1.growthSlot)(succeeded ? (0, cardGrowth_1.applyEnhanceLevel)(entries, cardId, step.level) : entries),
            },
            wallet: (0, walletStore_1.nextWallet)(wallet, (0, wallet_1.spend)(balances, step.currency, charged), "enhanceCard"),
        };
    }, (adopted) => {
        replayed = false;
        return { ...adopted, outcome, level, currency, cost, freeShotUsed };
    });
    if (replayed) {
        logger.info("receipt replay", { uid, env, source: "enhanceCard", txId, revision: result.revision });
    }
    else {
        logger.info("enhanceCard", {
            uid, env, cardId, outcome, level, currency, cost,
            freeShotRequested, freeShotUsed,
            revision: result.revision,
            txIdSource: (0, receiptId_1.isClientReceiptId)(request.data?.txId) ? "client" : "server",
        });
    }
    return result;
});
//# sourceMappingURL=enhanceCard.js.map