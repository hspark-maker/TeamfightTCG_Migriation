import {AiCardGrowth, parseAiCardGrowth} from "../deckValidation";
import {AiDeckRow} from "./aiDeckDraw";
import {computeRankPayout, RankGradeRow} from "../payout";

export type RankAiBattleKind = "Normal" | "DivisionFinal" | "GradeFinal";
export type RankAiEncounter = {
  id: number;
  tierIndex: number;
  battleKind: RankAiBattleKind;
  deckId: string;
  cardGrowth: AiCardGrowth[];
  highlightCardId: number;
};

// 실제 점수 정산과 같은 승급 대기 판정이다. 경계에 닿기 전 일반전이나
// 등급 내부 단계 이동은 승급전이 아니며, 마지막 등급에는 승급전이 없다.
export function resolveRankAiBattleKind(points: number, grades: RankGradeRow[]): RankAiBattleKind {
  return computeRankPayout(points, true, grades).promoBattle ? "GradeFinal" : "Normal";
}

function integer(value: unknown, min: number, max: number): value is number {
  return typeof value === "number" && Number.isSafeInteger(value) && value >= min && value <= max;
}

// CSV의 슬롯은 저작 순서다. 셔플 전에 AIDeck의 카드 ID에 성장값을 묶는다.
export function parseRankAiEncounters(
  rows: readonly Record<string, unknown>[], decks: readonly AiDeckRow[],
): RankAiEncounter[] {
  const byDeck = new Map(decks.map((deck) => [deck.deckId, deck]));
  const ids = new Set<number>();
  const keys = new Set<string>();
  return rows.map((row) => {
    const {id, tierIndex, battleKind, deckId, highlightSlot} = row;
    if (!integer(id, 1, 2147483647) || ids.has(id) || !integer(tierIndex, 0, 19) ||
        !integer(highlightSlot, 0, 6) || typeof deckId !== "string" ||
        (battleKind !== "Normal" && battleKind !== "DivisionFinal" && battleKind !== "GradeFinal")) {
      throw new Error(`invalid RankAiEncounter row:${row.id}`);
    }
    const deck = byDeck.get(deckId);
    const key = `${tierIndex}:${battleKind}:${deckId}`;
    if (deck == null || tierIndex < deck.fromTier || (deck.toTier !== 0 && tierIndex > deck.toTier) || keys.has(key)) {
      throw new Error(`invalid RankAiEncounter deck or duplicate key:${key}`);
    }
    const cardGrowth = parseAiCardGrowth(deck.cardIds, deck.cardIds.map((cardId, index) => ({
      cardId, level: row[`level${index + 1}`], limitBreak: row[`limitBreak${index + 1}`],
    })));
    if (cardGrowth == null) throw new Error(`invalid RankAiEncounter growth:${id}`);
    ids.add(id);
    keys.add(key);
    return {id, tierIndex, battleKind, deckId, cardGrowth,
      highlightCardId: highlightSlot === 0 ? 0 : deck.cardIds[highlightSlot - 1]};
  });
}

// 같은 티어·전투 종류에 저작된 후보만 기존 AIDeck 가중치로 고른다. 누락은 폴백하지 않는다.
export function drawRankAiEncounter(
  rows: readonly RankAiEncounter[], decks: readonly AiDeckRow[], tierIndex: number,
  battleKind: RankAiBattleKind, roll: (maxExclusive: number) => number,
): {profile: RankAiEncounter; deck: AiDeckRow} {
  const byDeck = new Map(decks.map((deck) => [deck.deckId, deck]));
  const candidates = rows.filter((row) => row.tierIndex === tierIndex && row.battleKind === battleKind)
    .map((profile) => ({profile, deck: byDeck.get(profile.deckId)!}));
  const total = candidates.reduce((sum, entry) => sum + Math.max(1, entry.deck.weight), 0);
  if (!Number.isSafeInteger(total) || total <= 0) {
    throw new Error(`RankAiEncounter has no candidate:${tierIndex}:${battleKind}`);
  }
  let value = roll(total);
  for (const candidate of candidates) {
    value -= Math.max(1, candidate.deck.weight);
    if (value < 0) return candidate;
  }
  throw new Error("invalid RankAiEncounter random draw");
}
