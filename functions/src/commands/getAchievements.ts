import {FieldValue} from "firebase-admin/firestore";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {isKnownEnv, requireUid, saveDocument} from "../save/saveDocument";
import {readSpecRows} from "../packs/packSpecReader";
import {parseAlbumEntryRows, parseAlbumThemeRows} from "../completionTable";
import {readAchievementCatalog} from "../achievements/achievementSpec";
import {applyAchievementAlbums} from "../achievements/achievementProgress";
import {achievementResponse, achievementsRef, readAchievements, writeAchievements} from "../achievements/achievementStore";

export const getAchievements = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  if (!isKnownEnv(env)) throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  const [definitions, entries, themes] = await Promise.all([
    readAchievementCatalog(env), readSpecRows(env, "AlbumEntry"), readSpecRows(env, "AlbumThemeInfo"),
  ]);
  const achievements = await db.runTransaction(async (tx) => {
    const ref = achievementsRef(db, env, uid);
    const [snapshot, save] = await tx.getAll(ref, saveDocument(env, uid));
    if (!save.exists) throw new HttpsError("failed-precondition", "Save document does not exist.");
    const state = readAchievements(snapshot);
    const previous = state.progress.CompleteAlbum ?? 0;
    applyAchievementAlbums(state, save.data()!, parseAlbumEntryRows(entries), parseAlbumThemeRows(themes));
    if ((state.progress.CompleteAlbum ?? 0) !== previous) {
      writeAchievements(tx, ref, state, FieldValue.serverTimestamp());
    }
    return achievementResponse(state);
  });
  return {achievements, definitions};
});
