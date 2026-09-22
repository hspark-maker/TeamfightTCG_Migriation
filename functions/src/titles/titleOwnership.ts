import {HttpsError} from "firebase-functions/v2/https";

export const TITLE_EVENTS = ["WinBattle", "DestroyCards", "PlaySynergy", "CompleteAlbum", "WinStreak", "OpenPack"] as const;
export type TitleEvent = typeof TITLE_EVENTS[number] | "";
export interface TitleDefinition {
  id: number;
  titleId: string;
  eventKey: TitleEvent;
  synergyId: string;
  targetCount: number;
  description: string;
}
export interface GrantedTitle {titleId: string; isNew: boolean}

export function parseTitles(rows: readonly Record<string, unknown>[]): TitleDefinition[] {
  if (!rows.length) throw new Error("Title is empty.");
  const ids = new Set<number>();
  const titles = new Set<string>();
  return rows.map((row) => {
    const {id, titleId, eventKey, synergyId, targetCount, description} = row;
    if (typeof id !== "number" || !Number.isSafeInteger(id) || id <= 0 || id > 2147483647 || ids.has(id) ||
        typeof titleId !== "string" || !titleId || titleId.trim() !== titleId || titles.has(titleId)) {
      throw new Error(`Invalid Title: ${String(id)}`);
    }
    if (typeof eventKey !== "string" || typeof synergyId !== "string" ||
        typeof targetCount !== "number" || !Number.isSafeInteger(targetCount) || targetCount > 2147483647 ||
        typeof description !== "string" || !description.trim() || description.trim() !== description ||
        (eventKey === "" ? targetCount !== 0 || synergyId !== "" :
          !TITLE_EVENTS.includes(eventKey as Exclude<TitleEvent, "">) || targetCount < 1 ||
          (eventKey === "PlaySynergy" ? !/^[A-Za-z][A-Za-z0-9_]{0,63}$/.test(synergyId) : synergyId !== ""))) {
      throw new Error(`Invalid Title condition: ${String(id)}`);
    }
    ids.add(id);
    titles.add(titleId);
    return {id, titleId, eventKey: eventKey as TitleEvent, synergyId, targetCount, description};
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
