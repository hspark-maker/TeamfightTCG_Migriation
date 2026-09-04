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
exports.findAiMatch = void 0;
const firestore_1 = require("firebase-admin/firestore");
const https_1 = require("firebase-functions/v2/https");
const logger = __importStar(require("firebase-functions/logger"));
const node_crypto_1 = require("node:crypto");
const firebaseApp_1 = require("../firebaseApp");
const aiDeckDraw_1 = require("../matchmaking/aiDeckDraw");
const matchPairing_1 = require("../matchPairing");
const countedTransaction_1 = require("../observability/countedTransaction");
const payout_1 = require("../payout");
const payloadGuards_1 = require("../match/payloadGuards");
const receiptId_1 = require("../save/receiptId");
const saveDocument_1 = require("../save/saveDocument");
const specBlobReader_1 = require("../specs/specBlobReader");
const DECK_SIZE = 6;
function parseData(raw) {
    const data = (0, payloadGuards_1.objectRecord)(raw);
    const env = data?.env;
    if ((env !== "live" && env !== "test") || !(0, saveDocument_1.isKnownEnv)(env)) {
        throw new https_1.HttpsError("invalid-argument", "invalid AI match payload");
    }
    const fingerprint = data?.contentFingerprint;
    const rawPlayerDeck = data?.playerDeck;
    // 서버 선배포 창. 구 클라는 env만 보내고 덱 선택만 받는다. 새 필드 중 하나만 보낸 반쪽
    // 페이로드는 구 버전으로 접지 않는다 — 매치가 봉인됐다고 믿는 클라를 만들 수 있다.
    if (fingerprint == null && rawPlayerDeck == null)
        return { env, legacy: true };
    if (typeof fingerprint !== "string" || !payloadGuards_1.HEX_64.test(fingerprint) ||
        !Array.isArray(rawPlayerDeck) || rawPlayerDeck.length !== DECK_SIZE) {
        throw new https_1.HttpsError("invalid-argument", "invalid AI match payload");
    }
    const playerDeck = rawPlayerDeck.map((rawCard) => (0, payloadGuards_1.safeInteger)(rawCard));
    if (playerDeck.some((cardId) => cardId == null || cardId <= 0) ||
        new Set(playerDeck).size !== DECK_SIZE) {
        throw new https_1.HttpsError("invalid-argument", "invalid player deck");
    }
    return {
        env,
        legacy: false,
        contentFingerprint: fingerprint,
        playerDeck: playerDeck,
        // 정상 클라는 ServerSaveCommands가 같은 재시도에 같은 유효 txId를 싣는다. fallback UUID는
        // txId가 없는 수동 호출용일 뿐이며 clientReceiptId는 유효한 원본을 그대로 반환한다.
        txId: (0, receiptId_1.clientReceiptId)(data?.txId, (0, node_crypto_1.randomUUID)()),
        resultProtocol: data?.resultProtocol === 1 ? 1 : 0,
    };
}
function shuffle(cards) {
    const result = [...cards];
    for (let index = result.length - 1; index > 0; index--) {
        const target = (0, node_crypto_1.randomInt)(index + 1);
        [result[index], result[target]] = [result[target], result[index]];
    }
    return result;
}
function sameCards(left, right) {
    if (left.length !== right.length)
        return false;
    const a = [...left].sort((x, y) => x - y);
    const b = [...right].sort((x, y) => x - y);
    return a.every((cardId, index) => cardId === b[index]);
}
function storedResponse(raw, data) {
    const aiDeck = (0, payloadGuards_1.objectRecord)(raw.aiDeck);
    const boardOrders = (0, payloadGuards_1.objectRecord)(raw.serverBoardOrders);
    const deck = aiDeck?.cardIds;
    const playerBoardOrder = boardOrders?.owner0;
    const enemyBoardOrder = boardOrders?.owner1;
    const playerDeck = raw.playerDeckCardIds;
    if (aiDeck == null || raw.mode !== "solo" || raw.cardDataVersion !== data.contentFingerprint ||
        typeof raw.matchId !== "string" || !payloadGuards_1.HEX_32.test(raw.matchId) ||
        typeof raw.seedHex !== "string" || !payloadGuards_1.HEX_16.test(raw.seedHex) ||
        !Number.isInteger(raw.rulesetVersion) || !Array.isArray(playerDeck) ||
        !playerDeck.every((value) => Number.isSafeInteger(value)) ||
        !sameCards(playerDeck, data.playerDeck) ||
        !Array.isArray(deck) || !Array.isArray(playerBoardOrder) || !Array.isArray(enemyBoardOrder) ||
        deck.length !== DECK_SIZE || playerBoardOrder.length !== DECK_SIZE ||
        enemyBoardOrder.length !== DECK_SIZE || !Number.isInteger(aiDeck?.cardLevel))
        return null;
    return {
        revision: 0,
        matchId: raw.matchId,
        seedHex: raw.seedHex,
        rulesetVersion: raw.rulesetVersion,
        deck,
        cardLevel: aiDeck.cardLevel,
        playerBoardOrder,
        enemyBoardOrder,
        resultProtocol: raw.resultProtocol === 1 ? 1 : 0,
    };
}
/**
 * 실시간 상대가 없을 때 AI 덱과 재시뮬 입력을 한 매치 문서에 봉인한다.
 * 플레이어 덱의 성장·소유 검증은 이어지는 lockDeck이 같은 matchId에서 수행한다.
 */
