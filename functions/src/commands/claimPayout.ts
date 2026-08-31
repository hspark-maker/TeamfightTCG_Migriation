import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {FieldValue, Timestamp} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {HEX_32} from "../match/payloadGuards";
import {CURRENCY_KEYS, CurrencyKey} from "../currency/currencyKeys";
import {CurrencyGain, grant} from "../currency/wallet";
import {nextWallet, readWallet, walletRef, writeWallet} from "../currency/walletStore";

type ClaimPayoutData = {env: "live" | "test"; action: "list" | "ack"; matchIds: string[]};

function parseClaimPayoutData(raw: unknown): ClaimPayoutData {
  if (raw == null || typeof raw !== "object") throw new HttpsError("invalid-argument", "payload required");
  const data = raw as Record<string, unknown>;
  const env = data.env;
  const action = data.action == null ? "list" : data.action;
  const rawIds = data.matchIds == null ? [] : data.matchIds;
  if ((env !== "live" && env !== "test") || (action !== "list" && action !== "ack") ||
      !Array.isArray(rawIds) || rawIds.length > 20 || rawIds.some((id) => typeof id !== "string" || !HEX_32.test(id))) {
    throw new HttpsError("invalid-argument", "invalid payout claim payload");
  }
  return {env, action, matchIds: [...new Set(rawIds as string[])]};
}

/**
 * payout 문서에 **이미 적혀 있는** 지급액을 CurrencyGain 으로 읽는다. 다시 계산하지 않는다
 * — 금액의 근거는 submitMatchResult 가 확정 시점에 computeCurrencyPayout 으로 넣어 둔 값이고,
 * 여기서 표를 다시 읽으면 확정 뒤 표가 바뀐 만큼 지급이 갈린다.
 * 재화가 4키 밖이거나 수량이 0 이하면 null 이다(그 payout 은 낙인만 되고 크레딧이 없다).
 * @param {unknown} payout payout 문서 값
 * @return {CurrencyGain | null} 지급 한 건, 읽을 수 없으면 null
 */
function readPayoutGain(payout: unknown): CurrencyGain | null {
  const source = (payout as {currency?: {currency?: unknown; amount?: unknown}} | undefined)?.currency;
  const currency: CurrencyKey | undefined = CURRENCY_KEYS.find((key) => key === source?.currency);
  const amount = Number(source?.amount);
  if (currency === undefined || !Number.isSafeInteger(amount) || amount <= 0) return null;
  return {currency, amount};
}

export const claimPayout = onCall({enforceAppCheck: false}, async (request) => {
  const uid = request.auth?.uid;
  if (!uid) throw new HttpsError("unauthenticated", "authentication required");
  const data = parseClaimPayoutData(request.data);
  const collection = db.collection(`envs/${data.env}/users/${uid}/payouts`);
  if (data.action === "list") {
    const snapshot = await collection.where("status", "==", "ready").limit(20).get();
    const payouts = snapshot.docs.map((doc) => doc.data()).sort((a, b) => {
      const left = a.settledAt instanceof Timestamp ? a.settledAt.toMillis() : 0;
      const right = b.settledAt instanceof Timestamp ? b.settledAt.toMillis() : 0;
      return left - right;
    }).map((payout) => {
      const settledAtMs = payout.settledAt instanceof Timestamp ? payout.settledAt.toMillis() : 0;
      const result = {...payout};
      delete result.settledAt;
      delete result.expiresAt;
      return {...result, settledAtMs};
    });
    return {payouts};
  }
  // 아무것도 요청하지 않은 ack 는 트랜잭션을 열지 않는다. 열면 지갑이 없는 계정에서
  // failed-precondition 이 나가고 클라가 그것을 세션 문제로 읽어 초기화를 끊는다 —
  // 낙인할 것도 크레딧할 것도 없는 호출이 만들 표면이 아니다.
  // wallet 을 null 로 접는 것은 "쓴 지갑을 싣는다"는 계약과 어긋나지 않는다. 읽을 이유가
  // 없어 읽지 않았다는 뜻이고, 클라 ServerSaveCommands 는 wallet 이 비면 채택을 건너뛴다.
  if (data.matchIds.length === 0) return {acked: [], wallet: null};

  const reference = walletRef(db, data.env, uid);
  // 낙인(ready → claimed)과 크레딧은 한 트랜잭션 안이어야 한다 — 갈라 놓으면 낙인만 성공해
  // 보상이 증발하거나, 크레딧만 성공해 무한 재지급이 열린다.
  const result = await db.runTransaction(async (tx) => {
    const refs = data.matchIds.map((matchId) => collection.doc(matchId));
    // Firestore 는 모든 읽기가 모든 쓰기보다 앞서야 한다 — 낙인 대상과 지갑을 먼저 다 읽는다.
    const snapshots = [];
    for (const ref of refs) snapshots.push(await tx.get(ref));
    const walletSnapshot = await tx.get(reference);
    if (!walletSnapshot.exists) {
      // 도메인 거절이 아니라 세션 문제다 — 초기화의 ensureWallet 이 돌지 않았다는 뜻이라
      // 클라가 다시 초기화하는 것이 옳은 조치다(currency/walletTransaction 과 같은 판정).
      throw new HttpsError(
        "failed-precondition",
        "Wallet document does not exist. Boot must call ensureWallet first.",
      );
    }

    const accepted: string[] = [];
    const gains: CurrencyGain[] = [];
    for (let i = 0; i < refs.length; i++) {
      const payout = snapshots[i].data();
      if (payout?.uid !== uid || payout?.matchId !== data.matchIds[i] || payout?.status !== "ready") continue;
      const gain = readPayoutGain(payout);
      if (gain === null) {
        // 확정 시점의 산출 사고라 유저가 지금 할 수 있는 것이 없다. 그래도 낙인은 한다 —
        // ready 로 남기면 클라가 초기화마다 같은 문서를 다시 집어 영원히 되돈다.
        logger.error("payout amount is unusable", {
          uid, env: data.env, matchId: data.matchIds[i], currency: payout?.currency,
        });
      } else {
        gains.push(gain);
      }
      tx.set(refs[i], {status: "claimed", claimedAt: FieldValue.serverTimestamp()}, {merge: true});
      accepted.push(data.matchIds[i]);
    }

    const current = readWallet(walletSnapshot);
    // 크레딧할 것이 없으면 지갑을 쓰지 않는다 — 빈 지급으로 rev 만 올리면 클라가 달라진 것
    // 없는 잔액을 채택하고 사고를 못 알아챈다. 응답에는 현재 잔액을 그대로 싣는다.
    if (gains.length === 0) return {acked: accepted, wallet: {rev: current.rev, balances: current.balances}};

    const next = nextWallet(current, grant(current.balances, gains));
    writeWallet(tx, reference, next, FieldValue.serverTimestamp());
    return {acked: accepted, wallet: {rev: next.rev, balances: next.balances}};
  });
  logger.info("claimPayout ack", {uid, env: data.env, acked: result.acked, rev: result.wallet.rev});
  return result;
});
