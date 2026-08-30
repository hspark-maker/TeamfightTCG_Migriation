import {randomUUID} from "node:crypto";
import {HttpsError} from "firebase-functions/v2/https";
import {FieldValue} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {isKnownEnv} from "../generated/save/environments";
import {
  readWallet,
  ReceiptKey,
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
 * @param {string} env 환경 id
 * @param {string} uid 유저 uid
 * @param {Function} mutate 현재 지갑을 받아 다음 지갑(nextWallet 산물)을 돌려준다
 * @return {Promise<WalletPatch>} 갱신된 지갑
 */
export async function mutateWallet(
  env: string,
  uid: string,
  mutate: (wallet: WalletState) => WalletUpdate,
): Promise<WalletPatch> {
  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }

  const reference = walletRef(db, env, uid);

  return db.runTransaction(async (transaction) => {
    const snapshot = await transaction.get(reference);
    if (!snapshot.exists) {
      // 도메인 거절(permission-denied)이 아니라 세션 문제다 — 부트의 ensureWallet 이
      // 돌지 않았다는 뜻이라 클라가 다시 부트하는 것이 옳은 조치다. rejectDomain 으로
      // 감싸면 클라가 "잔액이 모자란다" 류의 도메인 사유로 오해하고 부트를 다시 걸지 않는다.
      throw new HttpsError(
        "failed-precondition",
        "Wallet document does not exist. Boot must call ensureWallet first.",
      );
    }

    const update = mutate(readWallet(snapshot));
    // C8-2 에서 요청 txId 로 갈아끼운다 — 지금은 서버 발급이라 멱등이 아니고 영수증만 쌓인다.
    const receipt: ReceiptKey = {kind: "client", txId: randomUUID()};
    const credited = writeWallet(
      transaction, reference, update, receipt, undefined, FieldValue.serverTimestamp());

    return {rev: credited.rev, balances: credited.balances};
  });
}
