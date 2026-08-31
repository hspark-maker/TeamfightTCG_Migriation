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
const https_1 = require("firebase-functions/v2/https");
const logger = __importStar(require("firebase-functions/logger"));
const node_crypto_1 = require("node:crypto");
const saveDocument_1 = require("../save/saveDocument");
const cardCatalog_1 = require("../packs/cardCatalog");
const packDraw_1 = require("../packs/packDraw");
const packSlots_1 = require("../packs/packSlots");
const wallet_1 = require("../currency/wallet");
const walletStore_1 = require("../currency/walletStore");
const cardGrowth_1 = require("../growth/cardGrowth");
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
exports.openPack = (0, https_1.onCall)(async (request) => {
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
    const [dropRows, gradeRows, catalogIds] = await Promise.all([
        (0, packSpecReader_1.readDropRows)(env, packId),
        (0, packSpecReader_1.readRankGradeRows)(env),
        (0, cardCatalog_1.loadCatalogIds)(env),
    ]);
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
    // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
    // finalize 안에서 뒤집는다 — 트랜잭션 재실행마다 다시 돌아도 결과가 같다.
    let replayed = true;
    // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
    const txId = (0, receiptId_1.clientReceiptId)(request.data?.txId, (0, node_crypto_1.randomUUID)());
    const result = await (0, saveDocument_1.mutateSave)(env, uid, "openPack", { kind: "client", txId }, (current, _transaction, wallet) => {
        // 트랜잭션이 재실행되면 이전 추첨을 버리고 다시 뽑는다 — 잔액·소유와 정합해야 한다.
        const points = Number(current.rank?.points ?? 0);
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
        const paid = (0, wallet_1.spend)(balances, pack.priceType, pack.price);
        goldBefore = balances[pack.priceType];
        goldAfter = paid[pack.priceType];
        return {
            slots: {
                ownership: (0, packSlots_1.buildOwnershipSlot)(owned, drawn),
                cardGrowth: (0, cardGrowth_1.growthSlot)(drawn.reduce((entries, card) => (0, cardGrowth_1.addSnack)(entries, card.cardId, card.snack), (0, cardGrowth_1.readGrowthEntries)(current.cardGrowth))),
            },
            wallet: (0, walletStore_1.nextWallet)(wallet, paid, "openPack"),
        };
    }, (adopted) => {
        replayed = false;
        return { ...adopted, packId, cards: drawn, refundType: pack.refundType };
    });
    if (replayed) {
        logger.info("receipt replay", { uid, env, source: "openPack", txId, revision: result.revision });
    }
    else {
        logger.info("openPack", {
            uid, env, packId,
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
});
//# sourceMappingURL=openPack.js.map