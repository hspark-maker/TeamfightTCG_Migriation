import {FieldValue} from "firebase-admin/firestore";
import {randomUUID} from "node:crypto";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {
  battleAccountExperience, grantAccountExperience, loadAccountProgression,
} from "../account/accountProgression";
import {confirmedBattleExperienceOutcome} from "../account/battleExperience";
import {grant} from "../currency/wallet";
import {nextWallet} from "../currency/walletStore";
import {HEX_32} from "../match/payloadGuards";
import {applyGuideProgress} from "../missions/guideMutation";
import {readMissionCatalog} from "../missions/missionSpec";
import {beginMissionBump, commitMissionProgress, missionResponse, MissionResponse} from "../missions/missionStore";
import {missionPeriod} from "../missions/period";
import {rankRef} from "../rank/rankStore";
import {clientReceiptId} from "../save/receiptId";
import {isKnownEnv, mutateSave, requireUid} from "../save/saveDocument";

/** A permanent match marker outlives short-lived callable receipts and payout documents. */
export const claimBattleExperience = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const matchId = request.data?.matchId;
  if (!isKnownEnv(env) || typeof matchId !== "string" || !HEX_32.test(matchId)) {
    throw new HttpsError("invalid-argument", "Invalid environment or matchId.");
  }
  const context = await loadAccountProgression(env).catch(() => {
    // Missing/invalid published specs can be repaired; keep the client's recovery queue.
    throw new HttpsError("unavailable", "Account progression specs are unavailable.");
  });
  const matchRef = db.doc(`envs/${env}/matches/${matchId}`);
  const claimRef = db.doc(`envs/${env}/users/${uid}/accountBattleClaims/${matchId}`);
  const txId = clientReceiptId(request.data?.txId, randomUUID());
  const catalog = context.itemContext === null ? [] : await readMissionCatalog(env);
  const period = missionPeriod(Date.now());
  let credited: ReturnType<typeof grantAccountExperience> | undefined;
  let missions: MissionResponse | undefined;
  let alreadyClaimed = false;

  return mutateSave(env, uid, "claimBattleExperience", {kind: "client", txId},
    async (current, transaction, wallet, preparePackStatistics) => {
      // Reset callback output on transaction retries. All reads precede any write.
      credited = undefined;
      missions = undefined;
      const marker = await transaction.get(claimRef);
      alreadyClaimed = marker.exists;
      if (alreadyClaimed) {
        // Return current authority even when the previous response was lost, without regranting.
        return {slots: {profile: {...current.profile}}};
      }
      const match = (await transaction.get(matchRef)).data();
      const outcome = confirmedBattleExperienceOutcome(match, uid);
      if (!outcome.allow) {
        throw new HttpsError(outcome.reason === "MatchNotOwned" ? "permission-denied" : "unavailable",
          outcome.reason);
      }
      const rank = context.itemContext === null ? null :
        await transaction.get(rankRef(db, env, uid));
      const missionBump = context.itemContext === null ? null :
        await beginMissionBump(transaction, db, env, uid, period, current);
      credited = grantAccountExperience(current,
        battleAccountExperience(current, outcome.won, context), context,
        Number(rank?.data()?.points ?? current.rank?.points ?? 0));
      await preparePackStatistics(credited.packs?.length ?? 0);
      if (missionBump !== null && context.itemContext !== null && credited.cards.length > 0) {
        applyGuideProgress(missionBump, current, credited.slots, context.itemContext.cards, catalog);
        commitMissionProgress(transaction, missionBump, FieldValue.serverTimestamp());
        missions = missionResponse(missionBump.state, period, catalog);
      }
      transaction.create(claimRef, {
        matchId, uid, won: outcome.won, claimedAt: FieldValue.serverTimestamp(),
        accountExperience: credited.accountExperience,
      });
      return {
        slots: credited.slots,
        wallet: credited.currencies.length === 0 ? undefined :
          nextWallet(wallet, grant(wallet.balances, credited.currencies), "claimBattleExperience"),
      };
    },
    (adopted) => ({
      ...adopted, matchId, alreadyClaimed,
      granted: credited?.currencies ?? [], cards: credited?.cards ?? [], packs: credited?.packs ?? [],
      ...(credited === undefined ? {} : {accountExperience: credited.accountExperience}),
      ...(missions === undefined ? {} : {missions}),
    }));
});
