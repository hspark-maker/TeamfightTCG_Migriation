import {createHash} from "node:crypto";

export const SERVER_RULESET_VERSION = 2;
export const SERVER_AUTHORITATIVE_RULESET_VERSION = 2;
export const MATCH_PAIRING_TTL_MS = 10 * 60 * 1000;

export type MatchIdentity = {
  matchId: string;
  seedHex: string;
};

export type PairingRecord = MatchIdentity & {
  contentFingerprint: string;
  rulesetVersion: number;
  participantUids: string[];
  createdAtMs: number;
  expiresAtMs: number;
};

export type PairingDecision = {
  record: PairingRecord;
  slot: number;
  status: "waiting" | "paired";
};

export function pairingDocumentId(pairingKey: string): string {
  return createHash("sha256").update(pairingKey, "utf8").digest("hex");
}

/**
 * 매치 문서 id. pairingKey 에서 결정론적으로 파생한다 —
 * 그래야 페어링 레코드와 매치 문서가 같은 문서 하나로 합쳐진다(별도 matchPairings 컬렉션 불필요).
 *
 * 예측 가능해도 안전하다: 시드는 이 값과 무관한 별도 난수이고, 참가 자격은 matchId 가 아니라
 * participantUids 명단으로 판정한다. 명단은 페어링 트랜잭션에서만 늘어난다.
 *
 * pairingKey 자체가 매치마다 새 난수(양측 nonce)를 섞어 만들어지므로 사실상 재사용되지 않지만,
 * 만에 하나 같은 키가 다시 오면 createMatch 가 phase/status 를 보고 거절한다.
 * @param {string} pairingKey 클라가 만든 페어링 키
 * @return {string} 32자리 hex 매치 id
 */
export function matchIdFromPairingKey(pairingKey: string): string {
  return pairingDocumentId(pairingKey).slice(0, 32);
}

export function joinPairing(
  existing: PairingRecord | null,
  uid: string,
  contentFingerprint: string,
  nowMs: number,
  newIdentity: MatchIdentity
): PairingDecision {
  let record = existing;
  if (record == null || record.expiresAtMs <= nowMs) {
    record = {
      ...newIdentity,
      contentFingerprint,
      rulesetVersion: SERVER_RULESET_VERSION,
      participantUids: [uid],
      createdAtMs: nowMs,
      expiresAtMs: nowMs + MATCH_PAIRING_TTL_MS,
    };
    return {record, slot: 0, status: "waiting"};
  }

  if (record.contentFingerprint !== contentFingerprint) {
    throw new Error("content_fingerprint_mismatch");
  }
  const priorSlot = record.participantUids.indexOf(uid);
  if (priorSlot >= 0) {
    return {
      record,
      slot: priorSlot,
      status: record.participantUids.length >= 2 ? "paired" : "waiting",
    };
  }
  if (record.participantUids.length >= 2) {
    throw new Error("match_pairing_full");
  }

  record = {...record, participantUids: [...record.participantUids, uid]};
  return {record, slot: 1, status: "paired"};
}
