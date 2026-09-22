import type {CurrencyGain} from "../currency/wallet";
import {CURRENCY_MAX} from "../currency/currencyKeys";
import {resolveRewards, RewardRow} from "../rewardTable";

export const ACHIEVEMENT_EVENTS = [
  "WinBattle", "DestroyCards", "PlaySynergy", "CompleteAlbum", "WinStreak", "OpenPack",
] as const;
export type AchievementEvent = typeof ACHIEVEMENT_EVENTS[number];
export interface AchievementDef {
  id: string;
  groupId: string;
  stage: number;
  event: AchievementEvent;
  synergyId: string;
  target: number;
  title: string;
  description: string;
  sortOrder: number;
  reward: {currencies: CurrencyGain[]; items?: {rewardType: "Title"; rewardId: string; amount: number}[]};
}

const ID = /^[A-Za-z0-9][A-Za-z0-9_.-]{0,95}$/;
export const SYNERGY_ID = /^[A-Za-z][A-Za-z0-9_]{0,63}$/;
export function isAchievementId(value: unknown): value is string {
  return typeof value === "string" && ID.test(value);
}

// Conditions live in Achievement; every reward is authored in Reward by the permanent achievement ID.
export function parseAchievementCatalog(rows: readonly Record<string, unknown>[], rewards: RewardRow[]): AchievementDef[] {
  const ids = new Set<string>();
  const groups = new Map<string, AchievementDef[]>();
  const result: AchievementDef[] = [];
  for (const row of rows) {
    if (Number(row.enabled) === 0) continue;
    const id = String(row.achievementId ?? "");
    const groupId = String(row.groupId ?? "");
    const event = String(row.eventKey ?? "") as AchievementEvent;
    const synergyId = String(row.synergyId ?? "");
    const stage = Number(row.stage);
    const target = Number(row.targetCount);
    const sortOrder = Number(row.sortOrder);
    if (!isAchievementId(id) || !isAchievementId(groupId) || ids.has(id) ||
        !ACHIEVEMENT_EVENTS.includes(event) || !Number.isSafeInteger(stage) || stage < 1 ||
        !Number.isSafeInteger(target) || target < 1 || !Number.isSafeInteger(sortOrder) ||
        (event === "PlaySynergy" ? !SYNERGY_ID.test(synergyId) : synergyId !== "") ||
        typeof row.title !== "string" || row.title.trim().length === 0) {
      throw new Error(`Invalid Achievement row: ${id || row.id}`);
    }
    const reward = resolveRewards(rewards, "Achievement", id);
    if (reward.dropped.length > 0 || reward.gains.length + reward.items.length === 0 ||
        reward.gains.some((gain) => !Number.isSafeInteger(gain.amount) || gain.amount > CURRENCY_MAX) ||
        reward.items.some((item) => item.rewardType !== "Title")) {
      throw new Error(`Missing or invalid Achievement reward: ${id}`);
    }
    const definition: AchievementDef = {
      id, groupId, stage, event, synergyId, target, title: row.title,
      description: String(row.description ?? ""), sortOrder,
      reward: {currencies: reward.gains,
        items: reward.items.map((item) => ({rewardType: "Title", rewardId: item.rewardId, amount: item.amount}))},
    };
    ids.add(id);
    result.push(definition);
    const group = groups.get(groupId) ?? [];
    group.push(definition);
    groups.set(groupId, group);
  }
  for (const [id, entries] of groups) {
    entries.sort((a, b) => a.stage - b.stage);
    if (entries.some((entry, i) => entry.stage !== i + 1 || entry.event !== entries[0].event ||
        entry.synergyId !== entries[0].synergyId || (i > 0 && entry.target <= entries[i - 1].target))) {
      throw new Error(`Invalid Achievement stages: ${id}`);
    }
  }
  return result.sort((a, b) => a.sortOrder - b.sortOrder || a.stage - b.stage || a.id.localeCompare(b.id));
}

export function achievementProgressKey(definition: Pick<AchievementDef, "event" | "synergyId">): string {
  return definition.event === "PlaySynergy" ? `PlaySynergy:${definition.synergyId}` : definition.event;
}
