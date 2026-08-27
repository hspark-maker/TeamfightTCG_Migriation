import {setGlobalOptions} from "firebase-functions";
import {HttpsError, onCall, onRequest} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {initializeApp} from "firebase-admin/app";
import {FieldValue, Timestamp, getFirestore} from "firebase-admin/firestore";
import {
  decideMatch,
  expectedMatchId,
  sameSubmission,
  Submission,
} from "./matchResult";
import {
  CardSnapshot,
  computeDeckHash,
  parseCardSpecRow,
  validateDeckShape,
  validateDeckSnapshots,
} from "./deckValidation";

setGlobalOptions({maxInstances: 10, region: "asia-northeast3"});

const app = initializeApp();

/** Firestore 데이터베이스 ID. 클라이언트 FirebaseRootPath.DatabaseId 와 같아야 한다. */
const DATABASE_ID = "cardbattle";
const HEX_16 = /^[0-9a-f]{16}$/;
const HEX_32 = /^[0-9a-f]{32}$/;
const HEX_64 = /^[0-9a-f]{64}$/;
const SUBMISSION_DEADLINE_MS = 120_000;
const MAX_LOCK_DECK_PAYLOAD_CARDS = 64;
const LOCK_TTL_MS = 60 * 60 * 1000;

type SubmitData = Omit<Submission, "uid" | "submittedAt"> & {
  env: "live" | "test";
  matchId: string;
};

function parseSubmitData(raw: unknown): SubmitData {
  if (raw == null || typeof raw !== "object") throw new HttpsError("invalid-argument", "payload required");
  const data = raw as Record<string, unknown>;
  const env = data.env;
  const matchId = data.matchId;
  const myNonce = data.myNonce;
  const opponentNonce = data.opponentNonce;
  const myDeckHash = data.myDeckHash;
  const opponentDeckHash = data.opponentDeckHash;
  const finalStateHash = data.finalStateHash;
  const stateHashChain = data.stateHashChain;
  const stateHashChainPrev = data.stateHashChainPrev;
  const stateHashChainLength = data.stateHashChainLength;
  const contentFingerprint = data.contentFingerprint;
  const won = data.won;
  const myRemaining = data.myRemaining;
  const opponentRemaining = data.opponentRemaining;
  if ((env !== "live" && env !== "test") || typeof matchId !== "string" || !HEX_32.test(matchId) ||
      typeof myNonce !== "string" || !HEX_16.test(myNonce) ||
      typeof opponentNonce !== "string" || !HEX_16.test(opponentNonce) ||
      typeof myDeckHash !== "string" || !HEX_64.test(myDeckHash) ||
      typeof opponentDeckHash !== "string" || !HEX_64.test(opponentDeckHash) ||
      typeof finalStateHash !== "string" || !HEX_16.test(finalStateHash) ||
      typeof stateHashChain !== "string" || !HEX_16.test(stateHashChain) ||
      typeof stateHashChainPrev !== "string" || !HEX_16.test(stateHashChainPrev) ||
      !Number.isInteger(stateHashChainLength) || (stateHashChainLength as number) < 0 ||
      (stateHashChainLength as number) > 10000 ||
      typeof contentFingerprint !== "string" || !HEX_64.test(contentFingerprint) ||
      typeof won !== "boolean" || !Number.isInteger(myRemaining) || !Number.isInteger(opponentRemaining) ||
      (myRemaining as number) < 0 || (myRemaining as number) > 12 ||
      (opponentRemaining as number) < 0 || (opponentRemaining as number) > 12) {
    throw new HttpsError("invalid-argument", "invalid match result payload");
  }
  if (expectedMatchId(myNonce, opponentNonce) !== matchId) {
    throw new HttpsError("invalid-argument", "matchId does not match nonces");
  }
  return {env, matchId, myNonce, opponentNonce, myDeckHash, opponentDeckHash,
    finalStateHash, stateHashChain, stateHashChainPrev,
    stateHashChainLength: stateHashChainLength as number, contentFingerprint, won,
    myRemaining: myRemaining as number, opponentRemaining: opponentRemaining as number};
}

export const ping = onRequest(async (request, response) => {
  logger.info("ping called");
  const db = getFirestore(app, DATABASE_ID);
  let dbOk = false;
  let dbError: string | null = null;
  try {
    await db.collection("_health").doc("ping").get();
    dbOk = true;
  } catch (e) {
    dbError = e instanceof Error ? e.message : String(e);
  }
  response.json({ok: true, database: DATABASE_ID, dbOk, dbError});
});

type LockDeckData = {
  env: "live" | "test";
  matchId: string;
  myNonce: string;
  opponentNonce: string;
  contentFingerprint: string;
  deckHash: string;
  cardSnapshots: CardSnapshot[];
};

function objectRecord(value: unknown): Record<string, unknown> | null {
  if (value == null || typeof value !== "object" || Array.isArray(value)) return null;
  return value as Record<string, unknown>;
}

function safeInteger(value: unknown): number | null {
  return typeof value === "number" && Number.isSafeInteger(value) ? value : null;
}

