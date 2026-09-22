import {HttpsError} from "firebase-functions/v2/https";
import {CURRENCY_KEYS, CURRENCY_MAX, CurrencyKey} from "../currency/currencyKeys";
import {CurrencyGain} from "../currency/wallet";
import {RewardItem} from "../rewardTable";
import {ItemGrantContext} from "../rewards/itemGrant";

export const MAIL_PAGE_SIZE = 50;
export const MAIL_CLAIM_BATCH_SIZE = 20;
const MAX_ATTACHMENTS = 10;
const MAX_GRANTED_CARDS = 100;

export interface MailRewards {currencies: CurrencyGain[]; items: RewardItem[]}
export interface MailContent {title: string; body: string; expiresAtMs: number; rewards: MailRewards}
export interface MailDocument extends MailContent {
  schemaVersion: number;
  createdAtMs: number;
  claimedAtMs: number | null;
  createdBy: string;
  fingerprint: string;
}

export function isMailId(value: unknown): value is string {
  return typeof value === "string" && /^[A-Za-z0-9_-]{1,128}$/.test(value);
}

export function isRecipientUid(value: unknown): value is string {
  return typeof value === "string" && value.length > 0 && value.length <= 128 &&
    !value.includes("/") && value !== "." && value !== ".." && !/^__.*__$/.test(value);
}

export function isMailTime(value: unknown): value is number {
  return typeof value === "number" && Number.isSafeInteger(value) && value > 0 && value <= 8640000000000000;
}

function invalid(message: string): never {
  throw new HttpsError("invalid-argument", message);
}

export function parseMailContent(raw: unknown): MailContent {
  if (!raw || typeof raw !== "object") invalid("Mail content is required.");
  const data = raw as Record<string, unknown>;
  if (typeof data.title !== "string" || !data.title.trim() || data.title.length > 100 ||
      typeof data.body !== "string" || data.body.length > 4000 || !isMailTime(data.expiresAtMs)) {
    invalid("Mail requires a title (1..100), body (0..4000) and expiresAtMs timestamp.");
  }
  const rewards = data.rewards as Record<string, unknown> | undefined;
  if (!rewards || !Array.isArray(rewards.currencies) || !Array.isArray(rewards.items) ||
      rewards.currencies.length + rewards.items.length < 1 ||
      rewards.currencies.length + rewards.items.length > MAX_ATTACHMENTS) {
    invalid(`Mail requires 1..${MAX_ATTACHMENTS} reward attachments.`);
  }
  const currencies: CurrencyGain[] = rewards.currencies.map((rawGain: unknown) => {
    const gain = rawGain as Record<string, unknown> | null;
    if (!gain || !CURRENCY_KEYS.includes(gain.currency as CurrencyKey) ||
        typeof gain.amount !== "number" || !Number.isSafeInteger(gain.amount) || gain.amount <= 0 ||
        gain.amount > CURRENCY_MAX) invalid("Invalid mail currency reward.");
    return {currency: gain.currency as CurrencyKey, amount: gain.amount};
  });
  const items: RewardItem[] = rewards.items.map((rawItem: unknown) => {
    const item = rawItem as Record<string, unknown> | null;
    if (!item || (item.rewardType !== "Card" && item.rewardType !== "Pack") ||
        typeof item.rewardId !== "string" || !item.rewardId.trim() || item.rewardId.length > 128 ||
        typeof item.amount !== "number" || !Number.isSafeInteger(item.amount) ||
        item.amount < 1 || item.amount > 10) invalid("Invalid mail item reward (Card/Pack, amount 1..10).");
    if (item.rewardType === "Card" && !/^[1-9]\d*$/.test(item.rewardId)) invalid("Invalid mail card id.");
    return {rewardType: item.rewardType, rewardId: item.rewardId, amount: item.amount};
  });
  return {title: data.title, body: data.body, expiresAtMs: data.expiresAtMs, rewards: {currencies, items}};
}

// Recheck at claim time too: pack specs can change after delivery. Bound the receipt/response size.
export function validateMailItems(rewards: MailRewards, context: ItemGrantContext): void {
  let cards = 0;
  for (const item of rewards.items) {
    if (item.rewardType === "Card") {
      if (!context.catalog.has(Number(item.rewardId))) {
        throw new HttpsError("failed-precondition", `Mail card not found: ${item.rewardId}`);
      }
      cards += item.amount;
    } else {
      const pack = context.packs.get(item.rewardId);
      if (!pack || !Number.isSafeInteger(pack.pack.drawCount) || pack.pack.drawCount < 1 ||
          !pack.drops.some((drop) => context.catalog.has(drop.cardId))) {
        throw new HttpsError("failed-precondition", `Mail pack is invalid: ${item.rewardId}`);
      }
      cards += pack.pack.drawCount * item.amount;
    }
  }
  if (cards > MAX_GRANTED_CARDS) {
    throw new HttpsError("failed-precondition", `A mail may grant at most ${MAX_GRANTED_CARDS} cards.`);
  }
}

export function mailState(mail: MailDocument, nowMs: number): "Claimable" | "Claimed" | "Expired" {
  if (mail.claimedAtMs !== null) return "Claimed";
  return mail.expiresAtMs <= nowMs ? "Expired" : "Claimable";
}

export function readMail(raw: unknown): MailDocument {
  const mail = raw as MailDocument | undefined;
  if (!mail || mail.schemaVersion !== 1 || !isMailTime(mail.createdAtMs) ||
      (mail.claimedAtMs !== null && !isMailTime(mail.claimedAtMs))) {
    throw new HttpsError("failed-precondition", "Mail document is unreadable.");
  }
  try {
    return {...mail, ...parseMailContent(mail)};
  } catch {
    throw new HttpsError("failed-precondition", "Mail content is unreadable.");
  }
}
