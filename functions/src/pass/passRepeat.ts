import {CurrencyGain} from "../currency/wallet";
import {resolveRewards, RewardRow} from "../rewardTable";
import {PassLevelDef} from "./passSpec";
import {PassState} from "./passStore";

export interface PassRepeatDefinition {
  requiredExp: number;
  maxRequiredExp: number;
  reward: CurrencyGain[];
}

export interface PassRepeatResponse {
  requiredExp: number;
  reward: CurrencyGain[];
  availableClaims: number;
  claimedCount: number;
  progressExp: number;
}

/**
 * A repeat uses the final level's XP interval and the season's authored repeat reward.
 * @param {string} seasonId Active season key.
 * @param {PassLevelDef[]} levels Validated ordered level curve.
 * @param {RewardRow[]} rewards Authored reward rows.
 * @return {PassRepeatDefinition | null} Definition, or null before reward publication.
 */
export function passRepeatDefinition(
  seasonId: string, levels: PassLevelDef[], rewards: RewardRow[],
): PassRepeatDefinition | null {
  const resolved = resolveRewards(rewards, "Pass", `${seasonId}:repeat`);
  // Older season data remains usable until its repeat reward is published.
  if (!resolved.gains.length && !resolved.items.length && !resolved.dropped.length) return null;
  const last = levels[levels.length - 1];
  const requiredExp = last?.requiredExp - (levels[levels.length - 2]?.requiredExp ?? 0);
  if (!Number.isSafeInteger(requiredExp) || requiredExp <= 0 ||
      !resolved.gains.length || resolved.items.length || resolved.dropped.length) {
    throw new Error(`PASS_REPEAT_SPEC_INVALID season=${seasonId}`);
  }
  return {requiredExp, maxRequiredExp: last.requiredExp, reward: resolved.gains};
}

/**
 * Counts only XP earned beyond the last ordinary reward threshold.
 * @param {PassState} state Active season progress.
 * @param {PassRepeatDefinition} definition Valid repeat rule.
 * @return {PassRepeatResponse} Detached repeat reward status.
 */
export function passRepeatResponse(state: PassState, definition: PassRepeatDefinition): PassRepeatResponse {
  const extraExp = Math.max(0, state.exp - definition.maxRequiredExp);
  return {
    requiredExp: definition.requiredExp,
    reward: definition.reward.map((gain) => ({...gain})),
    availableClaims: Math.max(0, Math.floor(extraExp / definition.requiredExp) - state.repeatClaimed),
    claimedCount: state.repeatClaimed,
    progressExp: extraExp % definition.requiredExp,
  };
}
