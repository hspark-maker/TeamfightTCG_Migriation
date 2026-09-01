import {HttpsError, onCall} from "firebase-functions/v2/https";
import {logger} from "firebase-functions/v2";
import {FieldValue, Timestamp} from "firebase-admin/firestore";
import {randomBytes} from "node:crypto";
import {db} from "../firebaseApp";
import {withCountedTransaction} from "../observability/countedTransaction";
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
  /** "solo" = AI 대전(정원 1). 생략하면 대인전이다 — 구 클라의 페이로드가 그대로 대인전으로 읽힌다. */
  mode: "solo" | "pvp";
};

/**
 * 매치 정원. 검증 규칙(lockDeck)을 두 벌로 만들지 않으려고 AI 대전도 같은 매치 문서를 쓰고,
 * 다른 것은 "몇 명을 모아야 성립하는가" 하나뿐이다.
 * @param {"solo" | "pvp"} mode 매치 종류
 * @return {number} 정원
 */
export function expectedParticipantsOf(mode: "solo" | "pvp"): number {
  return mode === "solo" ? 1 : 2;
}

function parseCreateMatchData(raw: unknown): CreateMatchData {
  const data = objectRecord(raw);
  if (data == null) throw new HttpsError("invalid-argument", "payload required");
  const ownerIndex = safeInteger(data.ownerIndex);
  const mode = data.mode == null ? "pvp" : data.mode;
  if ((data.env !== "live" && data.env !== "test") ||
      typeof data.pairingKey !== "string" || !PAIRING_KEY.test(data.pairingKey) ||
      typeof data.contentFingerprint !== "string" || !HEX_64.test(data.contentFingerprint) ||
      (mode !== "solo" && mode !== "pvp")) {
    throw new HttpsError("invalid-argument", "invalid match pairing payload");
  }
  if (ownerIndex !== 0 && ownerIndex !== 1) {
    throw new HttpsError("invalid-argument", "invalid owner index");
  }
  // AI 대전에 상대 슬롯은 없다. 1을 받아 주면 ownerIndexByUid 가 비어 있는 0번 자리를 남긴 채
  // lockDeck 의 owner 대조를 통과해, 어느 쪽이 플레이어인지 문서만 봐서는 알 수 없게 된다.
  if (mode === "solo" && ownerIndex !== 0) {
    throw new HttpsError("invalid-argument", "solo match owner index must be 0");
  }
  return {
    env: data.env,
    pairingKey: data.pairingKey,
    contentFingerprint: data.contentFingerprint,
    ownerIndex,
    mode,
  };
}

function readPairingRecord(raw: Record<string, unknown> | undefined): PairingRecord | null {
  if (raw == null || typeof raw.matchId !== "string" || !HEX_32.test(raw.matchId) ||
      typeof raw.seedHex !== "string" || !HEX_16.test(raw.seedHex) ||
      // 매치 문서는 이 값을 cardDataVersion 으로 들고 있다 — contentFingerprint 라는 이름은
      // 클라 페이로드 쪽 이름이다. 여기서 이름을 잘못 읽으면 레코드가 항상 null 이 되어
      // 매 호출이 페어링을 재설정하고 두 클라가 영원히 만나지 못한다.
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
    // 이 필드가 없던 시절의 문서는 전부 대인전이다.
    expectedParticipants: Number.isInteger(raw.expectedParticipants) ?
      raw.expectedParticipants as number : 2,
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

  return withCountedTransaction("createMatch", async (tx) => {
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
      // 이 가드는 **다른 짝**이 같은 pairingKey 로 진행 중인 매치를 덮어쓰는 걸 막는 것이다.
      // 이미 참가자로 등록된 본인이 자기 매치를 다시 읽는 건 막으면 안 된다 —
      // 두 클라가 동시에 페어링하면 빠른 쪽이 lockDeck 으로 phase 를 "locked" 로 올린 뒤에
      // 느린 쪽의 마지막 폴이 도착하고, 그 정상 흐름이 여기서 already-exists 로 튕겼다.
      const participants = raw.participantUids;
      const owners = objectRecord(raw.ownerIndexByUid);
      const isParticipant = Array.isArray(participants) && participants.includes(uid) &&
        safeInteger(owners?.[uid]) === data.ownerIndex;
      if (!isParticipant) throw new HttpsError("already-exists", "pairing_key_reused");

      // 본인 확인됨. 문서를 건드리지 않고 이미 확정된 신원을 그대로 돌려준다(멱등).
      if (typeof raw.matchId !== "string" || typeof raw.seedHex !== "string" ||
          !Number.isInteger(raw.rulesetVersion)) {
        throw new HttpsError("failed-precondition", "match identity is incomplete");
      }
      return {
        matchId: raw.matchId,
        seedHex: raw.seedHex,
        rulesetVersion: raw.rulesetVersion as number,
        slot: data.ownerIndex,
        status: "paired",
      };
    }
    const priorRecord = readPairingRecord(raw);
    let decision;
    try {
      decision = joinPairing(
        priorRecord,
        uid,
        data.contentFingerprint,
        Date.now(),
        candidate,
        expectedParticipantsOf(data.mode)
      );
    } catch (error) {
      const reason = error instanceof Error ? error.message : String(error);
      if (reason === "content_fingerprint_mismatch") {
        throw new HttpsError("failed-precondition", reason);
      }
      if (reason === "match_pairing_full") throw new HttpsError("permission-denied", reason);
      if (reason === "match_mode_mismatch") throw new HttpsError("failed-precondition", reason);
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
      expectedParticipants: record.expectedParticipants,
      mode: data.mode,
      ownerIndexByUid,
      pairingKeyHash: pairingId,
      // 페어링 시각. 결과 제출 마감(createdAt + 120초)의 기준인 createdAt 과 섞으면
      // 전투 시작 전에 마감이 흘러가므로 별도 필드로 둔다.
      pairingCreatedAt: Timestamp.fromMillis(record.createdAtMs),
      pairedAt: decision.status === "paired" ? FieldValue.serverTimestamp() : null,
      expiresAt: Timestamp.fromMillis(record.expiresAtMs),
      updatedAt: FieldValue.serverTimestamp(),
    }, {merge: true});
    // 두 클라가 같은 페어링 문서에 붙었는지는 여기서만 대조할 수 있다(클라 응답에는 matchId 뿐).
    logger.info("createMatch pairing", {
      matchId: record.matchId,
      uid: uid.slice(0, 6),
      ownerIndex: data.ownerIndex,
      mode: data.mode,
      status: decision.status,
      participants: record.participantUids.length,
      expectedParticipants: record.expectedParticipants,
      pairingKeyHash: pairingId,
    });
    return response;
  });
});
