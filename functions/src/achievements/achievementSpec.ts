import {HttpsError} from "firebase-functions/v2/https";
import {readSpecRows} from "../packs/packSpecReader";
import {parseAchievementCatalog, AchievementDef} from "./achievementCatalog";
import {loadAchievementTitles} from "../titles/achievementTitles";
import type {TitleDefinition} from "../titles/titleOwnership";
import {parseRewardRows} from "../rewardTable";

// A missing/unpublished feature table only disables achievement queries and claims.
export async function readAchievementCatalog(env: string): Promise<AchievementDef[]> {
  return (await readAchievementContext(env)).catalog;
}

export async function readAchievementContext(
  env: string, loadedTitles?: TitleDefinition[],
): Promise<{catalog: AchievementDef[]; titles: TitleDefinition[]}> {
  try {
    const [rows, rewards] = await Promise.all([readSpecRows(env, "Achievement"), readSpecRows(env, "Reward")]);
    const catalog = parseAchievementCatalog(rows, parseRewardRows(rewards));
    const titleRewards = catalog.flatMap((entry) => entry.reward.items ?? []);
    const titles = loadedTitles ?? (titleRewards.length > 0 ? await loadAchievementTitles(env) : []);
    const registered = new Set(titles.map((title) => title.titleId));
    if (titleRewards.some((item) => !registered.has(item.rewardId))) throw new Error("Unknown achievement title reward.");
    return {catalog, titles};
  } catch {
    throw new HttpsError("unavailable", "Achievement definitions are unavailable.");
  }
}
