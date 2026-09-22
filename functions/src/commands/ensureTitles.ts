import {HttpsError, onCall} from "firebase-functions/v2/https";
import {assertWritableSchema, isKnownEnv, requireUid, saveDocument} from "../save/saveDocument";
import {withCountedTransaction} from "../observability/countedTransaction";
import {loadAchievementTitles, titleConditionDefinitions} from "../titles/achievementTitles";
import {readAchievementContext} from "../achievements/achievementSpec";

/** Retained boot contract: provide unlock descriptions without granting or mutating ownership. */
export const ensureTitles = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  if (!isKnownEnv(env)) throw new HttpsError("invalid-argument", "Unknown environment.");
  const context = await loadAchievementTitles(env);
  const definitions = context.length > 0 ?
    titleConditionDefinitions(context, (await readAchievementContext(env, context)).catalog) : [];
  const reference = saveDocument(env, uid);
  return withCountedTransaction("ensureTitles", async (tx) => {
    const snapshot = await tx.get(reference);
    if (!snapshot.exists) throw new HttpsError("failed-precondition", "Save document does not exist.");
    const current = snapshot.data()!;
    assertWritableSchema(current.schemaVersion, env, uid);
    return {changed: false, revision: current.revision, updatedSlots: {}, titles: [], definitions};
  });
});
