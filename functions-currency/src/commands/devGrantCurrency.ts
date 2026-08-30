import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {CURRENCY_KEYS, CurrencyKey} from "../generated/currency/currencyKeys";
import {grant} from "../generated/currency/wallet";
import {nextWallet} from "../generated/currency/walletStore";
import {mutateWallet} from "../currency/walletTransaction";

/**
 * 디버그 재화 지급. 클라 디버그 오버레이가 부르는 test env 전용 통로다.
 * 지갑 문서만 쓴다 — 세이브 진행도와는 무관하다.
 *
 * 여기서는 invalid-argument 를 던진다 — 도메인 명령이었다면 클라 CloudFailureClassifier 가
 * 세션을 끊어 문제였겠지만, 이 함수는 라이브에서 아예 닿지 않는 디버그 경로라
 * 잘못된 인자로 세션이 막히는 것이 오히려 옳은 신호다.
 *
 * 이 codebase 로 옮겨 온 첫 **지갑 쓰기** 명령이다(C6.6). 응답 모양은 default 에 있던 때와 같다
 * — 클라 계약이라 codebase 이사로 바뀌면 안 된다.
 */
export const devGrantCurrency = onCall(async (request) => {
  // requireUid(save/saveDocument)는 firebase-admin·세이브 문서를 물고 있어 이 codebase 로 넘어오지 않는다.
  // 인증 관문은 currencyPing 과 같은 3줄짜리 지역 관용구로 둔다 — 코드·메시지는 옛 requireUid 그대로다.
  const uid = request.auth?.uid;
  if (!uid) {
    throw new HttpsError("unauthenticated", "Sign-in is required.");
  }

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

  const wallet = await mutateWallet(env, uid, (current) =>
    nextWallet(current, grant(current.balances, [{currency, amount}]), "devGrantCurrency"));

  logger.info("devGrantCurrency", {uid, env, currency, amount, rev: wallet.rev});
  return {wallet};
});
