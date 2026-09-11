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
exports.openPack = void 0;
const requestMetrics_1 = require("../observability/requestMetrics");
const https_1 = require("firebase-functions/v2/https");
const logger = __importStar(require("firebase-functions/logger"));
const firestore_1 = require("firebase-admin/firestore");
const node_crypto_1 = require("node:crypto");
const firebaseApp_1 = require("../firebaseApp");
const eventNames_1 = require("../analytics/eventNames");
const analyticsEvent_1 = require("../observability/analyticsEvent");
const missionStore_1 = require("../missions/missionStore");
const period_1 = require("../missions/period");
const missionSpec_1 = require("../missions/missionSpec");
const guideMutation_1 = require("../missions/guideMutation");
const saveDocument_1 = require("../save/saveDocument");
const cardCatalog_1 = require("../packs/cardCatalog");
const packDraw_1 = require("../packs/packDraw");
const packSlots_1 = require("../packs/packSlots");
const wallet_1 = require("../currency/wallet");
const itemGrant_1 = require("../rewards/itemGrant");
const rewardTable_1 = require("../rewardTable");
const rankStore_1 = require("../rank/rankStore");
const walletStore_1 = require("../currency/walletStore");
const cardGrowth_1 = require("../growth/cardGrowth");
const snackGrowthSpec_1 = require("../growth/snackGrowthSpec");
const snackGrowthProgress_1 = require("../missions/snackGrowthProgress");
const domainReject_1 = require("../save/domainReject");
const receiptId_1 = require("../save/receiptId");
const packSpecReader_1 = require("../packs/packSpecReader");
const rankGrade_1 = require("../packs/rankGrade");
/**
 * 도메인 거절. 던지기와 로그는 save/domainReject 한 곳이고, 여기 남은 것은 사유 오타를 막는 타입 관문이다.
 * @param {PackReject} reason 사유 코드
 * @param {string} message 로그용 설명
 * @param {Record<string, unknown>} context 어느 값에 막혔는지
 */
function reject(reason, message, context) {
    (0, domainReject_1.rejectDomain)(reason, message, context);
}
/**
 * 카드팩 구매·개봉. 잠금 판정·풀 해석·차감·추첨·지급을 서버가 소유한다.
 *
 * 클라(CardPackOpener)는 같은 검사를 사전에 한 번 더 하지만 그건 왕복을 아끼는 낙관 검사이고,
 * 판정의 진실원은 여기다.
 */
