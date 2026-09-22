import {HttpsError} from "firebase-functions/v2/https";
import type {AchievementDef} from "../achievements/achievementCatalog";
import {readOptionalSpecRows} from "../specs/specBlobReader";
import {GrantedTitle, grantTitle, parseTitles, TitleDefinition} from "./titleOwnership";

export async function loadAchievementTitles(env: string): Promise<TitleDefinition[]> {
  try {
    const rows = await readOptionalSpecRows(env, "Title");
    return rows === null ? [] : parseTitles(rows);
  } catch {
    throw new HttpsError("unavailable", "Achievement title definitions are unavailable.",
      {reason: "TitleConditionsUnavailable"});
  }
}

export function titleConditionDefinitions(
  catalog: TitleDefinition[], achievements: AchievementDef[],
): {titleId: string; description: string}[] {
  return catalog.flatMap(({titleId}) => {
    const owners = achievements.filter((entry) => entry.reward.items?.some((item) => item.rewardId === titleId));
    if (owners.length === 0) return [];
    const conditions = [...new Set(owners.map((entry) => entry.description || entry.title))];
    return [{titleId, description: `${conditions.join(" / ")}\n해당 업적 보상을 수령하세요.`}];
  });
}

// Eligibility and the permanent claim marker belong to claimAchievement's transaction.
export function grantAchievementTitles(
  profile: Record<string, unknown>, definition: AchievementDef, catalog: TitleDefinition[],
): {profile: Record<string, unknown>; titles: GrantedTitle[]} {
  let updated = profile;
  const titles: GrantedTitle[] = [];
  for (const item of definition.reward.items ?? []) {
    const granted = grantTitle(updated, item.rewardId, catalog);
    updated = granted.profile;
    titles.push(granted.title);
  }
  return {profile: updated, titles};
}
