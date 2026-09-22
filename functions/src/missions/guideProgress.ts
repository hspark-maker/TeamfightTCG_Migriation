import {readOwnedIds} from "../packs/packSlots";
import {BASE_LEVEL, readGrowthEntries, levelOfCard} from "../growth/cardGrowth";
import {MissionDef} from "./catalog";

const STARTER_CARD_IDS = [3, 4, 1] as const;

/**
 * 서버 랭크의 최고 티어를 영구 가이드 달성으로 보존한다.
 * @param {Record} rank 서버 rank/current 또는 정산 후 랭크
 * @param {Record} previous 기존 진행도
 * @return {Record} 랭크 달성을 합친 진행도
 */
export function evaluateGuideRankProgress(
  rank: {bestTierIndex?: unknown} | undefined, previous: Record<string, number> = {},
): Record<string, number> {
  const key = "guide.Guide.Bronze2Reached";
  const reached = typeof rank?.bestTierIndex === "number" && rank.bestTierIndex >= 1;
  return {...previous, [key]: Math.max(previous[key] ?? 0, reached ? 1 : 0)};
}

/**
 * 서버 세이브에서 판정한다. 잠긴 단계도 기록하고, 덱을 바꿔도 최대 달성값은 유지한다.
 * @param {Record<string, unknown>} current 서버 세이브
 * @param {Record<string, unknown>[]} cards 카드 표
 * @param {Array} catalog 이번 요청의 정의 목록
 * @param {Record<string, number>} previous 영구 진행도를 포함한 기존 카운터
 * @param {Record} rank 서버 랭크 상태
 * @return {Record<string, number>} 최고 달성값을 합친 카운터
 */
export function evaluateGuideProgress(
  current: Record<string, unknown>, cards: Record<string, unknown>[], catalog: readonly MissionDef[],
  previous: Record<string, number> = {},
  rank?: {bestTierIndex?: unknown},
): Record<string, number> {
  const result = evaluateGuideRankProgress(rank, previous);
  const owned = new Set(readOwnedIds(current.ownership));
  const growth = readGrowthEntries(current.cardGrowth);
  const star = (id: number) => Math.max(0, levelOfCard(growth, id) - BASE_LEVEL);
  const deck = current.deck as {slots?: {cardIds?: number[]}[]; selectedSlot?: number} | undefined;
  // 직접 저장되는 슬롯 내부까지 보안 규칙이 검증하지는 않는다. 잘못된 덱 때문에
  // 가이드를 함께 계산하는 팩·강화 명령 전체가 실패하지 않도록 유효한 배열만 해석한다.
  const slots = Array.isArray(deck?.slots) ? deck.slots : [];
  const validDecks = slots.map((slot) => Array.isArray(slot?.cardIds) ? slot.cardIds : []).filter((ids) =>
    ids.length === 6 && new Set(ids).size === 6 && ids.every((id) => Number.isInteger(id) && owned.has(id)));
  const selectedIds = slots[deck?.selectedSlot ?? 0]?.cardIds ?? [];
  const selected = validDecks.includes(selectedIds) ? selectedIds : [];
  const caretaker = new Set(cards.filter((row) => String(row.synergies).split("/").includes("Data_Synergy_Caretaker"))
    .map((row) => Number(row.id)));
  const tracePair = cards.filter((row) => ["Data_Card_Nightchestnut", "Data_Card_MushroomCat"].includes(String(row.name)))
    .map((row) => Number(row.id));
  const adventure = current.adventure as {clearedNodeIds?: string[]} | undefined;
  const cleared = new Set(Array.isArray(adventure?.clearedNodeIds) ? adventure.clearedNodeIds : []);
  for (const mission of catalog.filter((entry) => entry.period === "guide")) {
    let progress = 0;
    const event = mission.event;
    if (event === "Guide.EnhanceCompleted") {
      progress = [...owned].some((id) => (growth[String(id)]?.shardProgress ?? 0) > 0 ||
        levelOfCard(growth, id) > BASE_LEVEL) ? 1 : 0;
    } else if (event === "Guide.EvolveCompleted") {
      progress = [...owned].some((id) => star(id) >= 2) ? 1 : 0;
    } else if (event === "Guide.StarterCardsAtStar1") {
      progress = STARTER_CARD_IDS.filter((id) => owned.has(id) && star(id) >= 1).length;
    } else if (event === "Guide.StarterCardsAtStar2") {
      // 기존 이벤트 키와 최고 진행도는 보존하되, 돌보미라면 어떤 카드든 인정한다.
      progress = [...owned].filter((id) => caretaker.has(id) && star(id) >= 2).length;
    } else if (event === "Guide.DeckSaved6") progress = validDecks.length ? 1 : 0;
    else if (/^Guide\.AdventureNode\d+$/.test(event)) {
      progress = cleared.has("node_" + event.slice("Guide.AdventureNode".length)) ? 1 : 0;
    } else if (event === "Guide.AdventureChapter01") progress = cleared.has("node_06") ? 1 : 0;
    else if (event === "Guide.CaretakerCardsAtStar1") {
      progress = [...owned].filter((id) => caretaker.has(id) && star(id) >= 1).length;
    } else if (event === "Guide.CaretakerDeckAtStar2") {
      progress = Math.max(0, ...validDecks.map((ids) => ids.filter((id) => caretaker.has(id) && star(id) >= 2).length));
    } else if (event === "Guide.CaretakerTraceDeck") {
      progress = validDecks.some((ids) => ids.filter((id) => caretaker.has(id) && star(id) >= 2).length >= 3 &&
        tracePair.length === 2 && tracePair.every((id) => ids.includes(id) && star(id) >= 2)) ? 2 : 0;
    } else if (event === "Guide.DeckCardsAtStar3") progress = selected.filter((id) => star(id) >= 3).length;
    else if (event === "Guide.DeckCardsAtStar2") progress = selected.filter((id) => star(id) >= 2).length;
    const key = "guide." + event;
    result[key] = Math.max(result[key] ?? 0, progress);
  }
  return result;
}
