import {HttpsError} from "firebase-functions/v2/https";
import {readSpecRows} from "../packs/packSpecReader";
import {parseAchievementCatalog, AchievementDef} from "./achievementCatalog";

// A missing/unpublished feature table only disables achievement queries and claims.
export async function readAchievementCatalog(env: string): Promise<AchievementDef[]> {
  try {
    return parseAchievementCatalog(await readSpecRows(env, "Achievement"));
  } catch {
    throw new HttpsError("unavailable", "Achievement definitions are unavailable.");
  }
}
