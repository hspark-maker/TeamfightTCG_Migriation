import {HttpsError, onCall} from "firebase-functions/v2/https";
import {isKnownEnv, requireUid} from "../save/saveDocument";
import {measuredCallable} from "../observability/requestMetrics";
import {CRAFT_CURRENCY} from "../crafting/catalog";
import {readCraftingCatalog} from "../crafting/spec";

/** Prices are public to signed-in players; ownership and affordability are checked on craft. */
export const getCardCrafting = onCall(measuredCallable("getCardCrafting", async (request) => {
  requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  if (!isKnownEnv(env)) throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  const catalog = await readCraftingCatalog(env);
  return {currency: CRAFT_CURRENCY, cards: catalog.recipes};
}));
