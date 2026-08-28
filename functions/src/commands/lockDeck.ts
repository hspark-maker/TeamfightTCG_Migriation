import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {FieldValue, Timestamp} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {
  CardSnapshot,
  computeDeckHash,
  parseCardSpecRow,
  validateDeckShape,
  validateDeckSnapshots,
} from "../deckValidation";
import {expectedMatchId} from "../matchResult";
import {HEX_16, HEX_32, HEX_64, objectRecord, safeInteger} from "../match/payloadGuards";

const MAX_LOCK_DECK_PAYLOAD_CARDS = 64;
const LOCK_TTL_MS = 60 * 60 * 1000;

type LockDeckData = {
  env: "live" | "test";
  matchId: string;
  seedSource: "server" | "commit_reveal";
  seedHex: string | null;
  rulesetVersion: number | null;
  cardDataVersion: string;
  myNonce: string | null;
  opponentNonce: string | null;
  contentFingerprint: string;
  deckHash: string;
  cardSnapshots: CardSnapshot[];
};

function parseLockDeckData(raw: unknown): LockDeckData {
  const data = objectRecord(raw);
  if (data == null) throw new HttpsError("invalid-argument", "payload required");
  const env = data.env;
  const matchId = data.matchId;
  const seedSource = data.seedSource == null ? "commit_reveal" : data.seedSource;
  const seedHex = data.seedHex;
  const rulesetVersion = data.rulesetVersion;
  const cardDataVersion = data.cardDataVersion ?? data.contentFingerprint;
  const myNonce = data.myNonce;
  const opponentNonce = data.opponentNonce;
  const contentFingerprint = data.contentFingerprint;
  const deckHash = data.deckHash;
  if ((env !== "live" && env !== "test") || typeof matchId !== "string" ||
      (seedSource !== "server" && seedSource !== "commit_reveal") ||
      typeof contentFingerprint !== "string" || typeof deckHash !== "string" ||
      typeof cardDataVersion !== "string" ||
      !HEX_32.test(matchId) || !HEX_64.test(contentFingerprint) ||
      !HEX_64.test(cardDataVersion) || !HEX_64.test(deckHash)) {
    throw new HttpsError("invalid-argument", "invalid deck lock identity");
  }
  if (seedSource === "server") {
    if (typeof seedHex !== "string" || !HEX_16.test(seedHex) ||
        !Number.isInteger(rulesetVersion) || (rulesetVersion as number) <= 0) {
      throw new HttpsError("invalid-argument", "invalid server seed identity");
    }
  } else {
    if (typeof myNonce !== "string" || typeof opponentNonce !== "string" ||
        !HEX_16.test(myNonce) || !HEX_16.test(opponentNonce) ||
        expectedMatchId(myNonce, opponentNonce) !== matchId) {
      throw new HttpsError("invalid-argument", "matchId does not match nonces");
    }
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
  return {
    env,
    matchId,
    seedSource,
    seedHex: seedSource === "server" ? seedHex as string : null,
    rulesetVersion: seedSource === "server" ? rulesetVersion as number : null,
    cardDataVersion,
    myNonce: typeof myNonce === "string" ? myNonce : null,
    opponentNonce: typeof opponentNonce === "string" ? opponentNonce : null,
    contentFingerprint,
    deckHash,
    cardSnapshots,
  };
}


export const lockDeck = onCall({enforceAppCheck: false}, async (request) => {
  const uid = request.auth?.uid;
  if (!uid) throw new HttpsError("unauthenticated", "authentication required");
  const data = parseLockDeckData(request.data);
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
  const matchRef = db.doc(`envs/${data.env}/matches/${data.matchId}`);
  return db.runTransaction(async (tx) => {
    const lockSnapshot = await tx.get(lockRef);
    const matchSnapshot = data.seedSource === "server" ? await tx.get(matchRef) : null;
    const saveSnapshot = await tx.get(saveRef);
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
    if (data.seedSource === "server") {
      const match = matchSnapshot?.data();
      const participantUids = match?.participantUids;
      if (!Array.isArray(participantUids) || !participantUids.includes(uid) ||
          match?.seedSource !== "server" || match?.seedHex !== data.seedHex ||
          match?.rulesetVersion !== data.rulesetVersion ||
          match?.cardDataVersion !== data.cardDataVersion) {
        throw new HttpsError("permission-denied", "server match identity is not registered");
      }
    }
    if (shapeError != null) return rejectLock(shapeError);

    if (!saveSnapshot.exists) {
      throw new HttpsError("failed-precondition", "player save is not available");
    }

    const priorApproval = objectRecord(approvals[uid]);
    if (priorApproval != null) {
      if (priorApproval.deckHash === data.deckHash &&
          priorApproval.contentFingerprint === data.contentFingerprint &&
          (priorApproval.seedSource ?? "commit_reveal") === data.seedSource) {
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
    if (typeof lock?.seedSource === "string" && lock.seedSource !== data.seedSource) {
      return rejectLock("seed_source_mismatch");
    }
    if (data.seedSource === "server" &&
        ((typeof lock?.seedHex === "string" && lock.seedHex !== data.seedHex) ||
         (Number.isInteger(lock?.rulesetVersion) && lock?.rulesetVersion !== data.rulesetVersion) ||
         (typeof lock?.cardDataVersion === "string" && lock.cardDataVersion !== data.cardDataVersion))) {
      return rejectLock("server_match_metadata_mismatch");
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
        // 멱등 판정이 이 필드를 읽는다 — 빠지면 같은 uid의 재호출(폴링)이 전부 already-exists로 떨어진다.
        contentFingerprint: data.contentFingerprint,
        seedSource: data.seedSource,
        seedHex: data.seedHex,
        rulesetVersion: data.rulesetVersion,
        cardDataVersion: data.cardDataVersion,
        myNonce: data.myNonce,
        opponentNonce: data.opponentNonce,
        cardSnapshots: data.cardSnapshots,
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
      seedSource: data.seedSource,
      seedHex: data.seedHex,
      rulesetVersion: data.rulesetVersion,
      cardDataVersion: data.cardDataVersion,
      approvals: nextApprovals,
      expiresAt: Timestamp.fromMillis(now.toMillis() + LOCK_TTL_MS),
      updatedAt: FieldValue.serverTimestamp(),
    }, {merge: true});
    return {status, idempotent: false};
  });
});

