import {readOwnedIds} from "../packs/packSlots";
import {readGrowthEntries, levelOfCard} from "../growth/cardGrowth";
import {MissionDef} from "./catalog";

/**
 * 서버 세이브에서 판정한다. 잠긴 단계도 기록하고, 덱을 바꿔도 최대 달성값은 유지한다.
 * @param {Record<string, unknown>} current 서버 세이브
 * @param {Record<string, unknown>[]} cards 카드 표
 * @param {Array} catalog 이번 요청의 정의 목록
 * @param {Record<string, number>} previous 영구 진행도를 포함한 기존 카운터
 * @return {Record<string, number>} 최고 달성값을 합친 카운터
 */
export function evaluateGuideProgress(
  current: Record<string, unknown>, cards: Record<string, unknown>[], catalog: readonly MissionDef[],
  previous: Record<string, number> = {},
): Record<string, number> {
  const result = {...previous};
  const owned = new Set(readOwnedIds(current.ownership));
  const growth = readGrowthEntries(current.cardGrowth);
  const star = (id: number) => Math.max(0, levelOfCard(growth, id) - 1);
  const deck = current.deck as {slots?: {cardIds?: number[]}[]; selectedSlot?: number} | undefined;
  const validDecks = (deck?.slots ?? []).map((slot) => slot.cardIds ?? []).filter((ids) =>
    ids.length === 6 && new Set(ids).size === 6 && ids.every((id) => owned.has(id)));
  const selectedIds = deck?.slots?.[deck.selectedSlot ?? 0]?.cardIds ?? [];
  const selected = validDecks.includes(selectedIds) ? selectedIds : [];
  const caretaker = new Set(cards.filter((row) => String(row.synergies).split("/").includes("Data_Synergy_Caretaker"))
    .map((row) => Number(row.id)));
  const tracePair = cards.filter((row) => ["Data_Card_Nightchestnut", "Data_Card_MushroomCat"].includes(String(row.name)))
    .map((row) => Number(row.id));
  const adventure = current.adventure as {clearedNodeIds?: string[]} | undefined;
  const cleared = new Set(adventure?.clearedNodeIds ?? []);
  for (const mission of catalog.filter((entry) => entry.period === "guide")) {
    let progress = 0;
    const event = mission.event;
    if (event === "Guide.DeckSaved6") progress = validDecks.length ? 1 : 0;
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
