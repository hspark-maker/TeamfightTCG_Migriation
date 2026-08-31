import {HttpsError} from "firebase-functions/v2/https";
import {FieldValue} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {isKnownEnv} from "../save/environments";
import {readWallet, walletRef, WalletPatch, WalletState, writeWallet} from "./walletStore";

/**
 * 지갑 문서만 여닫는 트랜잭션. 세이브 문서를 **아예 건드리지 않는** 명령이 쓴다
 * (전투 보상처럼 진행도가 아니라 잔액만 움직이는 것들).
 *
 * walletStore 는 functions-currency 로 미러되는 순수 파일이라 db 를 import 할 수 없다.
 * 이 파일은 미러 대상이 아니므로(scripts/shared-files.js 에 넣지 마라) 앱 핸들을 직접 쓴다.
 *
 * functions-currency 에 **같은 이름의 쌍둥이**가 있다(`functions-currency/src/currency/walletTransaction.ts`).
 * firebaseApp.ts 와 같은 이유로 일부러 두 벌이다 — 이 파일에 남은 것은 codebase 자기 db 핸들에
 * 묶인 트랜잭션 배관뿐이고, 재화 산술·문서 직렬화·키 목록·환경 목록·응답 모양은 전부
 * 미러되는 원본 한 벌(wallet·walletStore·currencyKeys·environments)에 있다.
 * 여기 재화 규칙을 새로 적기 시작하면 그 순간 두 codebase 가 갈린다.
 */

/**
 * 지갑을 트랜잭션 1회로 읽고 고친다. 반환은 WalletPatch 뿐이다
 * — revision·updatedSlots 는 세이브 문서의 것이고 여기선 아무것도 오르지 않는다.
 * @param {string} env 환경 id
 * @param {string} uid 유저 uid
 * @param {Function} mutate 현재 지갑을 받아 다음 지갑(nextWallet 산물)을 돌려준다
 * @return {Promise<WalletPatch>} 갱신된 지갑
 */
export async function mutateWallet(
  env: string,
  uid: string,
  mutate: (wallet: WalletState) => WalletState,
): Promise<WalletPatch> {
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

    const next = mutate(readWallet(snapshot));
    writeWallet(transaction, reference, next, FieldValue.serverTimestamp());

    return {rev: next.rev, balances: next.balances};
  });
}
