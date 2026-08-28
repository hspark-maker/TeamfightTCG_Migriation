import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {mutateSave, requireUid, SlotPatch} from "../save/saveDocument";
import {CURRENCY_KEYS, CurrencyKey} from "../currency/currencyKeys";
import {currencySlot, grant, readBalances} from "../currency/wallet";

/**
 * 디버그 재화 지급. 클라 디버그 오버레이가 부르는 test env 전용 통로다.
 *
 * 여기서는 invalid-argument 를 던진다 — 도메인 명령이었다면 클라 CloudFailureClassifier 가
 * 세션을 끊어 문제였겠지만, 이 함수는 라이브에서 아예 닿지 않는 디버그 경로라
 * 잘못된 인자로 세션이 막히는 것이 오히려 옳은 신호다.
 */
export const devGrantCurrency = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");

  // 라이브 문서는 어떤 경우에도 이 함수가 건드리지 않는다.
  if (env !== "test") {
    throw new HttpsError(
      "permission-denied",
      "devGrantCurrency is available on the test env only.",
    );
  }

  const requestedCurrency = String(request.data?.currency ?? "");
  const currency: CurrencyKey | undefined = CURRENCY_KEYS.find((key) => key === requestedCurrency);
  if (currency === undefined) {
    throw new HttpsError("invalid-argument", `Unknown currency: ${requestedCurrency}`);
  }

  const amount = Number(request.data?.amount ?? 0);
  if (!Number.isSafeInteger(amount) || amount <= 0) {
    throw new HttpsError("invalid-argument", "amount must be a positive safe integer.");
  }

  const result = await mutateSave(env, uid, (current): SlotPatch => ({
    currency: currencySlot(grant(readBalances(current.currency), [{currency, amount}])),
  }));

  logger.info("devGrantCurrency", {uid, env, currency, amount, revision: result.revision});
  return result;
});