exports.openPack = (0, https_1.onCall)((0, requestMetrics_1.measuredCallable)("openPack", async (request) => {
    const uid = (0, saveDocument_1.requireUid)(request.auth);
    const env = String(request.data?.env ?? "");
    const packId = String(request.data?.packId ?? "");
    if (!(0, saveDocument_1.isKnownEnv)(env)) {
        throw new https_1.HttpsError("invalid-argument", `Unknown env: ${env}`);
    }
    if (packId.length === 0 || packId.length > 64) {
        throw new https_1.HttpsError("invalid-argument", "packId must be a non-empty string.");
    }
    // 스펙 읽기는 트랜잭션 밖이다 — 유저 문서와 무관하고, 재실행마다 다시 읽으면 비용만 는다.
    const pack = await (0, packSpecReader_1.readCardPackRow)(env, packId);
    if (pack === null) {
        // 클라는 시트에 행이 없으면 SO 인스펙터 값으로 폴백하지만 서버는 SO 를 못 본다.
        // 이 로그가 뜨면 시트 저작이 빠진 것이고, 그 팩은 서버에서 영영 못 연다.
        logger.error("pack row missing from the CardPack spec", { uid, env, packId });
        reject("PackNotFound", `Pack '${packId}' is not authored in the CardPack spec.`, { uid, env, packId });
    }
    if (pack.refundAmount > 0) {
        // 환급 경로는 클라·서버 양쪽에서 죽어 있다(중복 보상은 간식). 저작 실수를 조용히 삼키지 않는다.
        logger.warn("pack authors a refund that is never paid out", { env, packId, refundAmount: pack.refundAmount });
    }
    const [dropRows, gradeRows, catalogIds, cardRows, rawRewards, catalog, ruleRows, curveRows] = await Promise.all([
        (0, packSpecReader_1.readDropRows)(env, packId),
        (0, packSpecReader_1.readRankGradeRows)(env),
        (0, cardCatalog_1.loadCatalogIds)(env),
        (0, packSpecReader_1.readSpecRows)(env, "Card"),
        pack.price > 0 ? (0, packSpecReader_1.readSpecRows)(env, "Reward") : Promise.resolve([]),
        (0, missionSpec_1.readMissionCatalog)(env),
        (0, packSpecReader_1.readSpecRows)(env, "CardEnhanceRule"),
        (0, packSpecReader_1.readSpecRows)(env, "CardLimitBreak"),
    ]);
    const snackGrowthCurve = (0, snackGrowthSpec_1.requireSnackGrowthCurve)(ruleRows, curveRows);
    const duplicateRows = (0, rewardTable_1.parseRewardRows)(rawRewards);
    const cardGrades = new Map(cardRows.map((row) => [Number(row.id), String(row.grade)]));
    let granted = [];
    const entryPoints = (0, rankGrade_1.entryPointsFromRows)(gradeRows);
    if (entryPoints === null) {
        // 임계치가 없으면 잠금이 통째로 어긋난다 — 폴백으로 돌되 반드시 보이게 남긴다.
        logger.error("RankGrade spec is unusable, falling back to built-in thresholds", { env, rowCount: gradeRows.length });
    }
    const thresholds = entryPoints ?? rankGrade_1.FALLBACK_ENTRY_POINTS;
    let drawn = [];
    let goldBefore = 0;
    let goldAfter = 0;
    let poolSize = 0;
    let missionState;
    // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
    // finalize 안에서 뒤집는다 — 트랜잭션 재실행마다 다시 돌아도 결과가 같다.
    let replayed = true;
    // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
    const txId = (0, receiptId_1.clientReceiptId)(request.data?.txId, (0, node_crypto_1.randomUUID)());
    // 기간은 여기서 **한 번만** 잰다 — 트랜잭션 콜백은 재실행되므로 그 안에서 재면
    // 경계에 걸린 호출이 어느 기간에 실릴지가 재실행 운에 달린다.
    const period = (0, period_1.missionPeriod)(Date.now());
    const result = await (0, saveDocument_1.mutateSave)(env, uid, "openPack", { kind: "client", txId }, async (current, transaction, wallet) => {
        // 독립 문서는 함께 읽고, 미션·지갑 쓰기 전에 모두 확보한다.
        const missionReference = (0, missionStore_1.missionsRef)(firebaseApp_1.db, env, uid);
        const [missionSnapshot, rankSnapshot] = await transaction.getAll(missionReference, (0, rankStore_1.rankRef)(firebaseApp_1.db, env, uid));
        const missions = (0, missionStore_1.missionBumpFromSnapshot)(missionReference, missionSnapshot, period);
        // 트랜잭션이 재실행되면 이전 추첨을 버리고 다시 뽑는다 — 잔액·소유와 정합해야 한다.
        const points = Number(rankSnapshot.data()?.points ?? current.rank?.points ?? 0);
        const grade = (0, rankGrade_1.gradeOf)(thresholds, points);
        const required = (0, rankGrade_1.parseRequiredGrade)(pack.minRankGrade);
        if (required !== null && (!(0, rankGrade_1.isRanked)(thresholds, points) || grade < required)) {
            reject("RankLocked", `Pack '${packId}' requires rank grade ${required}.`, { uid, env, packId, points, grade, required });
        }
        const pool = (0, packDraw_1.resolveDropPool)(dropRows, grade, catalogIds);
        if (pool.length === 0) {
            reject("EmptyPool", `Pack '${packId}' has no drawable card at grade ${grade}.`, { uid, env, packId, grade, dropRowCount: dropRows.length, catalogSize: catalogIds.size });
        }
        poolSize = pool.length;
        const balances = wallet.balances;
        if (!(0, wallet_1.canAfford)(balances, pack.priceType, pack.price)) {
            reject("InsufficientGold", `Not enough ${pack.priceType} for pack '${packId}'.`, { uid, env, packId, priceType: pack.priceType, price: pack.price, balance: balances[pack.priceType] });
        }
        const owned = (0, packSlots_1.readOwnedIds)(current.ownership);
        const ownedSet = new Set(owned);
        drawn = (0, packDraw_1.drawPack)(pool, pack.drawCount, pack.uniqueDraw, catalogIds, ownedSet, node_crypto_1.randomInt);
        granted = pack.price > 0 ? (0, itemGrant_1.duplicateGains)(drawn, cardGrades, duplicateRows) : [];
        const paid = (0, wallet_1.grant)((0, wallet_1.spend)(balances, pack.priceType, pack.price), granted);
        goldBefore = balances[pack.priceType];
        goldAfter = paid[pack.priceType];
        const slots = {
            ownership: (0, packSlots_1.buildOwnershipSlot)(owned, drawn),
            cardGrowth: (0, cardGrowth_1.growthSlot)((0, itemGrant_1.applyDrawnSnackGrowth)((0, cardGrowth_1.readGrowthEntries)(current.cardGrowth), drawn, snackGrowthCurve, cardGrades)),
        };
        (0, guideMutation_1.applyGuideProgress)(missions, current, slots, cardRows, catalog);
        (0, snackGrowthProgress_1.applySnackGrowthProgress)(missions, drawn);
        // 진행도는 콜백 **안**에서 올린다 — mutateSave 는 영수증이 히트하면 이 콜백을 통째로 건너뛰므로,
        // 그 덕에 재시도가 진행도를 두 번 올리지 않는다. 콜백 밖으로 옮기면 그 보장이 사라진다.
        (0, missionStore_1.commitMissionBump)(transaction, missions, eventNames_1.EVENTS.packOpened.missionKey, 1, firestore_1.FieldValue.serverTimestamp());
        missionState = (0, missionStore_1.missionResponse)(missions.state, period, catalog);
        return {
            slots,
            wallet: (0, walletStore_1.nextWallet)(wallet, paid, "openPack"),
        };
    }, (adopted) => {
        replayed = false;
        return { ...adopted, packId, cards: drawn, granted, refundType: pack.refundType, missions: missionState };
    });
    if (replayed) {
        logger.info("receipt replay", { uid, env, source: "openPack", txId, revision: result.revision });
    }
    else {
        (0, analyticsEvent_1.recordEvent)(eventNames_1.EVENTS.packOpened.name, {
            uid, env, eventId: txId, sourceCommand: "openPack", result: "success", packId,
            priceType: pack.priceType, price: pack.price,
            drawCount: pack.drawCount, uniqueDraw: pack.uniqueDraw, poolSize,
            drawn: drawn.map((card) => `${card.cardId}${card.isNew ? "+" : "="}`).join(","),
            goldBefore, goldAfter,
            specSource: entryPoints === null ? "rankFallback" : "spec",
            revision: result.revision,
            txIdSource: (0, receiptId_1.isClientReceiptId)(request.data?.txId) ? "client" : "server",
        });
    }
    return result;
}));
//# sourceMappingURL=openPack.js.map