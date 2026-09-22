import {HttpsError} from "firebase-functions/v2/https";
import {readSpecRows} from "../specs/specBlobReader";

export type CosmeticType = "Avatar" | "Frame" | "Emote";
export interface CosmeticItem {id: number; itemType: CosmeticType; itemId: string; defaultOwned: boolean}
export interface CosmeticGrant {itemType: CosmeticType; itemId: string; isNew: boolean}
const FIELDS = {Avatar: "ownedAvatarIds", Frame: "ownedFrameIds", Emote: "ownedEmoteIds"} as const;

export function isCosmeticType(value: string): value is CosmeticType {
  return value === "Avatar" || value === "Frame" || value === "Emote";
}

export function parseCosmeticItems(rows: Record<string, unknown>[]): CosmeticItem[] {
  if (!rows.length) throw new Error("CosmeticItem is empty.");
  const ids = new Set<number>();
  const keys = new Set<string>();
  return rows.map((row) => {
    const id = row.id;
    const itemType = String(row.itemType ?? "");
    const itemId = String(row.itemId ?? "");
    const key = `${itemType}:${itemId}`;
    if (typeof id !== "number" || !Number.isSafeInteger(id) || id <= 0 || id > 2147483647 || ids.has(id) ||
        !isCosmeticType(itemType) || !itemId || itemId.trim() !== itemId || keys.has(key) ||
        (row.defaultOwned !== 0 && row.defaultOwned !== 1) ||
        (itemType === "Emote" && (!/^[1-9][0-9]*$/.test(itemId) || !Number.isSafeInteger(Number(itemId)) || Number(itemId) > 2147483647))) {
      throw new Error(`Invalid CosmeticItem: ${String(id)} / ${key}`);
    }
    ids.add(id);
    keys.add(key);
    return {id, itemType, itemId, defaultOwned: row.defaultOwned === 1};
  });
}

export async function loadCosmeticItems(env: string): Promise<CosmeticItem[]> {
  try {
    return parseCosmeticItems(await readSpecRows(env, "CosmeticItem"));
  } catch (error) {
    throw new HttpsError("unavailable", `Cosmetic catalog is unavailable: ${String(error)}`,
      {reason: "CosmeticCatalogUnavailable"});
  }
}

function owned(profile: Record<string, unknown>, type: CosmeticType): (string | number)[] {
  const raw = profile[FIELDS[type]];
  if (raw === undefined || raw === null) return [];
  if (!Array.isArray(raw) || raw.some((id) => type === "Emote" ?
    !Number.isSafeInteger(id) || id <= 0 || id > 2147483647 : typeof id !== "string" || !id)) {
    throw new Error(`Invalid ${FIELDS[type]}`);
  }
  return [...new Set(raw)];
}

export function ensureCosmeticOwnership(
  profile: Record<string, unknown>, catalog: readonly CosmeticItem[],
): Record<string, unknown> {
  const next = {...profile};
  for (const type of ["Avatar", "Frame", "Emote"] as const) {
    const values = new Set(owned(profile, type));
    for (const item of catalog) {
      if (item.itemType === type && item.defaultOwned) values.add(type === "Emote" ? Number(item.itemId) : item.itemId);
    }
    next[FIELDS[type]] = [...values];
  }
  return next;
}

export function isCosmeticOwned(profile: Record<string, unknown>, type: CosmeticType, id: string): boolean {
  return owned(profile, type).includes(type === "Emote" ? Number(id) : id);
}

// Call after receipt replay and inside the purchase transaction, before debiting the wallet.
export function assertCosmeticPurchasable(
  profile: Record<string, unknown>, type: CosmeticType, id: string, catalog: readonly CosmeticItem[],
): void {
  if (!catalog.some((item) => item.itemType === type && item.itemId === id)) {
    throw new HttpsError("failed-precondition", "Cosmetic item is not registered.");
  }
  if (isCosmeticOwned(ensureCosmeticOwnership(profile, catalog), type, id)) {
    throw new HttpsError("failed-precondition", "Cosmetic item is already owned.", {reason: "AlreadyOwned"});
  }
}

export function grantCosmetic(
  profile: Record<string, unknown>, type: CosmeticType, id: string, catalog: readonly CosmeticItem[],
): {profile: Record<string, unknown>; cosmetic: CosmeticGrant} {
  if (!catalog.some((item) => item.itemType === type && item.itemId === id)) {
    throw new HttpsError("failed-precondition", `Cosmetic item is not registered: ${type}/${id}`);
  }
  const next = ensureCosmeticOwnership(profile, catalog);
  const isNew = !isCosmeticOwned(next, type, id);
  if (isNew) (next[FIELDS[type]] as (number | string)[]).push(type === "Emote" ? Number(id) : id);
  return {profile: next, cosmetic: {itemType: type, itemId: id, isNew}};
}
