import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {randomInt, randomUUID} from "node:crypto";
import {isKnownEnv, requireUid} from "../save/saveDocument";
import {rejectDomain} from "../save/domainReject";
import {clientReceiptId, isClientReceiptId} from "../save/receiptId";
import {canAfford, grant, spend} from "../currency/wallet";
import {nextWallet} from "../currency/walletStore";
import {mutateWallet} from "../currency/walletTransaction";
import {drawRouletteSlot, resolveRouletteBoard, RouletteSlot} from "../roulette/rouletteDraw";
import {MAX_ROULETTE_ID_LENGTH, readRouletteHeaderRow, readRouletteSlotRows} from "../roulette/rouletteSpecReader";

/**
 * 도메인 거절 사유. **와이어 계약**이다 — 클라가 이 문자열을 그대로 대조한다.
 * TxIdReused 는 여기 없다 — mutateWallet 이 직접 던진다.
 */
type RouletteReject = "RouletteNotFound" | "EmptyPool" | "InsufficientTicket";

/**
 * 도메인 거절. 던지기와 로그는 save/domainReject 한 곳이고, 여기 남은 것은 사유 오타를 막는 타입 관문이다.
 * @param {RouletteReject} reason 사유 코드
 * @param {string} message 로그용 설명
 * @param {Record<string, unknown>} context 어느 값에 막혔는지
 */
function reject(reason: RouletteReject, message: string, context: Record<string, unknown>): never {
  rejectDomain(reason, message, context);
}

/**
 * 룰렛 1회 회전. 비용 차감·추첨·지급을 서버가 소유한다.
 *
 * **세이브 문서를 건드리지 않는다** — 움직이는 것이 잔액뿐이라 revision 도 슬롯도 오를 이유가 없다.
 * 그래서 응답에 revision·updatedSlots 가 없다(claimBattleReward 와 같은 축).
 *
 * 자격 문서(WalletGuard)도 없다. 소진 자격이 티켓 잔액 그 자체라 낙인할 바깥 문서가 없다 —
 * 같은 txId 의 재시도는 영수증이 막고, 새 txId 는 티켓이 있는 만큼만 돈다.
 */
export const spinRoulette = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const rouletteId = String(request.data?.rouletteId ?? "");

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }
  if (rouletteId.length === 0 || rouletteId.length > MAX_ROULETTE_ID_LENGTH) {
    throw new HttpsError("invalid-argument", "rouletteId must be a non-empty string.");
  }

  // 스펙 읽기는 트랜잭션 밖이다 — 유저 문서와 무관하고, 재실행마다 다시 읽으면 비용만 는다.
  // 표가 헤더(Roulette)와 칸(RouletteSlot)으로 갈려 읽기가 둘이다. 서로 무관해 함께 기다린다.
  const [header, slotRows] = await Promise.all([
    readRouletteHeaderRow(env, rouletteId),
    readRouletteSlotRows(env, rouletteId),
  ]);
  const context = {uid, env, rouletteId, rowCount: slotRows.length};

  const board = resolveRouletteBoard(header, slotRows);
  if (board === null) {
    // 저작 사고라 유저가 할 수 있는 것이 없다. 헤더가 없거나 비용을 못 읽어도 여기로 온다 —
    // 비용 축을 모르는 채로 과금하느니 판을 통째로 닫는다.
    logger.error("roulette board is unusable", context);
    reject("RouletteNotFound", `Roulette '${rouletteId}' is not authored in the Roulette spec.`, context);
  }
  if (board.slots.length === 0) {
    reject("EmptyPool", `Roulette '${rouletteId}' has no drawable slot.`,
      {...context, droppedRows: board.droppedRows});
  }
  if (board.droppedRows > 0) {
    logger.warn("roulette rows dropped", {...context, droppedRows: board.droppedRows, slotCount: board.slots.length});
  }

  // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
  const txId = clientReceiptId(request.data?.txId, randomUUID());
  // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
  let replayed = true;
  // 추첨 결과. mutate 가 채우고 finalize 가 읽는다(한 트랜잭션 안에서 mutate 가 먼저 돈다).
  let drawn: RouletteSlot | null = null;

  const result = await mutateWallet(env, uid, "spinRoulette", {kind: "client", txId},
    (current) => {
      const balances = current.balances;
      if (!canAfford(balances, board.priceType, board.price)) {
        reject("InsufficientTicket", `Not enough ${board.priceType} to spin '${rouletteId}'.`,
          {...context, priceType: board.priceType, price: board.price, balance: balances[board.priceType]});
      }

      // 추첨이 콜백 안이어야 한다 — 밖에서 뽑으면 경합으로 트랜잭션이 재실행될 때
      // 옛 잔액 기준으로 정한 상품을 새 잔액에 얹는다.
      const slot = drawRouletteSlot(board.slots, randomInt);
      if (slot === null) {
        reject("EmptyPool", `Roulette '${rouletteId}' has no drawable slot.`, context);
      }
      drawn = slot;

      // 차감과 지급을 한 nextWallet 으로 묶는다 — 영수증 changes 가 순증감 한 줄로 남아야 한다.
      const paid = spend(balances, board.priceType, board.price);
      const after = grant(paid, [{currency: slot.currency, amount: slot.amount}]);
      return nextWallet(current, after, "spinRoulette");
    },
    (wallet) => {
      replayed = false;
      if (drawn === null) throw new HttpsError("internal", "Roulette draw did not run before finalize.");
      return {
        rouletteId,
        slotIndex: drawn.slotIndex,
        gain: {currency: drawn.currency, amount: drawn.amount},
        wallet,
      };
    });

  if (replayed) {
    logger.info("receipt replay",
      {uid, env, source: "spinRoulette", txId, rouletteId, rev: result.wallet.rev});
  } else {
    logger.info("spinRoulette", {
      uid, env, rouletteId, slotIndex: result.slotIndex,
      currency: result.gain.currency, amount: result.gain.amount,
      price: board.price, rev: result.wallet.rev,
      txIdSource: isClientReceiptId(request.data?.txId) ? "client" : "server",
    });
  }

  return result;
});
