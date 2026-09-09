import {HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {loadItemGrantContext, unlockedRewardPacks} from "../rewards/itemGrant";
import {rankRef} from "../rank/rankStore";
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
    const choiceItems = rewardSpec.filter((row) => row.ownerType === "Pass" && row.rewardType === "PackChoice")
      .map((row) => ({rewardType: "PackChoice" as const, rewardId: row.rewardId, amount: row.amount}));
    const choices = choiceItems.length ? await loadItemGrantContext(env, choiceItems) : null;
    const rankSnapshot = choices === null ? null : await rankRef(db, env, uid).get();
    const currentLevel = passLevelOf(state.exp, levels);
    const next = levels.find((entry) => entry.level > currentLevel);

    return {
      season,
      progress: passProgressResponse(state),
      currentLevel,
      nextRequiredExp: next?.requiredExp ?? null,
      packChoices: choices === null ? [] : unlockedRewardPacks(choices, Number(rankSnapshot?.data()?.points ?? 0)),
      levels: levels.map((entry) => ({
        level: entry.level,
        requiredExp: entry.requiredExp,
        items: resolveRewards(rewardSpec, "Pass", passRewardOwnerId(season.seasonId, entry.level)).items,
        reward: resolveRewards(
          rewardSpec, "Pass", passRewardOwnerId(season.seasonId, entry.level)).gains,
      })),
    };
  } catch (error) {
    if (error instanceof HttpsError) throw error;
    throw new HttpsError("failed-precondition", `PASS_SPEC_INVALID ${String(error)}`);
  }
});
