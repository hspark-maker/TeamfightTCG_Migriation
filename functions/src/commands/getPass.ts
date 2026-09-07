import {HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {readSpecRows} from "../packs/packSpecReader";
import {
  currentPassSeason,
  parsePassLevels,
  parsePassSeasons,
  passLevelOf,
  passRewardOwnerId,
} from "../pass/passSpec";
import {
  applyPassSeason,
  passProgressResponse,
  passRef,
  readPass,
} from "../pass/passStore";
import {parseRewardRows, resolveRewards} from "../rewardTable";
import {isKnownEnv, requireUid} from "../save/saveDocument";

/** Returns the active free battle-pass season, curve, rewards, and player progress. */
export const getPass = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  if (!isKnownEnv(env)) throw new HttpsError("invalid-argument", `Unknown env: ${env}`);

  const nowMs = Date.now();
  try {
    const [seasonRows, levelRows, rewardRows, snapshot] = await Promise.all([
      readSpecRows(env, "PassSeason"),
      readSpecRows(env, "PassLevel"),
      readSpecRows(env, "Reward"),
      passRef(db, env, uid).get(),
    ]);
    const season = currentPassSeason(parsePassSeasons(seasonRows), nowMs);
    if (season === null) {
      return {season: null, progress: null, currentLevel: 0, nextRequiredExp: null, levels: []};
    }

    const levels = parsePassLevels(levelRows, season);
    const state = applyPassSeason(readPass(snapshot), season.seasonId);
    const rewardSpec = parseRewardRows(rewardRows);
    const currentLevel = passLevelOf(state.exp, levels);
    const next = levels.find((entry) => entry.level > currentLevel);

    return {
      season,
      progress: passProgressResponse(state),
      currentLevel,
      nextRequiredExp: next?.requiredExp ?? null,
      levels: levels.map((entry) => ({
        level: entry.level,
        requiredExp: entry.requiredExp,
        reward: resolveRewards(
          rewardSpec, "Pass", passRewardOwnerId(season.seasonId, entry.level)).gains,
      })),
    };
  } catch (error) {
    if (error instanceof HttpsError) throw error;
    throw new HttpsError("failed-precondition", `PASS_SPEC_INVALID ${String(error)}`);
  }
});
