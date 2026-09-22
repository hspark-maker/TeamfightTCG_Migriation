import {FieldValue} from "firebase-admin/firestore";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {assertWritableSchema, isKnownEnv, requireUid, saveDocument} from "../save/saveDocument";
import {withCountedTransaction} from "../observability/countedTransaction";
import {applyStatisticsAlbums} from "../statistics/playerStatistics";
import {beginStatistics, commitStatistics, statisticsChanged} from "../statistics/playerStatisticsStore";
import {grantAutomaticTitles, loadAutomaticTitles, titleConditionDefinitions} from "../titles/automaticTitles";

/** Boot-only reconciliation, before the client adopts its initial save revision. */
export const ensureTitles = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  if (!isKnownEnv(env)) throw new HttpsError("invalid-argument", "Unknown environment.");
  const context = await loadAutomaticTitles(env);
  const definitions = titleConditionDefinitions(context);
  const reference = saveDocument(env, uid);
  return withCountedTransaction("ensureTitles", async (tx) => {
    const snapshot = await tx.get(reference);
    if (!snapshot.exists) throw new HttpsError("failed-precondition", "Save document does not exist.");
    const current = snapshot.data()!;
    assertWritableSchema(current.schemaVersion, env, uid);
    if (context === null) return {changed: false, revision: current.revision, updatedSlots: {}, titles: [], definitions};
    const statistics = await beginStatistics(tx, db, env, uid);
    applyStatisticsAlbums(statistics.state, current, context.entries, context.themes);
    const granted = grantAutomaticTitles(current.profile ?? {}, statistics.state, context);
    const changed = granted.titles.length > 0;
    const revision = Number(current.revision) + (changed ? 1 : 0);
    if (statisticsChanged(statistics)) commitStatistics(tx, statistics, FieldValue.serverTimestamp());
    if (changed) tx.update(reference, {profile: granted.profile, revision, updatedAt: FieldValue.serverTimestamp()});
    return {changed, revision, updatedSlots: changed ? {profile: granted.profile} : {}, titles: granted.titles, definitions};
  });
});
