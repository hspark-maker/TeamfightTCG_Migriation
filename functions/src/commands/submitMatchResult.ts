import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {FieldValue, Timestamp} from "firebase-admin/firestore";
import {
  decideMatch,
  expectedMatchId,
  sameSubmission,
  Submission,
} from "../matchResult";
import {db} from "../firebaseApp";

// 멀티 매치 결과 수집·대조. 양측이 각자 제출하면 서버가 교차 검증해
// pending / flagged / confirmed 를 정한다. 판정 로직 자체는 ../matchResult 가 갖는다.
//
// 통합처(origin/박형석작업용) 의 functions/src/index.ts 에서 옮겨 왔다.
// 로직은 그대로고 바뀐 것은 배선 둘뿐이다 —
//   1) DATABASE_ID 를 여기서 선언하지 않고 firebaseApp 이 갖는다
//   2) 핸들러 안에서 getFirestore 를 다시 부르지 않고 firebaseApp 의 db 싱글턴을 쓴다
// 이 함수는 실서비스 트래픽을 받고 있다. 고칠 때는 반드시 배포 전에 확인할 것.

const HEX_16 = /^[0-9a-f]{16}$/;
const HEX_32 = /^[0-9a-f]{32}$/;
const HEX_64 = /^[0-9a-f]{64}$/;
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

export const submitMatchResult = onCall({enforceAppCheck: false}, async (request) => {
  const uid = request.auth?.uid;
  if (!uid) throw new HttpsError("unauthenticated", "authentication required");
  const data = parseSubmitData(request.data);
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