function parseLockDeckData(raw: unknown): LockDeckData {
  const data = objectRecord(raw);
  if (data == null) throw new HttpsError("invalid-argument", "payload required");
  const env = data.env;
  const matchId = data.matchId;
  const myNonce = data.myNonce;
  const opponentNonce = data.opponentNonce;
  const contentFingerprint = data.contentFingerprint;
  const deckHash = data.deckHash;
  if ((env !== "live" && env !== "test") || typeof matchId !== "string" ||
      typeof myNonce !== "string" || typeof opponentNonce !== "string" ||
      typeof contentFingerprint !== "string" || typeof deckHash !== "string" ||
      !HEX_32.test(matchId) || !HEX_16.test(myNonce) || !HEX_16.test(opponentNonce) ||
      !HEX_64.test(contentFingerprint) || !HEX_64.test(deckHash)) {
    throw new HttpsError("invalid-argument", "invalid deck lock identity");
  }
  if (expectedMatchId(myNonce, opponentNonce) !== matchId) {
    throw new HttpsError("invalid-argument", "matchId does not match nonces");
  }
  if (!Array.isArray(data.cardSnapshots) || data.cardSnapshots.length === 0 ||
      data.cardSnapshots.length > MAX_LOCK_DECK_PAYLOAD_CARDS) {
    throw new HttpsError("invalid-argument", "card snapshots required");
  }

  const cardSnapshots = data.cardSnapshots.map((rawCard): CardSnapshot => {
    const card = objectRecord(rawCard);
    if (card == null) throw new HttpsError("invalid-argument", "invalid card snapshot");
    const cardId = safeInteger(card.cardId);
    const level = safeInteger(card.level);
    const hpBonus = safeInteger(card.hpBonus);
    const evolutionStage = safeInteger(card.evolutionStage);
    const unlockedKeywords = safeInteger(card.unlockedKeywords);
    if (cardId == null || cardId <= 0 || level == null || hpBonus == null ||
        evolutionStage == null || unlockedKeywords == null ||
        typeof card.synergyUnlocked !== "boolean") {
      throw new HttpsError("invalid-argument", "invalid card snapshot fields");
    }
    return {
      cardId,
      level,
      hpBonus,
      evolutionStage,
      unlockedKeywords,
      synergyUnlocked: card.synergyUnlocked,
    };
  });
  return {env, matchId, myNonce, opponentNonce, contentFingerprint, deckHash, cardSnapshots};
}

export const lockDeck = onCall({enforceAppCheck: false}, async (request) => {
  const uid = request.auth?.uid;
  if (!uid) throw new HttpsError("unauthenticated", "authentication required");
  const data = parseLockDeckData(request.data);
  const db = getFirestore(app, DATABASE_ID);
  const table = data.env === "test" ? "Card_Test" : "Card";
  const rowRoot = `envs/${data.env}/specs/${table}/rows`;
  const shapeError = validateDeckShape(data.cardSnapshots);
  const cardIds = [...new Set(data.cardSnapshots.map((card) => card.cardId))];
  const specSnapshots = shapeError == null ? await (async () => {
    try {
      const meta = await db.doc(`envs/${data.env}/specs/${table}`).get();
      if (!meta.exists) throw new HttpsError("unavailable", "card spec table is unavailable");
      return await db.getAll(...cardIds.map((id) => db.doc(`${rowRoot}/${id}`)));
    } catch (error) {
      if (error instanceof HttpsError) throw error;
      logger.error("lockDeck spec read failed", {env: data.env, table, error});
      throw new HttpsError("unavailable", "card spec read failed");
    }
  })() : [];
  const specs = new Map();
  for (const snapshot of specSnapshots) {
    if (!snapshot.exists) continue;
    const spec = parseCardSpecRow(snapshot.data());
    if (spec == null) {
      logger.error("lockDeck spec row is invalid", {path: snapshot.ref.path});
      throw new HttpsError("unavailable", "card spec row is invalid");
    }
    specs.set(spec.id, spec);
  }

  const lockRef = db.doc(`envs/${data.env}/matchLocks/${data.matchId}`);
  const saveRef = db.doc(`envs/${data.env}/users/${uid}/save/current`);
  return db.runTransaction(async (tx) => {
    const lockSnapshot = await tx.get(lockRef);
    const lock = lockSnapshot.data();
    if (lock?.status === "rejected") {
      return {status: "rejected", reason: "match_rejected"};
    }
    const approvals = objectRecord(lock?.approvals) ?? {};
    const rejectLock = (reason: string, cardId?: number) => {
      const now = Timestamp.now();
      tx.set(lockRef, {
        matchId: data.matchId,
        env: data.env,
        status: "rejected",
        reason,
        rejectedBy: uid,
        rejectedAt: now,
        expiresAt: Timestamp.fromMillis(now.toMillis() + LOCK_TTL_MS),
        updatedAt: FieldValue.serverTimestamp(),
      }, {merge: true});
      return cardId == null ?
        {status: "rejected", reason} :
        {status: "rejected", reason, cardId};
    };
    if (shapeError != null) return rejectLock(shapeError);

    const saveSnapshot = await tx.get(saveRef);
    if (!saveSnapshot.exists) {
      throw new HttpsError("failed-precondition", "player save is not available");
    }

    const priorApproval = objectRecord(approvals[uid]);
    if (priorApproval != null) {
      if (priorApproval.deckHash === data.deckHash &&
          priorApproval.contentFingerprint === data.contentFingerprint) {
        const status = Object.keys(approvals).length >= 2 ? "approved" : "pending";
        return {status, idempotent: true};
      }
      throw new HttpsError("already-exists", "a different deck is already locked");
    }
    if (Object.keys(approvals).length >= 2) {
      throw new HttpsError("permission-denied", "match already has two participants");
    }
    if (typeof lock?.contentFingerprint === "string" &&
        lock.contentFingerprint !== data.contentFingerprint) {
      return rejectLock("content_fingerprint_mismatch");
    }
    if (computeDeckHash(data.cardSnapshots) !== data.deckHash) {
      return rejectLock("deck_hash_mismatch");
    }

    const validation = validateDeckSnapshots(data.cardSnapshots, specs, saveSnapshot.data());
    if (!validation.ok) {
      logger.warn("lockDeck rejected", {
        uid,
        matchId: data.matchId,
        reason: validation.code,
        cardId: validation.cardId,
      });
      return rejectLock(validation.code, validation.cardId);
    }

    const now = Timestamp.now();
    const revision = safeInteger(saveSnapshot.get("revision")) ?? 0;
    const nextApprovals = {
      ...approvals,
      [uid]: {
        deckHash: data.deckHash,
        myNonce: data.myNonce,
        opponentNonce: data.opponentNonce,
        saveRevision: revision,
        approvedAt: now,
      },
    };
    const status = Object.keys(nextApprovals).length >= 2 ? "approved" : "pending";
    tx.set(lockRef, {
      matchId: data.matchId,
      env: data.env,
      status,
      contentFingerprint: data.contentFingerprint,
      approvals: nextApprovals,
      expiresAt: Timestamp.fromMillis(now.toMillis() + LOCK_TTL_MS),
      updatedAt: FieldValue.serverTimestamp(),
    }, {merge: true});
    return {status, idempotent: false};
  });
});

