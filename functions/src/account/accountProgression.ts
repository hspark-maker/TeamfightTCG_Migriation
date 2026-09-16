import {randomInt} from "node:crypto";
import {HttpsError} from "firebase-functions/v2/https";
import {RollFn} from "../packs/packDraw";
import {readSpecRows} from "../packs/packSpecReader";
import {parseRewardRows, resolveRewards, RewardRow} from "../rewardTable";
import {GrantedItems, grantRewardItems, ItemGrantContext, loadItemGrantContext} from "../rewards/itemGrant";

export interface AccountLevel {
  id: number;
  requiredExp: number;
  winExp: number;
  loseExp: number;
}

export interface AccountExperience {
  grantedExp: number;
  previousLevel: number;
  level: number;
}

export interface AccountProgressionContext {
  levels: readonly AccountLevel[];
  rewardRows: RewardRow[];
  itemContext: ItemGrantContext | null;
}

export type AccountExperienceGrant = GrantedItems & {accountExperience: AccountExperience};

function nonnegativeInteger(value: unknown, field: string): number {
  const number = typeof value === "number" || (typeof value === "string" && value.trim()) ?
    Number(value) : NaN;
  if (!Number.isSafeInteger(number) || number < 0) throw new Error(`Invalid ${field}.`);
  return number;
}

export function parseAccountLevels(rows: readonly Record<string, unknown>[]): AccountLevel[] {
  if (rows.length === 0) throw new Error("AccountLevel spec is empty.");
  const levels = rows.map((row) => ({
    id: nonnegativeInteger(row.id, "AccountLevel.id"),
    requiredExp: nonnegativeInteger(row.requiredExp, "AccountLevel.requiredExp"),
    winExp: nonnegativeInteger(row.winExp, "AccountLevel.winExp"),
    loseExp: nonnegativeInteger(row.loseExp, "AccountLevel.loseExp"),
  })).sort((a, b) => a.id - b.id);
  for (let index = 0; index < levels.length; index++) {
    const level = levels[index];
    if (level.id !== index + 1 || (index === 0 ? level.requiredExp !== 0 :
      level.requiredExp <= levels[index - 1].requiredExp)) {
      throw new Error("AccountLevel must start at level 1 / XP 0 and increase without gaps.");
    }
  }
  return levels;
}

export function resolveAccountLevel(exp: number, levels: readonly AccountLevel[]): number {
  let level = 1;
  for (const step of levels) {
    if (step.requiredExp > exp) break;
    level = step.id;
  }
  return level;
}

export function validateAccountRewards(levels: readonly AccountLevel[], rows: RewardRow[]): void {
  const maxLevel = levels.length;
  const owners = new Set(rows.filter((row) => row.ownerType === "AccountLevel").map((row) => row.ownerId));
  for (let level = 2; level <= maxLevel; level++) {
    if (!owners.has(String(level))) throw new Error(`Missing AccountLevel reward: ${level}`);
  }
  for (const owner of owners) {
    const level = Number(owner);
    if (!Number.isSafeInteger(level) || level < 2 || level > maxLevel || String(level) !== owner) {
      throw new Error(`Invalid AccountLevel reward owner: ${owner}`);
    }
    const reward = resolveRewards(rows, "AccountLevel", owner);
    if (reward.gains.length + reward.items.length === 0 || reward.dropped.length > 0 || reward.items.some((item) =>
      item.rewardType === "PackChoice" || !Number.isSafeInteger(item.amount) || item.amount > 100)) {
      throw new Error(`Invalid automatic AccountLevel reward: ${owner}`);
    }
  }
}

export async function loadAccountProgression(
  env: string, rewardRows?: RewardRow[],
): Promise<AccountProgressionContext> {
  try {
    const [rawLevels, rows] = await Promise.all([
      readSpecRows(env, "AccountLevel"),
      rewardRows === undefined ? readSpecRows(env, "Reward").then(parseRewardRows) : Promise.resolve(rewardRows),
    ]);
    const levels = parseAccountLevels(rawLevels);
    validateAccountRewards(levels, rows);
    const items = levels.flatMap((level) => resolveRewards(rows, "AccountLevel", String(level.id)).items);
    return {levels, rewardRows: rows, itemContext: items.length ? await loadItemGrantContext(env, items) : null};
  } catch (error) {
    // A partial spec rollout must be retryable and must never consume a reached-level reward.
    throw new HttpsError("unavailable", `Account progression spec is unavailable: ${String(error)}`,
      {reason: "AccountProgressionUnavailable"});
  }
}

function profileOf(current: Record<string, unknown>): Record<string, unknown> {
  const profile = current.profile;
  if (profile !== undefined && (profile === null || typeof profile !== "object" || Array.isArray(profile))) {
    throw new Error("Invalid account profile.");
  }
  return (profile ?? {}) as Record<string, unknown>;
}

export function battleAccountExperience(
  current: Record<string, unknown>, won: boolean, context: AccountProgressionContext,
): number {
  const exp = nonnegativeInteger(profileOf(current).accountExp ?? 0, "profile.accountExp");
  const level = context.levels[resolveAccountLevel(exp, context.levels) - 1];
  return won ? level.winExp : level.loseExp;
}

// No persistence occurs here: the caller commits the returned slots and wallet in one transaction.
export function grantAccountExperience(
  current: Record<string, unknown>, delta: number, context: AccountProgressionContext,
  rankPoints: number, roll: RollFn = randomInt,
): AccountExperienceGrant {
  nonnegativeInteger(delta, "account experience delta");
  const profile = profileOf(current);
  const rawExp = nonnegativeInteger(profile.accountExp ?? 0, "profile.accountExp");
  const cap = context.levels[context.levels.length - 1].requiredExp;
  const exp = Math.min(rawExp, cap);
  const previousLevel = resolveAccountLevel(exp, context.levels);
  // Legacy accounts start at their existing level; past rewards are never granted retroactively.
  const watermark = nonnegativeInteger(profile.accountRewardLevel ?? previousLevel, "profile.accountRewardLevel");
  if (watermark > context.levels.length) throw new Error("Account reward level exceeds the level curve.");
  const grantedExp = Math.min(delta, cap - exp);
  const nextExp = exp + grantedExp;
  const level = resolveAccountLevel(nextExp, context.levels);
  const rewards = context.levels.filter((step) => step.id > Math.max(previousLevel, watermark) && step.id <= level)
    .map((step) => {
      const reward = resolveRewards(context.rewardRows, "AccountLevel", String(step.id));
      if (reward.gains.length + reward.items.length === 0 || reward.dropped.length > 0 ||
          reward.items.some((item) => item.rewardType === "PackChoice")) {
        throw new Error(`Missing or invalid AccountLevel reward: ${step.id}`);
      }
      return reward;
    });
  const items = rewards.flatMap((reward) => reward.items);
  if (items.length > 0 && context.itemContext === null) throw new Error("Missing account reward item context.");
  const itemGrant: GrantedItems = items.length ? grantRewardItems(
    current, items, context.itemContext!, context.rewardRows, "", rankPoints, roll,
  ) : {slots: {}, cards: [], packs: [], currencies: []};
  return {
    ...itemGrant,
    slots: {...itemGrant.slots, profile: {...profile, accountExp: nextExp, accountRewardLevel: Math.max(watermark, level)}},
    currencies: [...rewards.flatMap((reward) => reward.gains), ...itemGrant.currencies],
    accountExperience: {grantedExp, previousLevel, level},
  };
}
