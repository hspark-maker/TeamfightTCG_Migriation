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
exports.enhanceKeyword = void 0;
const https_1 = require("firebase-functions/v2/https");
const logger = __importStar(require("firebase-functions/logger"));
const firestore_1 = require("firebase-admin/firestore");
const firebaseApp_1 = require("../firebaseApp");
const saveDocument_1 = require("../save/saveDocument");
const domainReject_1 = require("../save/domainReject");
const packSpecReader_1 = require("../packs/packSpecReader");
const wallet_1 = require("../currency/wallet");
const walletStore_1 = require("../currency/walletStore");
const keywordGrowth_1 = require("../growth/keywordGrowth");
const enhanceRules_1 = require("../growth/enhanceRules");
const tutorialGrants_1 = require("../growth/tutorialGrants");
/** 무료 한 방이 걸린 축. 카드 강화와 다른 축이라 따로 소진된다. */
const FREE_SHOT_AXIS = "enhanceKeyword";
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
 * 키워드 강화 1회. 비용 곡선과 차감을 서버가 소유한다.
 *
 * 확률 실패가 없어 outcome 은 언제나 Success 다(클라 KeywordGrowthManager.TryEnhance 에 판정이 없다)
 * — 카드 강화와 응답 모양을 맞추려고 같은 필드를 싣는다.
 * 무료 한 방은 비용만 0으로 만들고, 성공했을 때만 소진으로 찍는다.
 */
exports.enhanceKeyword = (0, https_1.onCall)(async (request) => {
    const uid = (0, saveDocument_1.requireUid)(request.auth);
    const env = String(request.data?.env ?? "");
    const keyword = Number(request.data?.keyword ?? 0);
    const freeShotRequested = request.data?.freeShot === true;
    if (!(0, saveDocument_1.isKnownEnv)(env)) {
        throw new https_1.HttpsError("invalid-argument", `Unknown env: ${env}`);
    }
    // CardKeyword 플래그 정수다 — 이름이 아니라 1·2·4·8·16·64 로 들어온다.
    if (!Number.isInteger(keyword) || keyword <= 0) {
        throw new https_1.HttpsError("invalid-argument", "keyword must be a positive CardKeyword flag.");
    }
    if (!(0, keywordGrowth_1.isSupportedKeyword)(keyword)) {
        reject("KeywordNotSupported", `Keyword flag ${keyword} is not enhanceable.`, { uid, env, keyword });
    }
    // 스펙 읽기는 트랜잭션 밖이다 — 유저 문서와 무관하고, 재실행마다 다시 읽으면 비용만 는다.
    const rows = await (0, packSpecReader_1.readSpecRows)(env, "KeywordEnhance");
    const rule = (0, enhanceRules_1.parseKeywordEnhanceRules)(rows).get(keyword);
    if (rule === undefined) {
        // 곡선 없이 차감할 수는 없다. 이 로그가 뜨면 그 키워드 행의 저작·업로드가 빠진 것이다.
        logger.error("KeywordEnhance spec has no row for this keyword", { uid, env, keyword, rowCount: rows.length });
        reject("RuleUnavailable", `Keyword ${keyword} is not authored in the KeywordEnhance spec.`, { uid, env, keyword, rowCount: rows.length });
    }
    let level = 0;
    let currency = "";
    let cost = 0;
    let freeShotUsed = false;
    const result = await (0, saveDocument_1.mutateSave)("enhanceKeyword", env, uid, async (current, transaction, wallet) => {
        const levels = (0, keywordGrowth_1.readKeywordLevels)(current.keywordGrowth);
        const currentLevel = (0, keywordGrowth_1.levelOfKeyword)(levels, keyword);
        const step = (0, enhanceRules_1.keywordEnhanceStep)(rule, currentLevel);
        if (step === null) {
            reject("MaxLevel", `Keyword ${keyword} is already at the max level.`, { uid, env, keyword, level: currentLevel, maxLevel: rule.maxLevel });
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
            reject("NotAffordable", `Not enough ${step.currency} to enhance keyword ${keyword}.`, { uid, env, keyword, level: currentLevel, currency: step.currency, cost: charged,
                balance: balances[step.currency] });
        }
        if (grantsReference !== null && freeShot !== null) {
            (0, tutorialGrants_1.writeGrantUsed)(transaction, grantsReference, FREE_SHOT_AXIS, freeShot, firestore_1.FieldValue.serverTimestamp());
        }
        level = step.level;
        currency = step.currency;
        cost = charged;
        freeShotUsed = freeShot !== null;
        return {
            slots: {
                keywordGrowth: (0, keywordGrowth_1.keywordGrowthSlot)((0, keywordGrowth_1.setKeywordLevel)(levels, keyword, step.level)),
            },
            wallet: (0, walletStore_1.nextWallet)(wallet, (0, wallet_1.spend)(balances, step.currency, charged)),
        };
    });
    logger.info("enhanceKeyword", {
        uid, env, keyword, level, currency, cost,
        freeShotRequested, freeShotUsed,
        revision: result.revision,
    });
    return { ...result, outcome: "Success", level, currency, cost, freeShotUsed };
});
//# sourceMappingURL=enhanceKeyword.js.map