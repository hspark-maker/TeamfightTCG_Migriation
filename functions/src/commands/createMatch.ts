import {HttpsError, onCall} from "firebase-functions/v2/https";
import {FieldValue, Timestamp} from "firebase-admin/firestore";
import {randomBytes} from "node:crypto";
import {db} from "../firebaseApp";
import {
  joinPairing,
  MatchIdentity,
  PairingRecord,
  pairingDocumentId,
} from "../matchPairing";
import {HEX_16, HEX_32, HEX_64, objectRecord} from "../match/payloadGuards";

const PAIRING_KEY = /^[A-Za-z0-9_-]{1,128}$/;

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

