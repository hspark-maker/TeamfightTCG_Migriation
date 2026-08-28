import {createHash} from "node:crypto";

export const SERVER_RULESET_VERSION = 1;
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
