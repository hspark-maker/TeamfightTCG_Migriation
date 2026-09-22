import {randomUUID} from "node:crypto";
import {FieldValue} from "firebase-admin/firestore";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {mutateSave, requireUid} from "../save/saveDocument";
import {clientReceiptId} from "../save/receiptId";
import {readAchievementCatalog} from "../achievements/achievementSpec";
import {achievementResponse} from "../achievements/achievementStore";
import {PlayerStatistics, statisticsResponse} from "../statistics/playerStatistics";
import {beginStatistics, commitStatistics} from "../statistics/playerStatisticsStore";

// Fill progress only. Claims still go through claimAchievement, including currency/title rewards.
export const devCompleteAchievements = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  if (env !== "test") {
    throw new HttpsError("permission-denied", "devCompleteAchievements is available on the test env only.");
  }

  const catalog = await readAchievementCatalog(env);
  const txId = clientReceiptId(request.data?.txId, randomUUID());
  let achievements: ReturnType<typeof achievementResponse> = {revision: 0, progress: {}, claimed: {}};
  let statistics: PlayerStatistics | undefined;
  return mutateSave(env, uid, "devCompleteAchievements", {kind: "client", txId}, async (_current, tx) => {
    const context = await beginStatistics(tx, db, env, uid);
    const lifetime = context.state.lifetime;
    for (const definition of catalog) {
      const target = definition.target;
      switch (definition.event) {
      case "WinBattle": lifetime.wins = Math.max(lifetime.wins, target); break;
      case "DestroyCards": lifetime.cardsDestroyed = Math.max(lifetime.cardsDestroyed, target); break;
      case "WinStreak": lifetime.bestWinStreak = Math.max(lifetime.bestWinStreak, target); break;
      case "OpenPack": lifetime.packsOpened = Math.max(lifetime.packsOpened, target); break;
      case "CompleteAlbum": lifetime.albumsCompleted = Math.max(lifetime.albumsCompleted, target); break;
      case "PlaySynergy":
        lifetime.synergyPlays[definition.synergyId] = Math.max(lifetime.synergyPlays[definition.synergyId] ?? 0, target);
        break;
      }
    }
    commitStatistics(tx, context, FieldValue.serverTimestamp());
    achievements = achievementResponse(context.achievements);
    statistics = statisticsResponse(context.state);
    return {slots: {}};
  }, (adopted) => ({...adopted, achievements, statistics, completedAchievements: catalog.length}));
});
