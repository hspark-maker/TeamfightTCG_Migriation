import {randomInt} from "node:crypto";
import {HttpsError} from "firebase-functions/v2/https";
import {RewardGain, RewardItem, RewardRow, resolveRewards} from "../rewardTable";
import {DrawnCard, drawPack, resolveDropPool, DropRow, RollFn, SNACK_PER_DUPLICATE} from "../packs/packDraw";
import {buildOwnershipSlot, readOwnedIds} from "../packs/packSlots";
import {addSnack, growthSlot, readGrowthEntries} from "../growth/cardGrowth";
import {CardPackRow, readCardPackRow, readDropRows, readRankGradeRows, readSpecRows} from "../packs/packSpecReader";
import {loadCatalogIds} from "../packs/cardCatalog";
import {entryPointsFromRows, gradeOf, parseRequiredGrade, isRanked} from "../packs/rankGrade";
import {SlotPatch} from "../save/saveDocument";

export interface RewardPack {pack: CardPackRow; drops: DropRow[]}
export interface ItemGrantContext {
  catalog: Set<number>;
  grades: Map<number, string>;
  thresholds: number[];
  packs: Map<string, RewardPack>;
  choices: string[];
}

/**
 * UnlockedThemePack은 상점에 진열된 랭크 제한 테마팩 중 현재 구매 가능한 팩이다.
 * @param {string} env 서버 환경
 * @param {RewardItem[]} items 해석한 비재화 보상
 * @return {Promise<ItemGrantContext>} 트랜잭션에서 재사용할 표
 */
export async function loadItemGrantContext(env: string, items: RewardItem[]): Promise<ItemGrantContext> {
  const [catalog, cards, ranks, packs] = await Promise.all([
    loadCatalogIds(env), readSpecRows(env, "Card"), readRankGradeRows(env), readSpecRows(env, "CardPack"),
  ]);
  const thresholds = entryPointsFromRows(ranks);
  if (thresholds === null) throw new HttpsError("failed-precondition", "Reward rank spec is unreadable.");
  const choices = items.some((item) => item.rewardType === "PackChoice") ? packs
    .filter((row) => Number(row.sortOrder) > 0 && String(row.minRankGrade ?? "") !== "" &&
      (env === "test" || row.channel === "Live"))
    .map((row) => String(row.packId)) : [];
  const ids = new Set([...items.filter((item) => item.rewardType === "Pack").map((item) => item.rewardId), ...choices]);
  const prepared = new Map<string, RewardPack>();
  await Promise.all([...ids].map(async (id) => {
    const [pack, drops] = await Promise.all([readCardPackRow(env, id), readDropRows(env, id)]);
    if (pack === null) throw new HttpsError("failed-precondition", `Reward pack not found: ${id}`);
    prepared.set(id, {pack, drops});
  }));
  return {catalog, grades: new Map(cards.map((row) => [Number(row.id), String(row.grade)])),
    thresholds, packs: prepared, choices};
}

export function unlockedRewardPacks(context: ItemGrantContext, points: number): string[] {
  const grade = gradeOf(context.thresholds, points);
  return context.choices.filter((id) => {
    const pack = context.packs.get(id)!.pack;
    const required = parseRequiredGrade(pack.minRankGrade);
    return required !== null && isRanked(context.thresholds, points) && grade >= required;
  });
}

/**
 * 중복 간식은 추첨이 지급한다. 추가 재화만 Reward/CardDuplicate에서 가져온다.
 * @param {DrawnCard[]} cards 이번 추첨 카드
 * @param {ReadonlyMap<number, string>} grades 카드별 등급
 * @param {RewardRow[]} rows 통합 보상 표
 * @return {RewardGain[]} 중복 추가 재화
 */
