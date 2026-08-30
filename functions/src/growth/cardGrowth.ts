/**
 * 카드 성장 진행도(강화 레벨 · 먹이 · 한계돌파)의 슬롯 코덱과 먹이 소비 판정.
 * 클라 CardGrowthSaveData / CardGrowthManager 의 쌍둥이다. 순수(Firestore·HttpsError 모름).
 *
 * 먹이는 그 카드에만 쓰는 재료라 전역 잔액이 아니라 카드 id 로 갈린 항목에 얹혀 있다
 * — 그래서 currency/wallet 과 합치지 않는다(키 집합·상한·슬롯 모양이 전부 다르다).
 *
 * packs/ 를 import 하지 않는다. 간식 적립 입력은 DrawnCard 타입이 아니라 카드 id·수량으로 받는다.
 */

import {intOf} from "../save/saveValues";

/** 미강화 카드의 레벨. 클라 CardGrowth.BaseLevel 과 같아야 한다. */
export const BASE_LEVEL = 1;

/** 먹이 보유 상한. 클라 CardGrowthEntry.Snack 이 int 이고 AddSnack 이 여기서 자른다. */
export const SNACK_MAX = 2147483647;

/** 카드 한 장의 성장 진행도. 클라 CardGrowthEntry 의 쌍둥이. */
export interface GrowthEntry {
  level: number;
  snack: number;
  limitBreak: number;
}

/** 카드 id 문자열 → 진행도. */
export type GrowthEntries = Record<string, GrowthEntry>;

/**
 * 카드 성장 항목을 읽는다. 키는 카드 id 문자열이다.
 * @param {unknown} cardGrowth 문서의 cardGrowth 슬롯
 * @return {GrowthEntries} 성장 항목
 */
export function readGrowthEntries(cardGrowth: unknown): GrowthEntries {
  const source = (cardGrowth as {entries?: Record<string, unknown>} | undefined)?.entries ?? {};
  const entries: GrowthEntries = {};
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
 * 항목 하나를 고쳐 새 맵을 낸다. 없으면 미강화 기본값으로 신설한다.
 * @param {GrowthEntries} entries 기존 항목
 * @param {number} cardId 카드 id
 * @param {Function} edit 항목을 제자리에서 고치는 함수
 * @return {GrowthEntries} 갱신된 항목
 */
function withEntry(
  entries: GrowthEntries,
  cardId: number,
  edit: (entry: GrowthEntry) => void,
): GrowthEntries {
  const next: GrowthEntries = {};
  for (const [key, entry] of Object.entries(entries)) next[key] = {...entry};

  const key = String(cardId);
  const entry = next[key] ?? {level: BASE_LEVEL, snack: 0, limitBreak: 0};
  edit(entry);
  next[key] = entry;
  return next;
}

/**
 * 이 카드의 현재 강화 레벨(기록이 없으면 미강화). 바닥 아래 값은 미강화로 읽는다
 * — 레벨을 0부터 세던 시절의 세이브가 그렇다(클라 CardGrowthManager.LevelOf 와 같은 정규화).
 * @param {GrowthEntries} entries 성장 항목
 * @param {number} cardId 카드 id
 * @return {number} 강화 레벨
 */
export function levelOfCard(entries: GrowthEntries, cardId: number): number {
  const level = entries[String(cardId)]?.level ?? BASE_LEVEL;
  return level < BASE_LEVEL ? BASE_LEVEL : level;
}

/**
 * 강화 성공을 반영한다. 먹이·한계돌파는 그대로 둔다 — 슬롯 **전체 값**을 되쓰므로
 * 여기서 흘리면 그 카드의 나머지 진행도가 지워진다.
 * @param {GrowthEntries} entries 기존 항목
 * @param {number} cardId 카드 id
 * @param {number} level 도달 레벨
 * @return {GrowthEntries} 갱신된 항목
 */
export function applyEnhanceLevel(entries: GrowthEntries, cardId: number, level: number): GrowthEntries {
  return withEntry(entries, cardId, (entry) => {
    entry.level = level < BASE_LEVEL ? BASE_LEVEL : level;
  });
}

/**
 * 보유 먹이. 음수 세이브는 0으로 읽는다(클라 SnackOf 와 같다).
 * @param {GrowthEntries} entries 성장 항목
 * @param {number} cardId 카드 id
 * @return {number} 보유량
 */
function snackOf(entries: GrowthEntries, cardId: number): number {
  const snack = entries[String(cardId)]?.snack ?? 0;
  return snack > 0 ? snack : 0;
}

/**
 * 먹이를 적립한다. 신규 카드에는 안 붙는다(수량 0 이하는 무변경).
 * @param {GrowthEntries} entries 기존 항목
 * @param {number} cardId 카드 id
 * @param {number} amount 적립량
 * @return {GrowthEntries} 갱신된 항목
 */
export function addSnack(entries: GrowthEntries, cardId: number, amount: number): GrowthEntries {
  if (cardId <= 0 || amount <= 0) return entries;

  const current = snackOf(entries, cardId);
  return withEntry(entries, cardId, (entry) => {
    entry.snack = Math.min(current + amount, SNACK_MAX);
  });
}

/**
 * 먹이가 충분한가.
 * @param {GrowthEntries} entries 성장 항목
 * @param {number} cardId 카드 id
 * @param {number} cost 소모량
 * @return {boolean} 보유량이 소모량 이상이면 true
 */
export function canAffordSnack(entries: GrowthEntries, cardId: number, cost: number): boolean {
  return snackOf(entries, cardId) >= cost;
}

/**
 * 먹이를 차감한다. 여력 검사는 호출부가 이미 끝냈다 — 여기서는 하한 0 으로만 자른다.
 * @param {GrowthEntries} entries 기존 항목
 * @param {number} cardId 카드 id
 * @param {number} cost 소모량
 * @return {GrowthEntries} 갱신된 항목
 */
export function spendSnack(entries: GrowthEntries, cardId: number, cost: number): GrowthEntries {
  const left = snackOf(entries, cardId) - cost;
  return withEntry(entries, cardId, (entry) => {
    entry.snack = left < 0 ? 0 : left;
  });
}

/**
 * 한계돌파 1단계. **먹이 차감과 단계 증가는 반드시 함께 간다**
 * — 클라 TryLimitBreak 이 한 몸으로 저장하므로 갈라 두면 반쪽 문서가 생긴다.
 * @param {GrowthEntries} entries 기존 항목
 * @param {number} cardId 카드 id
 * @param {number} stage 도달 단계
 * @param {number} snackCost 소모 먹이
 * @return {GrowthEntries} 갱신된 항목
 */
export function applyLimitBreak(
  entries: GrowthEntries,
  cardId: number,
  stage: number,
  snackCost: number,
): GrowthEntries {
  return withEntry(spendSnack(entries, cardId, snackCost), cardId, (entry) => {
    entry.limitBreak = stage;
  });
}

/**
 * 세이브의 cardGrowth 슬롯 **전체 값**. 기본값뿐인 항목은 버린다
 * — 클라 CardGrowthManager.FlushToData 가 같은 가지치기를 하므로, 안 맞추면 다음 저장에서 문서가 흔들린다.
 * @param {GrowthEntries} entries 성장 항목
 * @return {object} cardGrowth 슬롯
 */
export function growthSlot(entries: GrowthEntries): {entries: GrowthEntries} {
  const pruned: GrowthEntries = {};
  for (const [key, entry] of Object.entries(entries)) {
    if (entry.level <= BASE_LEVEL && entry.snack <= 0 && entry.limitBreak <= 0) continue;
    pruned[key] = {...entry};
  }
  return {entries: pruned};
}
