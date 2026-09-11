import {FieldValue} from "firebase-admin/firestore";
import {randomUUID} from "node:crypto";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import {CurrencyGain, grant} from "../currency/wallet";
import {nextWallet} from "../currency/walletStore";
import {db} from "../firebaseApp";
import {readSpecRows} from "../packs/packSpecReader";
import {PassRepeatDefinition, PassRepeatResponse, passRepeatDefinition, passRepeatResponse} from "../pass/passRepeat";
import {currentPassSeason, parsePassLevels, parsePassSeasons, PassSeasonDef} from "../pass/passSpec";
import {beginPassMutation, commitPassRepeatClaim, passProgressResponse, PassProgressResponse} from "../pass/passStore";
import {parseRewardRows} from "../rewardTable";
import {clientReceiptId} from "../save/receiptId";
import {isKnownEnv, mutateSave, requireUid, SaveMutation} from "../save/saveDocument";

/** Claims all earned repeat rewards up to a client-observed cumulative count. */
export const claimPassRepeatReward = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const seasonId = String(request.data?.seasonId ?? "");
  const toClaimCount = Number(request.data?.toClaimCount);
  if (!isKnownEnv(env)) throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  if (!seasonId || !Number.isSafeInteger(toClaimCount) || toClaimCount <= 0) {
    throw new HttpsError("invalid-argument", "PASS_REPEAT_INVALID_REQUEST");
  }

  let season: PassSeasonDef;
  let definition: PassRepeatDefinition;
  try {
    const [seasons, levels, rewards] = await Promise.all([
      readSpecRows(env, "PassSeason"), readSpecRows(env, "PassLevel"), readSpecRows(env, "Reward"),
    ]);
    const active = currentPassSeason(parsePassSeasons(seasons), Date.now());
    if (active === null || active.seasonId !== seasonId) {
      throw new HttpsError("permission-denied", "PASS_NO_ACTIVE_SEASON");
    }
    season = active;
    const repeat = passRepeatDefinition(seasonId, parsePassLevels(levels, season), parseRewardRows(rewards));
    if (repeat === null) throw new HttpsError("failed-precondition", "PASS_REPEAT_REWARD_NOT_FOUND");
    definition = repeat;
  } catch (error) {
    if (error instanceof HttpsError) throw error;
    throw new HttpsError("failed-precondition", `PASS_SPEC_INVALID ${String(error)}`);
  }

  const txId = clientReceiptId(request.data?.txId, randomUUID());
  let granted: CurrencyGain[] = [];
  let progress: PassProgressResponse | undefined;
  let repeat: PassRepeatResponse | undefined;
  return mutateSave(env, uid, "claimPassRepeatReward", {kind: "client", txId},
    async (_current, transaction, wallet): Promise<SaveMutation> => {
      const pass = await beginPassMutation(transaction, db, env, uid, seasonId);
      // Re-check on transaction retries; a request started just before expiry cannot claim later.
      if (Date.now() >= season.endAtMs) throw new HttpsError("permission-denied", "PASS_NO_ACTIVE_SEASON");
      const status = passRepeatResponse(pass.state, definition);
      if (toClaimCount <= status.claimedCount) {
        throw new HttpsError("already-exists", "PASS_REPEAT_ALREADY_CLAIMED");
      }
      if (toClaimCount > status.claimedCount + status.availableClaims) {
        throw new HttpsError("permission-denied", "PASS_REPEAT_LOCKED");
      }
      const count = toClaimCount - status.claimedCount;
      granted = definition.reward.map((gain) => ({currency: gain.currency, amount: gain.amount * count}));
      if (granted.some((gain) => !Number.isSafeInteger(gain.amount))) {
        throw new HttpsError("failed-precondition", "PASS_REPEAT_REWARD_OVERFLOW");
      }
      commitPassRepeatClaim(transaction, pass, toClaimCount, FieldValue.serverTimestamp());
      progress = passProgressResponse(pass.state);
      repeat = passRepeatResponse(pass.state, definition);
      return {slots: {}, wallet: nextWallet(wallet, grant(wallet.balances, granted), "claimPassRepeatReward")};
    },
    (adopted) => ({...adopted, seasonId, granted, progress, repeat}));
});
