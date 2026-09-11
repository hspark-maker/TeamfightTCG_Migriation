import {FieldValue} from "firebase-admin/firestore";
import {randomUUID} from "node:crypto";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {EVENTS} from "../analytics/eventNames";
import {CurrencyGain, grant} from "../currency/wallet";
import {nextWallet} from "../currency/walletStore";
import {GrantedItems, grantRewardItems, loadItemGrantContext} from "../rewards/itemGrant";
import {rankRef} from "../rank/rankStore";
import {beginMissionBump, commitMissionProgress, missionResponse, MissionResponse} from "../missions/missionStore";
import {missionPeriod} from "../missions/period";
import {readMissionCatalog} from "../missions/missionSpec";
import {applyGuideProgress} from "../missions/guideMutation";
import {applySnackGrowthProgress} from "../missions/snackGrowthProgress";
import {db} from "../firebaseApp";
import {recordEvent} from "../observability/analyticsEvent";
import {readSpecRows} from "../packs/packSpecReader";
import {
  currentPassSeason,
  parsePassLevels,
  parsePassSeasons,
  PassLevelDef,
  PassSeasonDef,
  passRewardOwnerId,
} from "../pass/passSpec";
import {
  beginPassMutation,
  commitPassClaim,
  passProgressResponse,
  PassProgressResponse,
} from "../pass/passStore";
import {parseRewardRows, resolveRewards, RewardRow, RewardItem} from "../rewardTable";
import {clientReceiptId, isClientReceiptId} from "../save/receiptId";
import {isKnownEnv, mutateSave, requireUid, SaveMutation} from "../save/saveDocument";

/** Claims one reached reward from either battle-pass track; premium entitlement is server-owned. */
export const claimPassReward = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const level = Number(request.data?.level);
  const track = request.data?.track ?? "free";
  if (!isKnownEnv(env)) throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  if (track !== "free" && track !== "premium") {
    throw new HttpsError("invalid-argument", "PASS_INVALID_TRACK");
  }
  if (!Number.isSafeInteger(level) || level <= 0 || level > 200) {
    throw new HttpsError("invalid-argument", "PASS_INVALID_LEVEL level must be 1..200.");
  }

  const nowMs = Date.now();
  let season: PassSeasonDef;
  let authoredLevel: PassLevelDef;
  let authoredRewards: CurrencyGain[] = [];
  let rewardRows: RewardRow[] = [];
  let items: RewardItem[] = [];
  try {
    const [seasonRows, levelRows, rawRewardRows] = await Promise.all([
      readSpecRows(env, "PassSeason"),
      readSpecRows(env, "PassLevel"),
      readSpecRows(env, "Reward"),
    ]);
    const currentSeason = currentPassSeason(parsePassSeasons(seasonRows), nowMs);
    if (currentSeason === null) {
      throw new HttpsError("permission-denied", "PASS_NO_ACTIVE_SEASON");
    }
    season = currentSeason;
    const levelDef = parsePassLevels(levelRows, season).find((entry) => entry.level === level);
    if (levelDef === undefined) {
      throw new HttpsError("not-found", `PASS_LEVEL_NOT_FOUND level=${level}`);
    }
    authoredLevel = levelDef;

    rewardRows = parseRewardRows(rawRewardRows);
    const rewards = resolveRewards(rewardRows, "Pass", passRewardOwnerId(season.seasonId, level, track));
    items = rewards.items;
    if (rewards.dropped.length > 0) {
      logger.warn("pass reward rows dropped", {
        uid, env, seasonId: season.seasonId, level, track, dropped: rewards.dropped,
      });
    }
    if (rewards.gains.length === 0 && items.length === 0) {
      throw new HttpsError(
        "failed-precondition", `PASS_REWARD_NOT_FOUND owner=${passRewardOwnerId(season.seasonId, level, track)}`);
    }
    authoredRewards = rewards.gains;
  } catch (error) {
    if (error instanceof HttpsError) throw error;
    throw new HttpsError("failed-precondition", `PASS_SPEC_INVALID ${String(error)}`);
  }

  const txId = clientReceiptId(request.data?.txId, randomUUID());
  const itemContext = items.length ? await loadItemGrantContext(env, items) : null;
  const catalog = itemContext ? await readMissionCatalog(env) : [];
  const period = missionPeriod(nowMs);
  let missionState: MissionResponse | undefined;
  let itemGrant: GrantedItems = {slots: {}, cards: [], currencies: []};
  let replayed = true;
  let progress: PassProgressResponse | undefined;
  let granted: CurrencyGain[] = [];

  const result = await mutateSave(env, uid, "claimPassReward", {kind: "client", txId},
    async (current, transaction, wallet): Promise<SaveMutation> => {
      const pass = await beginPassMutation(transaction, db, env, uid, season.seasonId);
      if (Date.now() >= season.endAtMs) throw new HttpsError("permission-denied", "PASS_NO_ACTIVE_SEASON");
      if (track === "premium" && !pass.state.premiumUnlocked) {
        throw new HttpsError("permission-denied", "PASS_PREMIUM_LOCKED");
      }
      const rankSnapshot = itemContext === null ? null : await transaction.get(rankRef(db, env, uid));
      const missions = itemContext ? await beginMissionBump(transaction, db, env, uid, period) : null;
      const claims = track === "premium" ? pass.state.premiumClaimed : pass.state.claimed;
      if (claims[String(level)] === true) {
        throw new HttpsError("already-exists", `PASS_ALREADY_CLAIMED level=${level} track=${track}`);
      }
      if (pass.state.exp < authoredLevel.requiredExp) {
        throw new HttpsError(
          "permission-denied",
          `PASS_LEVEL_LOCKED level=${level} exp=${pass.state.exp}/${authoredLevel.requiredExp}`,
        );
      }

      itemGrant = itemContext === null ? {slots: {}, cards: [], currencies: []} :
        grantRewardItems(current, items, itemContext, rewardRows, String(request.data?.selectedPackId ?? ""),
          Number(rankSnapshot?.data()?.points ?? (current.rank as {points?: number})?.points ?? 0));
      granted = [...authoredRewards, ...itemGrant.currencies];
      if (missions && itemContext) {
        applyGuideProgress(missions, current, itemGrant.slots, itemContext.cards, catalog);
        applySnackGrowthProgress(missions, itemGrant.cards);
        commitMissionProgress(transaction, missions, FieldValue.serverTimestamp());
        missionState = missionResponse(missions.state, period, catalog);
      }
      commitPassClaim(transaction, pass, level, FieldValue.serverTimestamp(), track);
      progress = passProgressResponse(pass.state);
      return {
        slots: itemGrant.slots,
        wallet: granted.length ? nextWallet(wallet, grant(wallet.balances, granted), "claimPassReward") : undefined,
      };
    },
    (adopted) => {
      replayed = false;
      return {...adopted, seasonId: season.seasonId, level, track, granted,
        cards: itemGrant.cards, packs: itemGrant.packs ?? [], progress,
        ...(missionState ? {missions: missionState} : {})};
    });

  if (replayed) {
    logger.info("receipt replay", {
      uid, env, source: "claimPassReward", txId, revision: result.revision,
    });
  } else {
    recordEvent(EVENTS.passRewardClaimed.name, {
      uid, env, eventId: txId, sourceCommand: "claimPassReward", result: "success",
      seasonId: season.seasonId, level, track,
      granted: granted.map((gain) => `${gain.currency}+${gain.amount}`).join(","),
      revision: result.revision,
      txIdSource: isClientReceiptId(request.data?.txId) ? "client" : "server",
    });
  }
  return result;
});
