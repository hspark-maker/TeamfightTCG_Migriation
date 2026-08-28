import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {createHash} from "node:crypto";
import {FieldValue, Timestamp} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {
  decideMatch,
  expectedMatchId,
  sameSubmission,
  Submission,
} from "../matchResult";
import {
  computeCurrencyPayout,
  computeRankPayout,
  parseRankGradeRows,
} from "../payout";
import {parseRewardRows} from "../rewardTable";
import {
  BATTLE_COMMAND_RECORD_BYTES,
  MAX_BATTLE_COMMANDS,
  validateBattleCommands,
} from "../battleCommand";
import {HEX_16, HEX_32, HEX_64} from "../match/payloadGuards";

const SUBMISSION_DEADLINE_MS = 120_000;

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


export const submitMatchResult = onCall({enforceAppCheck: false}, async (request) => {
  const uid = request.auth?.uid;
  if (!uid) throw new HttpsError("unauthenticated", "authentication required");
  const data = parseSubmitData(request.data);
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

