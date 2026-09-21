import {FieldValue} from "firebase-admin/firestore";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {isKnownEnv, mutateSave, requireUid} from "../save/saveDocument";
import {isClientReceiptId} from "../save/receiptId";
import {rejectDomain} from "../save/domainReject";
import {measuredCallable} from "../observability/requestMetrics";
import {CRAFT_CURRENCY} from "../crafting/catalog";
import {readCraftingCatalog} from "../crafting/spec";
import {canAfford, spend} from "../currency/wallet";
import {nextWallet} from "../currency/walletStore";
import {buildOwnershipSlotFromIds, readOwnedIds} from "../packs/packSlots";
import {readSpecRows} from "../packs/packSpecReader";
import {applyAcquiredCardGrowth, growthSlot, readGrowthEntries} from "../growth/cardGrowth";
import {readMissionCatalog} from "../missions/missionSpec";
import {beginMissionBump, commitMissionProgress, missionResponse, MissionResponse} from "../missions/missionStore";
import {applyGuideProgress} from "../missions/guideMutation";
import {missionPeriod} from "../missions/period";
import {parseAlbumEntryRows, parseAlbumThemeRows} from "../completionTable";
import {achievementResponse} from "../achievements/achievementStore";
import {applyStatisticsAlbums, PlayerStatistics, projectAchievements, statisticsResponse} from "../statistics/playerStatistics";
import {beginStatistics, commitStatistics, statisticsChanged} from "../statistics/playerStatisticsStore";

export const craftCard = onCall(measuredCallable("craftCard", async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const cardId = request.data?.cardId;
  const txId = request.data?.txId;
  if (!isKnownEnv(env) || typeof cardId !== "number" || !Number.isSafeInteger(cardId) || cardId <= 0 ||
      !isClientReceiptId(txId)) {
    throw new HttpsError("invalid-argument", "Valid env, positive integer cardId and client txId are required.");
  }
  const context = {uid, env, cardId, txId};
  const period = missionPeriod(Date.now());
  let cost = 0;
  let missions: MissionResponse | undefined;
  let achievements: ReturnType<typeof achievementResponse> | undefined;
  let statistics: PlayerStatistics | undefined;
  // Loading only inside the mutation preserves receipt replay even if recipes are later disabled/unpublished.
  const result = await mutateSave(env, uid, "craftCard", {kind: "client", txId}, async (current, tx, wallet) => {
    const owned = readOwnedIds(current.ownership);
    if (owned.includes(cardId)) rejectDomain("AlreadyOwned", "The card is already owned.", context);
    const [catalog, missionCatalog, entries, themes] = await Promise.all([
      readCraftingCatalog(env), readMissionCatalog(env),
      readSpecRows(env, "AlbumEntry"), readSpecRows(env, "AlbumThemeInfo"),
    ]);
    const recipe = catalog.recipes.find((entry) => entry.cardId === cardId);
    if (!recipe) rejectDomain("CardNotCraftable", "The card is not available for crafting.", context);
    cost = recipe.cost;
    if (!canAfford(wallet.balances, CRAFT_CURRENCY, cost)) {
      rejectDomain("NotAffordable", "Not enough CardDust to craft this card.", {...context, cost});
    }
    // Finish every document read before queuing mission, achievement, wallet or ownership writes.
    const bump = await beginMissionBump(tx, db, env, uid, period, current);
    const stats = await beginStatistics(tx, db, env, uid);
    const slots = {
      ownership: buildOwnershipSlotFromIds(owned, [cardId]),
      cardGrowth: growthSlot(applyAcquiredCardGrowth(readGrowthEntries(current.cardGrowth),
        [cardId], new Map([[cardId, recipe.grade]]))),
    };
    applyGuideProgress(bump, current, slots, catalog.cards, missionCatalog);
    commitMissionProgress(tx, bump, FieldValue.serverTimestamp());
    missions = missionResponse(bump.state, period, missionCatalog);
    applyStatisticsAlbums(stats.state, {...current, ...slots}, parseAlbumEntryRows(entries), parseAlbumThemeRows(themes));
    if (statisticsChanged(stats)) commitStatistics(tx, stats, FieldValue.serverTimestamp());
    achievements = achievementResponse(projectAchievements(stats.state, stats.achievements));
    statistics = statisticsResponse(stats.state);
    return {slots, wallet: nextWallet(wallet, spend(wallet.balances, CRAFT_CURRENCY, cost), "craftCard")};
  }, (adopted) => ({...adopted, cardId, currency: CRAFT_CURRENCY, cost, missions, achievements, statistics}));
  // A reused key replays without mutation; never report another card as this request's success.
  if (result.cardId !== cardId) rejectDomain("TxIdReused", "Crafting txId was used with different arguments.", context);
  return result;
}));
