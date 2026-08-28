/**
 * 카드팩 개봉이 바꾸는 세이브 슬롯 3종의 **갱신 후 전체 값** 빌더.
 * 클라 CurrencySaveData · OwnershipSaveData · CardGrowthSaveData 의 모양을 그대로 낸다.
 *
 * 키는 camelCase 다 — 클라가 Newtonsoft(CamelCaseNamingStrategy, ProcessDictionaryKeys=false)로
 * 역직렬화하므로 프로퍼티는 camelCase, 딕셔너리 키(재화 이름·카드 id)는 원형이어야 한다.
 *
 * Firestore 를 모른다(scripts/test-open-pack.js 가 직접 부른다).
 */

import {DrawnCard} from "./packDraw";
import {CURRENCY_KEYS} from "./packSpecReader";

/** 미강화 카드의 레벨. 클라 CardGrowth.BaseLevel 과 같아야 한다. */
export const BASE_LEVEL = 1;

/** 룰 isValidSave 가 재화에 거는 상한. */
const CURRENCY_MAX = 1000000000000;

/** 카드 한 장의 성장 진행도. 클라 CardGrowthEntry 의 쌍둥이. */
export interface GrowthEntry {
  level: number;
  snack: number;
  limitBreak: number;
}

/**
 * 정수로 읽고 못 읽으면 0. 문서가 손상돼도 룰이 거부하는 값(NaN·문자열)을 되쓰지 않게 한다.
 * @param {unknown} value 문서 값
 * @return {number} 정수
 */
function intOf(value: unknown): number {
  const numeric = Number(value);
  return Number.isFinite(numeric) ? Math.trunc(numeric) : 0;
}

/**
 * 재화 잔액을 4키로 정규화한다. 클라 CurrencySaveData.Normalize 가 없는 키를 0으로 채우는 것과 같고,
 * 룰의 `balances.keys().hasOnly([4키])` 때문에 **모르는 키는 버린다**.
 * @param {unknown} currency 문서의 currency 슬롯
 * @return {Record<string, number>} 4키 잔액
 */
export function readBalances(currency: unknown): Record<string, number> {
  const source = (currency as {balances?: Record<string, unknown>} | undefined)?.balances ?? {};
  const balances: Record<string, number> = {};
  for (const key of CURRENCY_KEYS) {
    const value = intOf(source[key]);
    balances[key] = value < 0 ? 0 : Math.min(value, CURRENCY_MAX);
  }
  return balances;
}

/**
 * 소유 카드 id. **기존 순서를 유지**하고 중복·비정수·0 이하를 버린다.
 * @param {unknown} ownership 문서의 ownership 슬롯
 * @return {number[]} 카드 id
 */
export function readOwnedIds(ownership: unknown): number[] {
  const source = (ownership as {cardIds?: unknown[]} | undefined)?.cardIds;
  if (!Array.isArray(source)) return [];

  const seen = new Set<number>();
  const ids: number[] = [];
  for (const raw of source) {
    const id = intOf(raw);
    if (id <= 0 || seen.has(id)) continue;
    seen.add(id);
    ids.push(id);
  }
  return ids;
}

/**
 * 카드 성장 항목. 키는 카드 id 문자열이다.
 * @param {unknown} cardGrowth 문서의 cardGrowth 슬롯
 * @return {Record<string, GrowthEntry>} 성장 항목
 */
export function readGrowthEntries(cardGrowth: unknown): Record<string, GrowthEntry> {
  const source = (cardGrowth as {entries?: Record<string, unknown>} | undefined)?.entries ?? {};
  const entries: Record<string, GrowthEntry> = {};
  for (const [key, raw] of Object.entries(source)) {
    const id = intOf(key);
    if (id <= 0) continue;

    const value = raw as Partial<GrowthEntry> | undefined;
    entries[String(id)] = {
      level: intOf(value?.level),
      snack: intOf(value?.snack),
      limitBreak: intOf(value?.limitBreak),
    };
  }
  return entries;
}

/**
 * 결제 후 재화 슬롯. 잔액 검사는 호출부가 이미 끝냈다.
 * @param {Record<string, number>} balances 결제 전 잔액
 * @param {string} priceCurrency 결제 재화 키
 * @param {number} price 가격
 * @return {object} currency 슬롯 전체 값
 */
export function buildCurrencySlot(
  balances: Record<string, number>,
  priceCurrency: string,
  price: number,
): {balances: Record<string, number>} {
  const next = {...balances};
  const spent = next[priceCurrency] - price;
  next[priceCurrency] = spent < 0 ? 0 : spent;
  return {balances: next};
}

/**
 * 지급 후 소유 슬롯. 신규 카드를 뽑힌 순서로 뒤에 붙인다.
 * @param {number[]} owned 기존 소유 id(순서 유지)
 * @param {DrawnCard[]} drawn 뽑힌 카드
 * @return {object} ownership 슬롯 전체 값
 */
export function buildOwnershipSlot(owned: number[], drawn: DrawnCard[]): {cardIds: number[]} {
  const seen = new Set(owned);
  const cardIds = [...owned];
  for (const card of drawn) {
    if (!card.isNew || seen.has(card.cardId)) continue;
    seen.add(card.cardId);
    cardIds.push(card.cardId);
  }
  return {cardIds};
}

/**
 * 간식 적립 후 성장 슬롯. 기본값뿐인 항목은 버린다
 * — 클라 CardGrowthManager.FlushToData 가 같은 가지치기를 하므로, 안 맞추면 다음 저장에서 문서가 흔들린다.
 * @param {Record<string, GrowthEntry>} entries 기존 항목
 * @param {DrawnCard[]} drawn 뽑힌 카드
 * @return {object} cardGrowth 슬롯 전체 값
 */
export function buildCardGrowthSlot(
  entries: Record<string, GrowthEntry>,
  drawn: DrawnCard[],
): {entries: Record<string, GrowthEntry>} {
  const next: Record<string, GrowthEntry> = {};
  for (const [key, entry] of Object.entries(entries)) next[key] = {...entry};

  for (const card of drawn) {
    if (card.snack <= 0) continue;

    const key = String(card.cardId);
    const entry = next[key] ?? {level: BASE_LEVEL, snack: 0, limitBreak: 0};
    // 음수 간식은 0으로 읽는다(클라 AddSnack 과 같다).
    const current = entry.snack > 0 ? entry.snack : 0;
    entry.snack = current + card.snack;
    next[key] = entry;
  }

  const pruned: Record<string, GrowthEntry> = {};
  for (const [key, entry] of Object.entries(next)) {
    if (entry.level <= BASE_LEVEL && entry.snack <= 0 && entry.limitBreak <= 0) continue;
    pruned[key] = entry;
  }
  return {entries: pruned};
}
