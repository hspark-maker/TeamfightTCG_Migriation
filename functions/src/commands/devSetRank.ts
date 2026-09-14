import {randomUUID} from "node:crypto";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {FieldValue} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {mutateSave, requireUid} from "../save/saveDocument";
import {clientReceiptId} from "../save/receiptId";
import {rejectDomain} from "../save/domainReject";
import {readSpecRows} from "../specs/specBlobReader";
import {currentRankSeason} from "../rank/rankSeason";
import {
  applyRankSeason, applyTutorialRankEntry, legacyClaimedTiers, legacyRankPoints,
  rankProgressResponse, rankRef, readRank, writeRank,
} from "../rank/rankStore";
import {DIVISIONS_PER_GRADE, parseRankGradeRows, rankTierCount, requiredPointsForTier, resolveTierIndex} from "../payout";

/** Test-account rank controls. All rank copies and the receipt commit together. */
export const devSetRank = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  if (env !== "test") throw new HttpsError("permission-denied", "devSetRank is available on the test env only.");
  const action = request.data?.action;
  const step = request.data?.step;
  if (!["step", "reset", "promo"].includes(action) ||
      (action === "step" && step !== 1 && step !== -1)) {
    throw new HttpsError("invalid-argument", "Expected step (+1/-1), reset or promo.");
  }
  const [gradeRows, seasonRows] = await Promise.all([
    readSpecRows(env, "RankGrade"), readSpecRows(env, "PassSeason"),
  ]);
  const grades = parseRankGradeRows(gradeRows);
  const season = currentRankSeason(seasonRows, Date.now());
  if (grades.length === 0 || season === null) throw new HttpsError("failed-precondition", "Rank season is unavailable.");

  const txId = clientReceiptId(request.data?.txId, randomUUID());
  let rank = {...rankProgressResponse({seasonId: season.seasonId, points: 0, bestTierIndex: -1, claimed: {}}), tierIndex: 0};
  const result = await mutateSave(env, uid, "devSetRank", {kind: "client", txId}, async (current, transaction) => {
    const reference = rankRef(db, env, uid);
    const payoutRef = db.doc(`envs/${env}/users/${uid}/payoutState/current`);
    const [snapshot, payout] = await transaction.getAll(reference, payoutRef);
    let state = applyTutorialRankEntry(applyRankSeason(readRank(snapshot,
      legacyRankPoints(payout.data(), current), legacyClaimedTiers(current, rankTierCount(grades)), grades),
    season.seasonId, grades), current, grades);
    const tierBefore = resolveTierIndex(state.points, grades);
    if (action === "step") {
      const tier = Math.max(0, Math.min(rankTierCount(grades) - 1, tierBefore + step));
      state = {...state, points: requiredPointsForTier(tier, grades)!, bestTierIndex: Math.max(state.bestTierIndex, tier)};
    } else if (action === "reset") {
      state = {...state, points: grades[0].entryPoints, bestTierIndex: 0, claimed: {}};
    } else {
      const nextGrade = Math.floor(tierBefore / DIVISIONS_PER_GRADE) + 1;
      if (state.points < grades[0].entryPoints || nextGrade >= grades.length) {
        rejectDomain("PromoUnavailable", "Promotion standby requires a ranked account below the top grade.", {uid, env});
      }
      const points = grades[nextGrade].entryPoints - 1;
      state = {...state, points, bestTierIndex: Math.max(state.bestTierIndex, resolveTierIndex(points, grades))};
    }
    rank = {...rankProgressResponse(state), tierIndex: resolveTierIndex(state.points, grades)};
    const now = FieldValue.serverTimestamp();
    writeRank(transaction, reference, state, now, current.profile);
    transaction.set(payoutRef, {currentPoints: state.points, updatedAt: now}, {merge: true});
    return {slots: {rank: {points: state.points, claimedTiers: rank.claimedTierIndexes}}};
  }, (adopted) => ({...adopted, rank}));
  logger.info("devSetRank", {uid, env, action, txId, points: result.rank.points, revision: result.revision});
  return result;
});
