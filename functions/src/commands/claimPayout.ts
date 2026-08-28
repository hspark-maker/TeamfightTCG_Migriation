import {HttpsError, onCall} from "firebase-functions/v2/https";
import {FieldValue, Timestamp} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {HEX_32} from "../match/payloadGuards";

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
