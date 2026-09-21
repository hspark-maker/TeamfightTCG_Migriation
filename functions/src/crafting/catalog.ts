import {CURRENCY_MAX} from "../currency/currencyKeys";

export const CRAFT_CURRENCY = "CardDust";
const GRADES = new Set(["Common", "Rare", "Arcane", "Mythic"]);

export interface CraftRecipe {
  cardId: number;
  grade: string;
  cost: number;
}

// Invalid prices disable the feature instead of falling back to free crafting.
export function parseCraftRecipes(
  rules: readonly Record<string, unknown>[], cards: readonly Record<string, unknown>[],
  catalogIds: ReadonlySet<number>,
): CraftRecipe[] {
  if (rules.length === 0) throw new Error("CardCraft is empty.");
  const prices = new Map<string, number>();
  const grades = new Set<string>();
  const ids = new Set<number>();
  for (const row of rules) {
    const {id, grade, cost, enabled} = row;
    if (typeof id !== "number" || !Number.isSafeInteger(id) || id <= 0 || ids.has(id) ||
        typeof grade !== "string" || !GRADES.has(grade) || grades.has(grade) ||
        typeof cost !== "number" || !Number.isSafeInteger(cost) || cost <= 0 || cost > CURRENCY_MAX ||
        (enabled !== 0 && enabled !== 1)) {
      throw new Error("Invalid or duplicate CardCraft rule.");
    }
    ids.add(id);
    grades.add(grade);
    if (enabled === 1) prices.set(grade, cost);
  }
  const seen = new Set<number>();
  const recipes: CraftRecipe[] = [];
  for (const row of cards) {
    const cardId = Number(row.id);
    if (!catalogIds.has(cardId)) continue;
    if (seen.has(cardId) || typeof row.grade !== "string" || !GRADES.has(row.grade)) {
      throw new Error("Invalid or duplicate crafting card.");
    }
    seen.add(cardId);
    const cost = prices.get(row.grade);
    if (cost !== undefined) recipes.push({cardId, grade: row.grade, cost});
  }
  return recipes.sort((a, b) => a.cardId - b.cardId);
}
