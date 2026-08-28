import {HttpsError, onCall} from "firebase-functions/v2/https";
import {FieldValue, Timestamp} from "firebase-admin/firestore";
import {randomBytes} from "node:crypto";
import {db} from "../firebaseApp";
import {
  joinPairing,
  MatchIdentity,
  matchIdFromPairingKey,
  PairingRecord,
  pairingDocumentId,
} from "../matchPairing";
import {HEX_16, HEX_32, HEX_64, objectRecord, safeInteger} from "../match/payloadGuards";

const PAIRING_KEY = /^[A-Za-z0-9_-]{1,128}$/;

type CreateMatchData = {
  env: "live" | "test";
  pairingKey: string;
  contentFingerprint: string;
  ownerIndex: number;
};

function parseCreateMatchData(raw: unknown): CreateMatchData {
  const data = objectRecord(raw);
  if (data == null) throw new HttpsError("invalid-argument", "payload required");
  const ownerIndex = safeInteger(data.ownerIndex);
  if ((data.env !== "live" && data.env !== "test") ||
      typeof data.pairingKey !== "string" || !PAIRING_KEY.test(data.pairingKey) ||
      typeof data.contentFingerprint !== "string" || !HEX_64.test(data.contentFingerprint)) {
    throw new HttpsError("invalid-argument", "invalid match pairing payload");
  }
  if (ownerIndex !== 0 && ownerIndex !== 1) {
    throw new HttpsError("invalid-argument", "invalid owner index");
  }
  return {
    env: data.env,
    pairingKey: data.pairingKey,
    contentFingerprint: data.contentFingerprint,
    ownerIndex,
  };
}

function readPairingRecord(raw: Record<string, unknown> | undefined): PairingRecord | null {
  if (raw == null || typeof raw.matchId !== "string" || !HEX_32.test(raw.matchId) ||
      typeof raw.seedHex !== "string" || !HEX_16.test(raw.seedHex) ||
      // 매치 문서는 이 값을 cardDataVersion 으로 들고 있다 — contentFingerprint 라는 이름은
      // 클라 페이로드 쪽 이름이다. 여기서 이름을 잘못 읽으면 레코드가 항상 null 이 되어
      // 매 호출이 페어링을 초기화하고 두 클라가 영원히 만나지 못한다.
      typeof raw.cardDataVersion !== "string" || !HEX_64.test(raw.cardDataVersion) ||
      !Number.isInteger(raw.rulesetVersion) ||
      !Array.isArray(raw.participantUids) ||
      !raw.participantUids.every((uid) => typeof uid === "string") ||
      !(raw.pairingCreatedAt instanceof Timestamp) ||
      !(raw.expiresAt instanceof Timestamp)) return null;
  return {
    matchId: raw.matchId,
    seedHex: raw.seedHex,
    contentFingerprint: raw.cardDataVersion,
    rulesetVersion: raw.rulesetVersion as number,
    participantUids: raw.participantUids as string[],
    createdAtMs: raw.pairingCreatedAt.toMillis(),
    expiresAtMs: raw.expiresAt.toMillis(),
  };
}


export const createMatch = onCall({enforceAppCheck: false}, async (request) => {
  const uid = request.auth?.uid;
  if (!uid) throw new HttpsError("unauthenticated", "authentication required");
  const data = parseCreateMatchData(request.data);
  const pairingId = pairingDocumentId(data.pairingKey);
  // 매치 문서 하나가 페어링 레코드까지 겸한다 — id 를 pairingKey 에서 파생해야 두 클라가
  // 같은 문서를 집는다. 시드는 이 값과 무관한 별도 난수라 예측 가능성이 옮겨가지 않는다.
  const matchRef = db.doc(`envs/${data.env}/matches/${matchIdFromPairingKey(data.pairingKey)}`);
  const candidate: MatchIdentity = {
    matchId: matchIdFromPairingKey(data.pairingKey),
    seedHex: randomBytes(8).toString("hex"),
  };

  return db.runTransaction(async (tx) => {
    const matchSnapshot = await tx.get(matchRef);
    const raw = matchSnapshot.data();
    const priorOwners = objectRecord(raw?.ownerIndexByUid) ?? {};
    const priorOwner = safeInteger(priorOwners[uid]);
    if (priorOwner != null && priorOwner !== data.ownerIndex) {
      throw new HttpsError("already-exists", "owner index cannot be changed");
    }
    for (const [otherUid, rawOwner] of Object.entries(priorOwners)) {
      if (otherUid !== uid && safeInteger(rawOwner) === data.ownerIndex) {
        throw new HttpsError("failed-precondition", "owner index conflict");
      }
    }
    const ownerIndexByUid = {...priorOwners, [uid]: data.ownerIndex};
    // 같은 pairingKey 가 다시 온 경우. 덱 잠금이나 결과 정산이 이미 시작된 문서를 페어링 단계로
    // 되돌리면 진행 중인 매치를 덮어쓴다 — 클라가 nonce 를 새로 뽑아 다시 오게 한다.
    if (raw != null && (raw.phase === "locked" || raw.phase === "settled" ||
        raw.status === "confirmed" || raw.status === "flagged")) {
      throw new HttpsError("already-exists", "pairing_key_reused");
    }
    const priorRecord = readPairingRecord(raw);
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
      slot: data.ownerIndex,
      status: decision.status,
    };
    const unchanged = priorRecord != null &&
      priorRecord.matchId === record.matchId &&
      priorRecord.participantUids.length === record.participantUids.length &&
      priorRecord.participantUids.every((participant, index) =>
        participant === record.participantUids[index]);
    if (unchanged && priorOwner === data.ownerIndex) return response;

    tx.set(matchRef, {
      matchId: record.matchId,
      env: data.env,
      phase: "pairing",
      status: "pending",
      pairingStatus: decision.status,
      seedSource: "server",
      seedHex: record.seedHex,
      rulesetVersion: record.rulesetVersion,
      cardDataVersion: record.contentFingerprint,
      participantUids: record.participantUids,
      ownerIndexByUid,
      pairingKeyHash: pairingId,
      // 페어링 시각. 결과 제출 마감(createdAt + 120초)의 기준인 createdAt 과 섞으면
      // 전투 시작 전에 마감이 흘러가므로 별도 필드로 둔다.
      pairingCreatedAt: Timestamp.fromMillis(record.createdAtMs),
      pairedAt: decision.status === "paired" ? FieldValue.serverTimestamp() : null,
      expiresAt: Timestamp.fromMillis(record.expiresAtMs),
      updatedAt: FieldValue.serverTimestamp(),
    }, {merge: true});
    return response;
  });
});
