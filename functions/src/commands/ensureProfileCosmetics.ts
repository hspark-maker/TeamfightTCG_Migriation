import {isDeepStrictEqual} from "node:util";
import {FieldValue} from "firebase-admin/firestore";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import {withCountedTransaction} from "../observability/countedTransaction";
import {assertWritableSchema, isKnownEnv, requireUid, saveDocument} from "../save/saveDocument";
import {ensureCosmeticOwnership, loadCosmeticItems} from "../profile/cosmetics";

/** Idempotent boot migration; no receipt or wallet mutation is needed. */
export const ensureProfileCosmetics = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  if (!isKnownEnv(env)) throw new HttpsError("invalid-argument", "Unknown environment.");
  const catalog = await loadCosmeticItems(env);
  const reference = saveDocument(env, uid);
  return withCountedTransaction("ensureProfileCosmetics", async (transaction) => {
    const snapshot = await transaction.get(reference);
    if (!snapshot.exists) throw new HttpsError("failed-precondition", "Save document does not exist.");
    const current = snapshot.data()!;
    assertWritableSchema(current.schemaVersion, env, uid);
    const profile = ensureCosmeticOwnership(current.profile ?? {}, catalog);
    const changed = !isDeepStrictEqual(profile, current.profile);
    const revision = Number(current.revision) + (changed ? 1 : 0);
    if (changed) transaction.update(reference, {profile, revision, updatedAt: FieldValue.serverTimestamp()});
    return {revision, changed, updatedSlots: {profile}};
  });
});
