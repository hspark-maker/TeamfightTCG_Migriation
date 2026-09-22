import {HttpsError} from "firebase-functions/v2/https";

export interface TitleDefinition {
  id: number;
  titleId: string;
}
export interface GrantedTitle {titleId: string; isNew: boolean}

export function parseTitles(rows: readonly Record<string, unknown>[]): TitleDefinition[] {
  if (!rows.length) throw new Error("Title is empty.");
  const ids = new Set<number>();
  const titles = new Set<string>();
  return rows.map((row) => {
    const {id, titleId} = row;
    if (typeof id !== "number" || !Number.isSafeInteger(id) || id <= 0 || id > 2147483647 || ids.has(id) ||
        typeof titleId !== "string" || !titleId || titleId.trim() !== titleId || titles.has(titleId)) {
      throw new Error(`Invalid Title: ${String(id)}`);
    }
    ids.add(id);
    titles.add(titleId);
    return {id, titleId};
  });
}

// The reward owner validates eligibility; this function only grants permanent ownership.
export function grantTitle(
  profile: Record<string, unknown>, titleId: string, catalog: readonly TitleDefinition[],
): {profile: Record<string, unknown>; title: GrantedTitle} {
  if (!catalog.some((entry) => entry.titleId === titleId)) {
    throw new HttpsError("failed-precondition", `Title is not registered: ${titleId}`);
  }
  const raw = profile.ownedTitleIds ?? [];
  if (!Array.isArray(raw) || raw.some((id) => typeof id !== "string" || !id)) {
    throw new HttpsError("failed-precondition", "Invalid ownedTitleIds.");
  }
  const owned = new Set<string>(raw);
  const isNew = !owned.has(titleId);
  owned.add(titleId);
  return {profile: {...profile, ownedTitleIds: [...owned]}, title: {titleId, isNew}};
}
