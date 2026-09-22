import {HttpsError} from "firebase-functions/v2/https";
import {AlbumEntryRow, AlbumThemeRow, parseAlbumEntryRows, parseAlbumThemeRows} from "../completionTable";
import {readOptionalSpecRows, readSpecRows} from "../specs/specBlobReader";
import {PlayerStatistics, statisticsProgress} from "../statistics/playerStatistics";
import {GrantedTitle, grantTitle, parseTitles, TitleDefinition} from "./titleOwnership";

export interface AutomaticTitleContext {
  catalog: TitleDefinition[];
  entries: AlbumEntryRow[];
  themes: AlbumThemeRow[];
}

export const AUTOMATIC_TITLE_COMMANDS = new Set([
  "openPack", "claimAttendance", "claimMission", "claimReward", "claimPassReward", "claimBattleExperience",
  "spinRoulette", "craftCard", "grantTutorialCards", "claimAchievement",
]);

export async function loadAutomaticTitles(env: string): Promise<AutomaticTitleContext | null> {
  try {
    const rows = await readOptionalSpecRows(env, "Title");
    if (rows === null) return null;
    const catalog = parseTitles(rows);
    const needsAlbums = catalog.some((title) => title.eventKey === "CompleteAlbum");
    const [entries, themes] = needsAlbums ? await Promise.all([
      readSpecRows(env, "AlbumEntry"), readSpecRows(env, "AlbumThemeInfo"),
    ]) : [[], []];
    return {catalog, entries: parseAlbumEntryRows(entries), themes: parseAlbumThemeRows(themes)};
  } catch {
    throw new HttpsError("unavailable", "Automatic title definitions are unavailable.",
      {reason: "TitleConditionsUnavailable"});
  }
}

export function titleConditionDefinitions(context: AutomaticTitleContext | null): {
  titleId: string; description: string;
}[] {
  return context?.catalog.map(({titleId, description}) => ({titleId, description})) ?? [];
}

// Permanent ownership is the grant marker. Goals and descriptions come from the independent Title table.
export function grantAutomaticTitles(
  profile: Record<string, unknown>, statistics: PlayerStatistics, context: AutomaticTitleContext,
): {profile: Record<string, unknown>; titles: GrantedTitle[]} {
  const progress = statisticsProgress(statistics);
  const titles: GrantedTitle[] = [];
  let updated = profile;
  for (const title of context.catalog) {
    if (title.eventKey === "") continue;
    const key = title.eventKey === "PlaySynergy" ? "PlaySynergy:" + title.synergyId : title.eventKey;
    if ((progress[key] ?? 0) < title.targetCount) continue;
    const grant = grantTitle(updated, title.titleId, context.catalog);
    updated = grant.profile;
    if (grant.title.isNew) titles.push(grant.title);
  }
  return {profile: updated, titles};
}
