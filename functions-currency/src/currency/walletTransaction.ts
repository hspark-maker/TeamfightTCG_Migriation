import {HttpsError} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {FieldValue} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {isKnownEnv} from "../generated/save/environments";
import {
  readReceipt,
  readWallet,
  ReceiptKey,
  receiptRef,
  walletRef,
  WalletPatch,
  WalletState,
  WalletUpdate,
  writeWallet,
} from "../generated/currency/walletStore";

/**
 * 지갑 문서만 여닫는 트랜잭션. 이 codebase 의 명령이 잔액을 쓰는 유일한 통로다.
 *
 * functions(default) 의 `src/currency/walletTransaction.ts` 와 **일부러 두 벌이다**
 * — firebaseApp.ts 가 두 벌인 것과 같은 이유다. `db` 는 codebase 마다 자기 앱 인스턴스라
 * 미러(`src/generated`)에 넣을 수 없고, 미러 파일은 `HttpsError` 도 지지 않기로 돼 있다.
 *
 * 그래서 여기 남는 것은 **자기 db 핸들에 묶인 트랜잭션 배관과 거절 코드**뿐이다.
 * 재화 산술·문서 직렬화·rev 승급·키 목록·환경 목록·응답 모양은 전부 미러되는 원본 한 벌
 * (`wallet` · `walletStore` · `currencyKeys` · `environments`)이 갖는다.
 * 이 파일에 재화 규칙을 새로 적기 시작하면 그 순간 두 codebase 가 갈린다 — 적지 마라.
 */

/**
 * 지갑을 트랜잭션 1회로 읽고 고친다. 반환은 WalletPatch 뿐이다
 * — revision·updatedSlots 는 세이브 문서의 것이고 이 codebase 는 세이브를 아예 열지 않는다.
 * 같은 txId 로 다시 온 요청은 콜백에 들어가기도 전에 첫 응답을 되돌려준다(쓰기 0회).
 * 응답 조립을 finalize 콜백으로 받는 것이 그 때문이다 — 트랜잭션 밖에서 조립하면
 * 캐시할 응답이 아직 없어서 영수증에 실을 것이 없다.
 * @param {string} env 환경 id
 * @param {string} uid 유저 uid
 * @param {string} source 명령 이름. 재시도 판정의 대조축이다
 * @param {ReceiptKey} receipt 영수증 번호(요청 txId 또는 서버 발급)
 * @param {Function} mutate 현재 지갑을 받아 다음 지갑(nextWallet 산물)을 돌려준다
 * @param {Function} finalize 갱신된 지갑에 명령별 필드를 얹어 최종 응답을 만든다. 트랜잭션 안에서 돌다
 * @return {Promise<TResponse>} finalize 가 만든 응답
 */
export async function mutateWallet<TResponse>(
  env: string,
  uid: string,
  source: string,
  receipt: ReceiptKey,
  mutate: (wallet: WalletState) => WalletUpdate,
  finalize: (wallet: WalletPatch) => TResponse,
): Promise<TResponse> {
  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }

  const reference = walletRef(db, env, uid);

  return db.runTransaction(async (transaction) => {
    const snapshot = await transaction.get(reference);
    if (!snapshot.exists) {
      // 도메인 거절(permission-denied)이 아니라 세션 문제다 — 초기화의 ensureWallet 이
      // 돌지 않았다는 뜻이라 클라가 다시 초기화하는 것이 옳은 조치다. rejectDomain 으로
      // 감싸면 클라가 "잔액이 모자란다" 류의 도메인 사유로 오해하고 초기화를 다시 걸지 않는다.
      throw new HttpsError(
        "failed-precondition",
        "Wallet document does not exist. Boot must call ensureWallet first.",
      );
    }

    // 영수증 조회가 마지막 읽기다 — 히트면 쓰기를 하나도 하지 않고 첫 응답을 그대로 돌려준다.
    const lookup = readReceipt(await transaction.get(receiptRef(reference, receipt.txId)));
    if (lookup.hit) {
      if (lookup.source !== source) {
        // 같은 txId 를 다른 명령이 재사용했다. 첫 명령의 응답을 돌려주면 클라가 엉뚱한
        // 결과를 채택하므로 집행하지 않고 거절한다. 도메인 거절이라 permission-denied 다
        // (save/domainReject 와 같은 계약) — 다른 코드로 나가면 클라가 세션을 끊는다.
        logger.warn("domain rejected", {
          reason: "TxIdReused", uid, env, source,
          receiptSource: lookup.source, txId: receipt.txId,
        });
        throw new HttpsError(
          "permission-denied",
          `TxIdReused: txId '${receipt.txId}' was already used by another command.`,
          {reason: "TxIdReused"});
      }
      return lookup.result as TResponse;
    }

    // 트랜잭션을 콜백에 넘기지 않는다 — 넘기면 walletStore 밖에서 쓰는 콜백이 생겨
    // 브랜드 타입 강제가 뚫린다.
    const update = mutate(readWallet(snapshot));
    // 응답은 쓰기 전에 짓는다 — 그것 그대로가 영수증에 담겨야 재시도가 같은 답을 받는다.
    const response = finalize({rev: update.next.rev, balances: update.next.balances});
    writeWallet(
      transaction, reference, update, receipt, response, FieldValue.serverTimestamp());

    return response;
  });
}
