import {FieldValue, Timestamp} from "firebase-admin/firestore";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {createHash, randomBytes, randomInt, randomUUID} from "node:crypto";
import {db} from "../firebaseApp";
import {drawAiDeck, parseAiDeckRows} from "../matchmaking/aiDeckDraw";
import {MATCH_PAIRING_TTL_MS, SERVER_RULESET_VERSION} from "../matchPairing";
import {withCountedTransaction} from "../observability/countedTransaction";
import {parseRankGradeRows, resolveTierIndex} from "../payout";
import {HEX_16, HEX_32, HEX_64, objectRecord, safeInteger} from "../match/payloadGuards";
import {clientReceiptId} from "../save/receiptId";
import {isKnownEnv, requireUid, saveDocument} from "../save/saveDocument";
import {
  BATTLE_REPLAY_SPEC_TABLES,
  fingerprintOfSpecPins,
  readSpecPins,
  readSpecRows,
} from "../specs/specBlobReader";

const DECK_SIZE = 6;

type LegacyFindAiMatchData = {
  env: "live" | "test";
  legacy: true;
};

type AuthoredFindAiMatchData = {
  env: "live" | "test";
  legacy: false;
  contentFingerprint: string;
  playerDeck: number[];
  txId: string;
  resultProtocol: 0 | 1;
};

type FindAiMatchData = LegacyFindAiMatchData | AuthoredFindAiMatchData;

function parseData(raw: unknown): FindAiMatchData {
  const data = objectRecord(raw);
  const env = data?.env;
  if ((env !== "live" && env !== "test") || !isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", "invalid AI match payload");
  }

  const fingerprint = data?.contentFingerprint;
  const rawPlayerDeck = data?.playerDeck;
  // 서버 선배포 창. 구 클라는 env만 보내고 덱 선택만 받는다. 새 필드 중 하나만 보낸 반쪽
  // 페이로드는 구 버전으로 접지 않는다 — 매치가 봉인됐다고 믿는 클라를 만들 수 있다.
  if (fingerprint == null && rawPlayerDeck == null) return {env, legacy: true};
  if (typeof fingerprint !== "string" || !HEX_64.test(fingerprint) ||
      !Array.isArray(rawPlayerDeck) || rawPlayerDeck.length !== DECK_SIZE) {
    throw new HttpsError("invalid-argument", "invalid AI match payload");
  }

  const playerDeck = rawPlayerDeck.map((rawCard) => safeInteger(rawCard));
  if (playerDeck.some((cardId) => cardId == null || cardId <= 0) ||
      new Set(playerDeck).size !== DECK_SIZE) {
    throw new HttpsError("invalid-argument", "invalid player deck");
  }
  return {
    env,
    legacy: false,
    contentFingerprint: fingerprint,
    playerDeck: playerDeck as number[],
    // 정상 클라는 ServerSaveCommands가 같은 재시도에 같은 유효 txId를 싣는다. fallback UUID는
    // txId가 없는 수동 호출용일 뿐이며 clientReceiptId는 유효한 원본을 그대로 반환한다.
    txId: clientReceiptId(data?.txId, randomUUID()),
    resultProtocol: data?.resultProtocol === 1 ? 1 : 0,
  };
}

function shuffle(cards: readonly number[]): number[] {
  const result = [...cards];
  for (let index = result.length - 1; index > 0; index--) {
    const target = randomInt(index + 1);
    [result[index], result[target]] = [result[target], result[index]];
  }
  return result;
}

function sameCards(left: readonly number[], right: readonly number[]): boolean {
  if (left.length !== right.length) return false;
  const a = [...left].sort((x, y) => x - y);
  const b = [...right].sort((x, y) => x - y);
  return a.every((cardId, index) => cardId === b[index]);
}

