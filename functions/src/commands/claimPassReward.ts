import {FieldValue} from "firebase-admin/firestore";
import {randomUUID} from "node:crypto";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {EVENTS} from "../analytics/eventNames";
import {CurrencyGain, grant} from "../currency/wallet";
import {nextWallet} from "../currency/walletStore";
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
import {parseRewardRows, resolveRewards} from "../rewardTable";
import {clientReceiptId, isClientReceiptId} from "../save/receiptId";
import {isKnownEnv, mutateSave, requireUid, SaveMutation} from "../save/saveDocument";

/** Claims one reached reward from the active free battle-pass track. */
export const claimPassReward = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const level = Number(request.data?.level);
  if (!isKnownEnv(env)) throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  if (!Number.isSafeInteger(level) || level <= 0 || level > 200) {
    throw new HttpsError("invalid-argument", "PASS_INVALID_LEVEL level must be 1..200.");
  }

  const nowMs = Date.now();
  let season: PassSeasonDef;
  let authoredLevel: PassLevelDef;
  let authoredRewards: CurrencyGain[] = [];
  try {
    const [seasonRows, levelRows, rawRewardRows] = await Promise.all([
      readSpecRows(env, "PassSeason"),
      readSpecRows(env, "PassLevel"),
      readSpecRows(env, "Reward"),
    ]);
    const currentSeason = currentPassSeason(parsePassSeasons(seasonRows), nowMs);
    if (currentSeason === null) {
      throw new HttpsError("failed-precondition", "PASS_NO_ACTIVE_SEASON");
    }
    season = currentSeason;
    const levelDef = parsePassLevels(levelRows, season).find((entry) => entry.level === level);
    if (levelDef === undefined) {
      throw new HttpsError("not-found", `PASS_LEVEL_NOT_FOUND level=${level}`);
    }
    authoredLevel = levelDef;

    const rewards = resolveRewards(
      parseRewardRows(rawRewardRows), "Pass", passRewardOwnerId(season.seasonId, level));
    if (rewards.dropped.length > 0) {
      logger.warn("pass reward rows dropped", {
        uid, env, seasonId: season.seasonId, level, dropped: rewards.dropped,
      });
    }
    if (rewards.gains.length === 0) {
      throw new HttpsError(
        "failed-precondition", `PASS_REWARD_NOT_FOUND owner=${season.seasonId}:${level}`);
    }
    authoredRewards = rewards.gains;
  } catch (error) {
    if (error instanceof HttpsError) throw error;
    throw new HttpsError("failed-precondition", `PASS_SPEC_INVALID ${String(error)}`);
  }

  const txId = clientReceiptId(request.data?.txId, randomUUID());
  let replayed = true;
  let progress: PassProgressResponse | undefined;
  let granted: CurrencyGain[] = [];

  const result = await mutateSave(env, uid, "claimPassReward", {kind: "client", txId},
    async (_current, transaction, wallet): Promise<SaveMutation> => {
      const pass = await beginPassMutation(transaction, db, env, uid, season.seasonId);
      if (pass.state.claimed[String(level)] === true) {
        throw new HttpsError("already-exists", `PASS_ALREADY_CLAIMED level=${level}`);
      }
      if (pass.state.exp < authoredLevel.requiredExp) {
        throw new HttpsError(
          "failed-precondition",
          `PASS_LEVEL_LOCKED level=${level} exp=${pass.state.exp}/${authoredLevel.requiredExp}`,
        );
      }

      granted = authoredRewards;
      commitPassClaim(transaction, pass, level, FieldValue.serverTimestamp());
      progress = passProgressResponse(pass.state);
      return {
        slots: {},
        wallet: nextWallet(wallet, grant(wallet.balances, granted), "claimPassReward"),
      };
    },
    (adopted) => {
      replayed = false;
      return {...adopted, seasonId: season.seasonId, level, granted, progress};
    });

  if (replayed) {
    logger.info("receipt replay", {
      uid, env, source: "claimPassReward", txId, revision: result.revision,
    });
  } else {
    recordEvent(EVENTS.passRewardClaimed.name, {
      uid, env, eventId: txId, sourceCommand: "claimPassReward", result: "success",
      seasonId: season.seasonId, level,
      granted: granted.map((gain) => `${gain.currency}+${gain.amount}`).join(","),
      revision: result.revision,
      txIdSource: isClientReceiptId(request.data?.txId) ? "client" : "server",
    });
  }
  return result;
});