export const submitMatchResult = onCall({enforceAppCheck: false}, async (request) => {
  const uid = request.auth?.uid;
  if (!uid) throw new HttpsError("unauthenticated", "authentication required");
  const data = parseSubmitData(request.data);
  const db = getFirestore(app, DATABASE_ID);
  const matchRef = db.doc(`envs/${data.env}/matches/${data.matchId}`);

  return db.runTransaction(async (tx) => {
    const matchSnapshot = await tx.get(matchRef);
    const match = matchSnapshot.data() as Record<string, unknown> | undefined;
    const status = typeof match?.status === "string" ? match.status : "pending";
    if (status !== "pending") {
      return {status, reason: match?.reason ?? null};
    }

    const submissions = {...(match?.submissions as Record<string, Submission> | undefined)};
    const incoming: Submission = {...data, uid, submittedAt: Timestamp.now()};
    delete (incoming as Partial<SubmitData>).env;
    delete (incoming as Partial<SubmitData>).matchId;
    const prior = submissions[uid];
    if (prior && !sameSubmission(prior, incoming)) {
      throw new HttpsError("already-exists", "submission cannot be changed");
    }
    submissions[uid] = prior ?? incoming;
    const entries = Object.values(submissions);
    const createdAt = match?.createdAt instanceof Timestamp ? match.createdAt : Timestamp.now();
    const expiresAt = Timestamp.fromMillis(createdAt.toMillis() + 7 * 24 * 60 * 60 * 1000);

    const decision = decideMatch(entries, createdAt.toMillis(), Timestamp.now().toMillis(), SUBMISSION_DEADLINE_MS);
    if (decision.status === "pending") {
      tx.set(matchRef, {status: "pending", submissions, createdAt, expiresAt,
        deadlineAt: Timestamp.fromMillis(createdAt.toMillis() + SUBMISSION_DEADLINE_MS)}, {merge: true});
      return {status: "pending"};
    }
    if (decision.status === "flagged") {
      tx.set(matchRef, {status: "flagged", reason: decision.reason, submissions,
        settledAt: FieldValue.serverTimestamp(), expiresAt}, {merge: true});
      return {status: "flagged", reason: decision.reason};
    }

    // 수집 단계다 — 서버는 두 제출이 서로 맞는지만 기록한다.
    // 랭크·보상은 클라이언트가 로컬에서 확정하며, 여기서 세이브를 읽지도 쓰지도 않는다.
    logger.info("match_settled", {
      matchId: data.matchId, env: data.env, status: "confirmed",
      uids: entries.map((entry) => entry.uid),
    });
    tx.set(matchRef, {status: "confirmed", submissions,
      settledAt: FieldValue.serverTimestamp(), expiresAt}, {merge: true});
    return {status: "confirmed"};
  });
});