function storedResponse(raw: Record<string, unknown>, data: AuthoredFindAiMatchData) {
  const aiDeck = objectRecord(raw.aiDeck);
  const boardOrders = objectRecord(raw.serverBoardOrders);
  const deck = aiDeck?.cardIds;
  const playerBoardOrder = boardOrders?.owner0;
  const enemyBoardOrder = boardOrders?.owner1;
  const playerDeck = raw.playerDeckCardIds;
  if (aiDeck == null || raw.mode !== "solo" || raw.cardDataVersion !== data.contentFingerprint ||
      typeof raw.matchId !== "string" || !HEX_32.test(raw.matchId) ||
      typeof raw.seedHex !== "string" || !HEX_16.test(raw.seedHex) ||
      !Number.isInteger(raw.rulesetVersion) || !Array.isArray(playerDeck) ||
      !playerDeck.every((value) => Number.isSafeInteger(value)) ||
      !sameCards(playerDeck as number[], data.playerDeck) ||
      !Array.isArray(deck) || !Array.isArray(playerBoardOrder) || !Array.isArray(enemyBoardOrder) ||
      deck.length !== DECK_SIZE || playerBoardOrder.length !== DECK_SIZE ||
      enemyBoardOrder.length !== DECK_SIZE || !Number.isInteger(aiDeck?.cardLevel)) return null;

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
export const findAiMatch = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const data = parseData(request.data);
  const [snapshot, rankRows, deckRows] = await Promise.all([
    saveDocument(data.env, uid).get(),
    readSpecRows(data.env, "RankGrade"),
    readSpecRows(data.env, "AIDeck"),
  ]);
  if (!snapshot.exists) throw new HttpsError("failed-precondition", "Save document is missing.");

  const rank = snapshot.data()?.rank as Record<string, unknown> | undefined;
  const rawPoints = rank?.points;
  const points = Number.isSafeInteger(rawPoints) ? rawPoints as number : 0;
  const grades = parseRankGradeRows(rankRows);
  if (grades.length === 0) throw new HttpsError("failed-precondition", "RankGrade spec is empty.");

  try {
    const parsed = parseAiDeckRows(deckRows);
    if (parsed.skipped.length > 0) {
      logger.warn("AIDeck rows skipped", {env: data.env, skipped: parsed.skipped});
    }
    const tierIndex = resolveTierIndex(points, grades);
    const draw = drawAiDeck(parsed.rows, tierIndex, randomInt);
    if (draw === null) throw new Error("AIDeck spec has no usable row");
    if (data.legacy) {
      logger.info("legacy AI deck selected", {uid, env: data.env, tierIndex, deckId: draw.deckId});
      return {revision: 0, deck: draw.deck, cardLevel: draw.cardLevel};
    }
    const authoredData: AuthoredFindAiMatchData = data;
    const specPins = await readSpecPins(authoredData.env, BATTLE_REPLAY_SPEC_TABLES);
    if (fingerprintOfSpecPins(authoredData.env, specPins, ["Card"]) !== authoredData.contentFingerprint) {
      throw new HttpsError("failed-precondition", "content_fingerprint_mismatch");
    }

    // 같은 txId 재시도는 같은 문서를 읽는다. callable 응답 유실 뒤 클라가 재시도해도
    // AI 덱·시드·보드 순서가 바뀐 유령 매치를 하나 더 만들지 않는다.
    const matchId = createHash("sha256").update(`${uid}:${authoredData.txId}`, "utf8")
      .digest("hex").slice(0, 32);
    const matchRef = db.doc(`envs/${authoredData.env}/matches/${matchId}`);
    const seedHex = randomBytes(8).toString("hex");
    const playerBoardOrder = shuffle(authoredData.playerDeck);
    const enemyBoardOrder = shuffle(draw.deck);
    const now = Timestamp.now();

    const response = await withCountedTransaction("findAiMatch", async (tx) => {
      const prior = await tx.get(matchRef);
      if (prior.exists) {
        const stored = storedResponse(prior.data() ?? {}, authoredData);
        if (stored == null) throw new HttpsError("already-exists", "AI match receipt was reused");
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
        rulesetVersion: SERVER_RULESET_VERSION,
        cardDataVersion: authoredData.contentFingerprint,
        specPins,
        participantUids: [uid],
        expectedParticipants: 1,
        mode: "solo",
        resultProtocol: authoredData.resultProtocol,
        ownerIndexByUid: {[uid]: 0},
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
        pairedAt: FieldValue.serverTimestamp(),
        expiresAt: Timestamp.fromMillis(now.toMillis() + MATCH_PAIRING_TTL_MS),
        updatedAt: FieldValue.serverTimestamp(),
      });
      return {
        revision: 0,
        matchId,
        seedHex,
        rulesetVersion: SERVER_RULESET_VERSION,
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
  } catch (error) {
    if (error instanceof HttpsError) throw error;
    logger.error("AI match creation failed", {uid, env: data.env, error});
    throw new HttpsError("failed-precondition", "AI match could not be created.");
  }
});
