import {randomUUID} from "node:crypto";
import {FieldValue} from "firebase-admin/firestore";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {isKnownEnv, mutateSave, requireUid} from "../save/saveDocument";
import {clientReceiptId} from "../save/receiptId";
import {rejectDomain} from "../save/domainReject";
import {readSpecRows} from "../packs/packSpecReader";
import {parseAlbumEntryRows, parseAlbumThemeRows} from "../completionTable";
import {grant} from "../currency/wallet";
import {nextWallet} from "../currency/walletStore";
import {isAchievementId} from "../achievements/achievementCatalog";
import {readAchievementCatalog} from "../achievements/achievementSpec";
import {judgeAchievementClaim} from "../achievements/achievementProgress";
import {achievementResponse} from "../achievements/achievementStore";
import {applyStatisticsAlbums, PlayerStatistics, projectAchievements, statisticsResponse} from "../statistics/playerStatistics";
import {beginStatistics, commitStatistics} from "../statistics/playerStatisticsStore";

export const claimAchievement = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const achievementId = request.data?.achievementId;
  if (!isKnownEnv(env) || !isAchievementId(achievementId)) {
    throw new HttpsError("invalid-argument", "Valid env and achievementId are required.");
  }
  const [catalog, entries, themes] = await Promise.all([
    readAchievementCatalog(env), readSpecRows(env, "AlbumEntry"), readSpecRows(env, "AlbumThemeInfo"),
  ]);
  const definition = catalog.find((entry) => entry.id === achievementId);
  if (!definition) rejectDomain("AchievementNotFound", "Unknown achievement.", {uid, env, achievementId});
  let achievements: ReturnType<typeof achievementResponse> = {revision: 0, progress: {}, claimed: {}};
  let statistics: PlayerStatistics | undefined;
  const txId = clientReceiptId(request.data?.txId, randomUUID());
  const result = await mutateSave(env, uid, "claimAchievement", {kind: "client", txId}, async (current, tx, wallet) => {
    const context = await beginStatistics(tx, db, env, uid);
    applyStatisticsAlbums(context.state, current, parseAlbumEntryRows(entries), parseAlbumThemeRows(themes));
    const state = projectAchievements(context.state, context.achievements);
    const reason = judgeAchievementClaim(achievementId, state, catalog);
    if (reason) rejectDomain(reason, `Achievement claim rejected: ${reason}.`, {uid, env, achievementId});
    // Permanent marker is checked in the same transaction as the wallet, even after receipt TTL expiry.
    state.claimed[achievementId] = true;
    context.achievements = state;
    commitStatistics(tx, context, FieldValue.serverTimestamp());
    achievements = achievementResponse(context.achievements);
    statistics = statisticsResponse(context.state);
    return {slots: {}, wallet: nextWallet(wallet, grant(wallet.balances, definition.reward.currencies), "claimAchievement")};
  }, (adopted) => ({...adopted, achievementId, achievements, statistics, granted: definition.reward.currencies}));
  if (result.achievementId !== achievementId) {
    rejectDomain("TxIdReused", "Achievement txId was used with different arguments.", {uid, env, achievementId});
  }
  return result;
});
