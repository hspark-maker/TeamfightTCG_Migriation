import {setGlobalOptions} from "firebase-functions";
import {HttpsError, onCall, onRequest} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {createHash, randomBytes} from "node:crypto";
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
import {
  joinPairing,
  MatchIdentity,
  PairingRecord,
  pairingDocumentId,
} from "./matchPairing";
import {
  computeCurrencyPayout,
  computeRankPayout,
  parseRankGradeRows,
  parseRewardRows,
} from "./payout";
import {
  BATTLE_COMMAND_RECORD_BYTES,
  MAX_BATTLE_COMMANDS,
  validateBattleCommands,
} from "./battleCommand";

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
const PAIRING_KEY = /^[A-Za-z0-9_-]{1,128}$/;

type SubmitData = Omit<Submission, "uid" | "submittedAt"> & {
  env: "live" | "test";
  matchId: string;
};

function parseSubmitData(raw: unknown): SubmitData {
  if (raw == null || typeof raw !== "object") throw new HttpsError("invalid-argument", "payload required");
  const data = raw as Record<string, unknown>;
  const env = data.env;
  const matchId = data.matchId;
  const seedSource = data.seedSource == null ? "commit_reveal" : data.seedSource;
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
  const rankPointsBefore = data.rankPointsBefore;
  const commandLogVersion = data.commandLogVersion == null ? 0 : data.commandLogVersion;
  const commandLog = data.commandLog == null ? "" : data.commandLog;
  const commandLogHash = data.commandLogHash == null ? "" : data.commandLogHash;
  const commandCount = data.commandCount == null ? 0 : data.commandCount;
  const commandLogTruncated = data.commandLogTruncated == null ? false : data.commandLogTruncated;
  if ((env !== "live" && env !== "test") || typeof matchId !== "string" || !HEX_32.test(matchId) ||
      (seedSource !== "server" && seedSource !== "commit_reveal") ||
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
      (opponentRemaining as number) < 0 || (opponentRemaining as number) > 12 ||
      !Number.isSafeInteger(rankPointsBefore) || (rankPointsBefore as number) < 0) {
    throw new HttpsError("invalid-argument", "invalid match result payload");
  }
  if (!Number.isInteger(commandLogVersion) || (commandLogVersion !== 0 && commandLogVersion !== 1) ||
      typeof commandLog !== "string" || typeof commandLogHash !== "string" ||
      !Number.isInteger(commandCount) || (commandCount as number) < 0 ||
      (commandCount as number) > MAX_BATTLE_COMMANDS ||
      typeof commandLogTruncated !== "boolean") {
    throw new HttpsError("invalid-argument", "invalid command log metadata");
  }
  if (commandLogVersion === 1) {
    const rawLog = Buffer.from(commandLog, "base64");
    const canonicalBase64 = rawLog.toString("base64");
    const expectedHash = createHash("sha256").update(rawLog).digest("hex");
    const truncatedShape = commandLogTruncated && commandCount === MAX_BATTLE_COMMANDS && commandLog === "";
    const completeShape = !commandLogTruncated && canonicalBase64 === commandLog &&
      rawLog.length === (commandCount as number) * BATTLE_COMMAND_RECORD_BYTES;
    const commandError = completeShape ? validateBattleCommands(rawLog, commandCount as number) : null;
    if (!HEX_64.test(commandLogHash) || commandLogHash !== expectedHash ||
        (!truncatedShape && !completeShape) || commandError != null) {
      throw new HttpsError("invalid-argument", "invalid command log payload");
    }
  } else if (commandLog !== "" || commandLogHash !== "" || commandCount !== 0 || commandLogTruncated) {
    throw new HttpsError("invalid-argument", "legacy command log fields must be empty");
  }
  if (seedSource === "commit_reveal" &&
      (typeof myNonce !== "string" || !HEX_16.test(myNonce) ||
       typeof opponentNonce !== "string" || !HEX_16.test(opponentNonce) ||
       expectedMatchId(myNonce, opponentNonce) !== matchId)) {
    throw new HttpsError("invalid-argument", "matchId does not match nonces");
  }
  return {env, matchId, seedSource,
    myNonce: seedSource === "server" ? "" : myNonce as string,
    opponentNonce: seedSource === "server" ? "" : opponentNonce as string,
    myDeckHash, opponentDeckHash,
    finalStateHash, stateHashChain, stateHashChainPrev,
    stateHashChainLength: stateHashChainLength as number, contentFingerprint, won,
    myRemaining: myRemaining as number, opponentRemaining: opponentRemaining as number,
    rankPointsBefore: rankPointsBefore as number,
    commandLogVersion: commandLogVersion as number,
    commandLog, commandLogHash, commandCount: commandCount as number, commandLogTruncated};
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

type CreateMatchData = {
  env: "live" | "test";
  pairingKey: string;
  contentFingerprint: string;
};

function parseCreateMatchData(raw: unknown): CreateMatchData {
  const data = objectRecord(raw);
  if (data == null) throw new HttpsError("invalid-argument", "payload required");
  if ((data.env !== "live" && data.env !== "test") ||
      typeof data.pairingKey !== "string" || !PAIRING_KEY.test(data.pairingKey) ||
      typeof data.contentFingerprint !== "string" || !HEX_64.test(data.contentFingerprint)) {
    throw new HttpsError("invalid-argument", "invalid match pairing payload");
  }
  return {
    env: data.env,
    pairingKey: data.pairingKey,
    contentFingerprint: data.contentFingerprint,
  };
}

function readPairingRecord(raw: Record<string, unknown> | undefined): PairingRecord | null {
  if (raw == null || typeof raw.matchId !== "string" || !HEX_32.test(raw.matchId) ||
      typeof raw.seedHex !== "string" || !HEX_16.test(raw.seedHex) ||
      typeof raw.contentFingerprint !== "string" || !HEX_64.test(raw.contentFingerprint) ||
      !Number.isInteger(raw.rulesetVersion) ||
      !Array.isArray(raw.participantUids) ||
      !raw.participantUids.every((uid) => typeof uid === "string") ||
      !(raw.createdAt instanceof Timestamp) || !(raw.expiresAt instanceof Timestamp)) return null;
  return {
    matchId: raw.matchId,
    seedHex: raw.seedHex,
    contentFingerprint: raw.contentFingerprint,
    rulesetVersion: raw.rulesetVersion as number,
    participantUids: raw.participantUids as string[],
    createdAtMs: raw.createdAt.toMillis(),
    expiresAtMs: raw.expiresAt.toMillis(),
  };
}

export const createMatch = onCall({enforceAppCheck: false}, async (request) => {
  const uid = request.auth?.uid;
  if (!uid) throw new HttpsError("unauthenticated", "authentication required");
  const data = parseCreateMatchData(request.data);
  const db = getFirestore(app, DATABASE_ID);
  const pairingId = pairingDocumentId(data.pairingKey);
  const pairingRef = db.doc(`envs/${data.env}/matchPairings/${pairingId}`);
  const candidate: MatchIdentity = {
    matchId: randomBytes(16).toString("hex"),
    seedHex: randomBytes(8).toString("hex"),
  };

  return db.runTransaction(async (tx) => {
    const pairingSnapshot = await tx.get(pairingRef);
    const priorRecord = readPairingRecord(pairingSnapshot.data());
    let decision;
    try {
      decision = joinPairing(
        priorRecord,
        uid,
        data.contentFingerprint,
        Date.now(),
        candidate
      );
    } catch (error) {
      const reason = error instanceof Error ? error.message : String(error);
      if (reason === "content_fingerprint_mismatch") {
        throw new HttpsError("failed-precondition", reason);
      }
      if (reason === "match_pairing_full") throw new HttpsError("permission-denied", reason);
      throw error;
    }

    const record = decision.record;
    const response = {
      matchId: record.matchId,
      seedHex: decision.status === "paired" ? record.seedHex : null,
      rulesetVersion: record.rulesetVersion,
      slot: decision.slot,
      status: decision.status,
    };
    const unchanged = priorRecord != null &&
      priorRecord.matchId === record.matchId &&
      priorRecord.participantUids.length === record.participantUids.length &&
      priorRecord.participantUids.every((participant, index) =>
        participant === record.participantUids[index]);
    if (unchanged) return response;

    const createdAt = Timestamp.fromMillis(record.createdAtMs);
    const expiresAt = Timestamp.fromMillis(record.expiresAtMs);
    tx.set(pairingRef, {
      pairingKeyHash: pairingId,
      matchId: record.matchId,
      seedHex: record.seedHex,
      contentFingerprint: record.contentFingerprint,
      rulesetVersion: record.rulesetVersion,
      participantUids: record.participantUids,
      status: decision.status,
      createdAt,
      pairedAt: decision.status === "paired" ? FieldValue.serverTimestamp() : null,
      expiresAt,
      updatedAt: FieldValue.serverTimestamp(),
    });
    tx.set(db.doc(`envs/${data.env}/matches/${record.matchId}`), {
      matchId: record.matchId,
      env: data.env,
      status: "pending",
      seedSource: "server",
      seedHex: record.seedHex,
      rulesetVersion: record.rulesetVersion,
      cardDataVersion: record.contentFingerprint,
      participantUids: record.participantUids,
      pairingKeyHash: pairingId,
      pairedAt: decision.status === "paired" ? FieldValue.serverTimestamp() : null,
      expiresAt,
      updatedAt: FieldValue.serverTimestamp(),
    }, {merge: true});
    return response;
  });
});

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

export const submitMatchResult = onCall({enforceAppCheck: false}, async (request) => {
  const uid = request.auth?.uid;
  if (!uid) throw new HttpsError("unauthenticated", "authentication required");
  const data = parseSubmitData(request.data);
  const db = getFirestore(app, DATABASE_ID);
  const matchRef = db.doc(`envs/${data.env}/matches/${data.matchId}`);
  const [rewardSnapshot, rankSnapshot] = await Promise.all([
    db.collection(`envs/${data.env}/specs/Reward/rows`).get(),
    db.collection(`envs/${data.env}/specs/RankGrade/rows`).get(),
  ]);
  let rewardRows;
  let rankRows;
  try {
    rewardRows = parseRewardRows(rewardSnapshot.docs.map((doc) => doc.data()));
    rankRows = parseRankGradeRows(rankSnapshot.docs.map((doc) => doc.data()));
  } catch (error) {
    logger.error("payout_spec_invalid", {env: data.env, error});
    throw new HttpsError("failed-precondition", "payout specs are unavailable");
  }

  return db.runTransaction(async (tx) => {
    const matchSnapshot = await tx.get(matchRef);
    const match = matchSnapshot.data() as Record<string, unknown> | undefined;
    if (data.seedSource === "server") {
      const participantUids = match?.participantUids;
      if (match?.seedSource !== "server" ||
          !Array.isArray(participantUids) || !participantUids.includes(uid)) {
        throw new HttpsError("permission-denied", "server match identity is not registered");
      }
    }
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

    const rankStateRefs = entries.map((entry) =>
      db.doc(`envs/${data.env}/users/${entry.uid}/payoutState/current`));
    const saveRefs = entries.map((entry) =>
      db.doc(`envs/${data.env}/users/${entry.uid}/save/current`));
    const rankStateSnapshots = [];
    for (const ref of rankStateRefs) rankStateSnapshots.push(await tx.get(ref));
    const saveSnapshots = [];
    for (const ref of saveRefs) saveSnapshots.push(await tx.get(ref));
    const settledAt = Timestamp.now();
    const payoutExpiresAt = Timestamp.fromMillis(settledAt.toMillis() + 180 * 24 * 60 * 60 * 1000);
    const payoutSummary: Record<string, unknown> = {};
    for (let i = 0; i < entries.length; i++) {
      const entry = entries[i];
      const storedPoints = rankStateSnapshots[i].data()?.currentPoints;
      const storedSequence = rankStateSnapshots[i].data()?.sequence;
      const saveRank = saveSnapshots[i].data()?.rank as Record<string, unknown> | undefined;
      const savePoints = saveRank?.points;
      const rankBefore = Number.isSafeInteger(storedPoints) ? storedPoints as number : savePoints;
      const rankSequence = Number.isSafeInteger(storedSequence) ? (storedSequence as number) + 1 : 1;
      if (!Number.isSafeInteger(rankBefore) || (rankBefore as number) < 0) {
        throw new HttpsError("failed-precondition", "rank baseline is unavailable");
      }
      if (!rankStateSnapshots[i].exists && entry.rankPointsBefore !== rankBefore) {
        throw new HttpsError("failed-precondition", "rank baseline does not match server save");
      }
      let currency;
      let rank;
      try {
        currency = computeCurrencyPayout(entry.won, entry.myRemaining, rewardRows);
        rank = computeRankPayout(rankBefore as number, entry.won, rankRows);
      } catch (error) {
        logger.error("payout_calculation_failed", {matchId: data.matchId, uid: entry.uid, error});
        throw new HttpsError("failed-precondition", "payout calculation failed");
      }
      const payout = {
        status: "ready",
        env: data.env,
        matchId: data.matchId,
        uid: entry.uid,
        won: entry.won,
        currency,
        rank,
        rankSequence,
        settledAt,
        expiresAt: payoutExpiresAt,
      };
      tx.set(db.doc(`envs/${data.env}/users/${entry.uid}/payouts/${data.matchId}`), payout);
      tx.set(rankStateRefs[i], {
        currentPoints: rank.after,
        sequence: rankSequence,
        lastMatchId: data.matchId,
        updatedAt: settledAt,
      }, {merge: true});
      payoutSummary[entry.uid] = {currency, rank, won: entry.won};
    }

    // 수집 단계다 — 서버는 두 제출이 서로 맞는지만 기록한다.
    // 랭크·보상은 클라이언트가 로컬에서 확정하며, 여기서 세이브를 읽지도 쓰지도 않는다.
    logger.info("match_settled", {
      matchId: data.matchId, env: data.env, status: "confirmed",
      uids: entries.map((entry) => entry.uid),
    });
    tx.set(matchRef, {status: "confirmed", submissions, payouts: payoutSummary,
      settledAt: FieldValue.serverTimestamp(), expiresAt}, {merge: true});
    return {status: "confirmed"};
  });
});

type ClaimPayoutData = {env: "live" | "test"; action: "list" | "ack"; matchIds: string[]};

function parseClaimPayoutData(raw: unknown): ClaimPayoutData {
  if (raw == null || typeof raw !== "object") throw new HttpsError("invalid-argument", "payload required");
  const data = raw as Record<string, unknown>;
  const env = data.env;
  const action = data.action == null ? "list" : data.action;
  const rawIds = data.matchIds == null ? [] : data.matchIds;
  if ((env !== "live" && env !== "test") || (action !== "list" && action !== "ack") ||
      !Array.isArray(rawIds) || rawIds.length > 20 || rawIds.some((id) => typeof id !== "string" || !HEX_32.test(id))) {
    throw new HttpsError("invalid-argument", "invalid payout claim payload");
  }
  return {env, action, matchIds: [...new Set(rawIds as string[])]};
}

export const claimPayout = onCall({enforceAppCheck: false}, async (request) => {
  const uid = request.auth?.uid;
  if (!uid) throw new HttpsError("unauthenticated", "authentication required");
  const data = parseClaimPayoutData(request.data);
  const db = getFirestore(app, DATABASE_ID);
  const collection = db.collection(`envs/${data.env}/users/${uid}/payouts`);
  if (data.action === "list") {
    const snapshot = await collection.where("status", "==", "ready").limit(20).get();
    const payouts = snapshot.docs.map((doc) => doc.data()).sort((a, b) => {
      const left = a.settledAt instanceof Timestamp ? a.settledAt.toMillis() : 0;
      const right = b.settledAt instanceof Timestamp ? b.settledAt.toMillis() : 0;
      return left - right;
    }).map((payout) => {
      const settledAtMs = payout.settledAt instanceof Timestamp ? payout.settledAt.toMillis() : 0;
      const result = {...payout};
      delete result.settledAt;
      delete result.expiresAt;
      return {...result, settledAtMs};
    });
    return {payouts};
  }
  if (data.matchIds.length === 0) return {acked: []};
  const acked = await db.runTransaction(async (tx) => {
    const refs = data.matchIds.map((matchId) => collection.doc(matchId));
    const snapshots = [];
    for (const ref of refs) snapshots.push(await tx.get(ref));
    const accepted: string[] = [];
    for (let i = 0; i < refs.length; i++) {
      const payout = snapshots[i].data();
      if (payout?.uid !== uid || payout?.matchId !== data.matchIds[i] || payout?.status !== "ready") continue;
      tx.set(refs[i], {status: "claimed", claimedAt: FieldValue.serverTimestamp()}, {merge: true});
      accepted.push(data.matchIds[i]);
    }
    return accepted;
  });
  return {acked};
});