export function duplicateGains(cards: DrawnCard[], grades: ReadonlyMap<number, string>, rows: RewardRow[]): RewardGain[] {
  return cards.filter((card) => !card.isNew).flatMap((card) => {
    const grade = grades.get(card.cardId);
    if (!grade) throw new Error(`Missing card grade: ${card.cardId}`);
    const reward = resolveRewards(rows, "CardDuplicate", grade);
    if (reward.gains.length === 0 || reward.dropped.length || reward.items.length) {
      throw new Error(`Missing or invalid duplicate reward: ${grade}`);
    }
    return reward.gains;
  });
}

export interface GrantedItems {slots: SlotPatch; cards: DrawnCard[]; currencies: RewardGain[]}

/**
 * 선택·추첨·중복 판정을 기존 소유와 함께 수행한다. 호출자는 낙인/지갑과 같은 트랜잭션에 저장한다.
 * @param {Record<string, unknown>} current 현재 서버 세이브
 * @param {RewardItem[]} items 지급할 비재화 보상
 * @param {ItemGrantContext} context 표 조회 결과
 * @param {RewardRow[]} rewardRows 중복 지급에 사용할 보상 표
 * @param {string} selectedPackId 사용자가 고른 팩
 * @param {number} points 현재 서버 랭크 점수
 * @param {RollFn} roll 추첨 난수원
 * @return {GrantedItems} 원자적으로 반영할 슬롯과 표시 결과
 */
export function grantRewardItems(
  current: Record<string, unknown>, items: RewardItem[], context: ItemGrantContext,
  rewardRows: RewardRow[], selectedPackId: string, points: number, roll: RollFn = randomInt,
): GrantedItems {
  const owned = readOwnedIds(current.ownership);
  const ownedSet = new Set(owned);
  const cards: DrawnCard[] = [];
  const currencies: RewardGain[] = [];
  for (const item of items) {
    if (!Number.isSafeInteger(item.amount) || item.amount <= 0 || item.amount > 100) {
      throw new HttpsError("failed-precondition", "Invalid reward item amount.");
    }
    if (item.rewardType === "Card") {
      const id = Number(item.rewardId);
      if (!context.catalog.has(id)) throw new HttpsError("failed-precondition", `Reward card not found: ${id}`);
      const direct: DrawnCard[] = [];
      for (let i = 0; i < item.amount; i++) {
        const isNew = !ownedSet.has(id);
        ownedSet.add(id);
        direct.push({cardId: id, isNew, snack: isNew ? 0 : SNACK_PER_DUPLICATE});
      }
      cards.push(...direct);
      currencies.push(...duplicateGains(direct, context.grades, rewardRows));
      continue;
    }
    let packId = item.rewardId;
    if (item.rewardType === "PackChoice") {
      if (item.rewardId !== "UnlockedThemePack" || !unlockedRewardPacks(context, points).includes(selectedPackId)) {
        throw new HttpsError("invalid-argument", "Choose an unlocked theme pack.");
      }
      packId = selectedPackId;
    }
    const prepared = context.packs.get(packId);
    if (!prepared) throw new HttpsError("failed-precondition", `Reward pack not found: ${packId}`);
    const {pack, drops} = prepared;
    const pool = resolveDropPool(drops, gradeOf(context.thresholds, points), context.catalog);
    if (!pool.length) throw new HttpsError("failed-precondition", `Reward pack is empty: ${packId}`);
    for (let i = 0; i < item.amount; i++) {
      const drawn = drawPack(pool, pack.drawCount, pack.uniqueDraw, context.catalog, ownedSet, roll);
      cards.push(...drawn);
      if (pack.price > 0) currencies.push(...duplicateGains(drawn, context.grades, rewardRows));
    }
  }
  return {cards, currencies, slots: cards.length ? {
    ownership: buildOwnershipSlot(owned, cards),
    cardGrowth: growthSlot(cards.reduce((entries, card) => addSnack(entries, card.cardId, card.snack),
      readGrowthEntries(current.cardGrowth))),
  } : {}};
}