exports.findAiMatch = (0, https_1.onCall)(async (request) => {
    const uid = (0, saveDocument_1.requireUid)(request.auth);
    const data = parseData(request.data);
    const [snapshot, rankRows, deckRows] = await Promise.all([
        (0, saveDocument_1.saveDocument)(data.env, uid).get(),
        (0, specBlobReader_1.readSpecRows)(data.env, "RankGrade"),
        (0, specBlobReader_1.readSpecRows)(data.env, "AIDeck"),
    ]);
    if (!snapshot.exists)
        throw new https_1.HttpsError("failed-precondition", "Save document is missing.");
    const rank = snapshot.data()?.rank;
    const rawPoints = rank?.points;
    const points = Number.isSafeInteger(rawPoints) ? rawPoints : 0;
    const grades = (0, payout_1.parseRankGradeRows)(rankRows);
    if (grades.length === 0)
        throw new https_1.HttpsError("failed-precondition", "RankGrade spec is empty.");
    try {
        const parsed = (0, aiDeckDraw_1.parseAiDeckRows)(deckRows);
        if (parsed.skipped.length > 0) {
            logger.warn("AIDeck rows skipped", { env: data.env, skipped: parsed.skipped });
        }
        const tierIndex = (0, payout_1.resolveTierIndex)(points, grades);
        const draw = (0, aiDeckDraw_1.drawAiDeck)(parsed.rows, tierIndex, node_crypto_1.randomInt);
        if (draw === null)
            throw new Error("AIDeck spec has no usable row");
        if (data.legacy) {
            logger.info("legacy AI deck selected", { uid, env: data.env, tierIndex, deckId: draw.deckId });
            return { revision: 0, deck: draw.deck, cardLevel: draw.cardLevel };
        }
        const authoredData = data;
        const specPins = await (0, specBlobReader_1.readSpecPins)(authoredData.env, specBlobReader_1.BATTLE_REPLAY_SPEC_TABLES);
        if ((0, specBlobReader_1.fingerprintOfSpecPins)(authoredData.env, specPins, ["Card"]) !== authoredData.contentFingerprint) {
            throw new https_1.HttpsError("failed-precondition", "content_fingerprint_mismatch");
        }
        // 같은 txId 재시도는 같은 문서를 읽는다. callable 응답 유실 뒤 클라가 재시도해도
        // AI 덱·시드·보드 순서가 바뀐 유령 매치를 하나 더 만들지 않는다.
        const matchId = (0, node_crypto_1.createHash)("sha256").update(`${uid}:${authoredData.txId}`, "utf8")
            .digest("hex").slice(0, 32);
        const matchRef = firebaseApp_1.db.doc(`envs/${authoredData.env}/matches/${matchId}`);
        const seedHex = (0, node_crypto_1.randomBytes)(8).toString("hex");
        const playerBoardOrder = shuffle(authoredData.playerDeck);
        const enemyBoardOrder = shuffle(draw.deck);
        const now = firestore_1.Timestamp.now();
        const response = await (0, countedTransaction_1.withCountedTransaction)("findAiMatch", async (tx) => {
            const prior = await tx.get(matchRef);
            if (prior.exists) {
                const stored = storedResponse(prior.data() ?? {}, authoredData);
                if (stored == null)
                    throw new https_1.HttpsError("already-exists", "AI match receipt was reused");
                return stored;
            }
            tx.set(matchRef, {
                matchId,
                env: authoredData.env,
                phase: "pairing",
                status: "pending",
                pairingStatus: "paired",
                seedSource: "server",
                seedHex,
                rulesetVersion: matchPairing_1.SERVER_RULESET_VERSION,
                cardDataVersion: authoredData.contentFingerprint,
                specPins,
                participantUids: [uid],
                expectedParticipants: 1,
                mode: "solo",
                resultProtocol: authoredData.resultProtocol,
                ownerIndexByUid: { [uid]: 0 },
                playerDeckCardIds: [...authoredData.playerDeck].sort((a, b) => a - b),
                aiDeck: {
                    deckId: draw.deckId,
                    cardIds: draw.deck,
                    cardLevel: draw.cardLevel,
                },
                serverBoardOrders: {
                    owner0: playerBoardOrder,
                    owner1: enemyBoardOrder,
                },
                pairingCreatedAt: now,
                pairedAt: firestore_1.FieldValue.serverTimestamp(),
                expiresAt: firestore_1.Timestamp.fromMillis(now.toMillis() + matchPairing_1.MATCH_PAIRING_TTL_MS),
                updatedAt: firestore_1.FieldValue.serverTimestamp(),
            });
            return {
                revision: 0,
                matchId,
                seedHex,
                rulesetVersion: matchPairing_1.SERVER_RULESET_VERSION,
                deck: draw.deck,
                cardLevel: draw.cardLevel,
                playerBoardOrder,
                enemyBoardOrder,
                resultProtocol: authoredData.resultProtocol,
            };
        });
        logger.info("AI match created", {
            uid, env: authoredData.env, matchId, tierIndex, deckId: draw.deckId,
        });
        return response;
    }
    catch (error) {
        if (error instanceof https_1.HttpsError)
            throw error;
        logger.error("AI match creation failed", { uid, env: data.env, error });
        throw new https_1.HttpsError("failed-precondition", "AI match could not be created.");
    }
});
//# sourceMappingURL=findAiMatch.js.map