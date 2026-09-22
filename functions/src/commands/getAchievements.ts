import {FieldValue} from "firebase-admin/firestore";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {isKnownEnv, requireUid, saveDocument} from "../save/saveDocument";
import {readSpecRows} from "../packs/packSpecReader";
import {parseAlbumEntryRows, parseAlbumThemeRows} from "../completionTable";
import {readAchievementCatalog} from "../achievements/achievementSpec";
import {achievementResponse} from "../achievements/achievementStore";
import {applyStatisticsAlbums, projectAchievements, statisticsResponse} from "../statistics/playerStatistics";
import {beginStatistics, commitStatistics, statisticsChanged} from "../statistics/playerStatisticsStore";

export const getAchievements = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  if (!isKnownEnv(env)) throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  const [definitions, entries, themes] = await Promise.all([
    readAchievementCatalog(env), readSpecRows(env, "AlbumEntry"), readSpecRows(env, "AlbumThemeInfo"),
  ]);
  const result = await db.runTransaction(async (tx) => {
    const context = await beginStatistics(tx, db, env, uid);
    const save = await tx.get(saveDocument(env, uid));
    if (!save.exists) throw new HttpsError("failed-precondition", "Save document does not exist.");
    applyStatisticsAlbums(context.state, save.data()!, parseAlbumEntryRows(entries), parseAlbumThemeRows(themes));
    if (statisticsChanged(context)) commitStatistics(tx, context, FieldValue.serverTimestamp());
    return {achievements: achievementResponse(projectAchievements(context.state, context.achievements)),
      statistics: statisticsResponse(context.state)};
  });
  return {...result, definitions};
});
