import {HttpsError} from "firebase-functions/v2/https";
import {readSpecRows} from "../packs/packSpecReader";
import {loadCatalogIds} from "../packs/cardCatalog";
import {parseCraftRecipes} from "./catalog";

export async function readCraftingCatalog(env: string) {
  try {
    const [rules, cards, catalogIds] = await Promise.all([
      readSpecRows(env, "CardCraft"), readSpecRows(env, "Card"), loadCatalogIds(env),
    ]);
    return {cards, recipes: parseCraftRecipes(rules, cards, catalogIds)};
  } catch {
    throw new HttpsError("unavailable", "Card crafting definitions are unavailable.");
  }
}
