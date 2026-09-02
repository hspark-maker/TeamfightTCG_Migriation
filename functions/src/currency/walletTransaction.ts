import {HttpsError} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {DocumentReference, DocumentSnapshot, FieldValue} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {withCountedTransaction} from "../observability/countedTransaction";
import {isKnownEnv} from "../save/environments";
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
} from "./walletStore";

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
 * 지급 자격을 쥔 바깥 문서. 지갑 쓰기와 **같은 트랜잭션**에서 읽고 낙인한다.
 *
 * 영수증(txId)은 "같은 요청이 두 번 왔는가" 만 가른다 — 클라가 새 txId 를 붙여 같은 자격을
 * 다시 청구하는 것은 못 막는다. 자격이 매치 문서처럼 한 번만 소진되어야 하는 것이면
 * 그 문서 자체를 낙인해야 하고, 낙인이 지갑 쓰기와 갈라지면 그 사이에서 이중 지급이 성립한다.
 * 트랜잭션을 콜백에 넘기지 않는 이유는 mutate 와 같다 — 여기서 열면 walletStore 밖에서
 * 지갑을 쓰는 경로가 생긴다. 그래서 읽기(verify)와 쓰기(stamp)를 값으로만 받는다.
 */
export type WalletGuard = {
  /** 자격 문서. verify 가 통과하면 stamp 가 merge 로 얹힌다. */
  ref: DocumentReference;
  /** 자격 판정. 부적격이면 던진다(rejectDomain 등) — 던지면 지갑도 낙인도 쓰이지 않는다. */
  verify: (snapshot: DocumentSnapshot) => void;
  /** 자격 소진 낙인. 다음 청구가 verify 에서 걸리도록 만드는 필드다. */
  stamp: Record<string, unknown>;
};

/**
 * 지갑을 트랜잭션 1회로 읽고 고친다. 반환은 WalletPatch 뿐이다
 * — revision·updatedSlots 는 세이브 문서의 것이고 여기선 아무것도 오르지 않는다.
 * 같은 txId 로 다시 온 요청은 콜백에 들어가기도 전에 첫 응답을 되돌려준다(쓰기 0회).
 * 응답 조립을 finalize 콜백으로 받는 것이 그 때문이다 — 트랜잭션 밖에서 조립하면
 * 캐시할 응답이 아직 없어서 영수증에 실을 것이 없다.
 * @param {string} env 환경 id
 * @param {string} uid 유저 uid
 * @param {string} source 명령 이름. 재시도 판정의 대조축이다
 * @param {ReceiptKey} receipt 영수증 번호(요청 txId 또는 서버 발급)
 * @param {Function} mutate 현재 지갑을 받아 다음 지갑(nextWallet 산물)을 돌려준다
 * @param {Function} finalize 갱신된 지갑에 명령별 필드를 얹어 최종 응답을 만든다. 트랜잭션 안에서 돌다
 * @param {WalletGuard} guard 지급 자격 문서. 넘기면 같은 트랜잭션에서 검사·낙인한다
 * @return {Promise<TResponse>} finalize 가 만든 응답
 */
export async function mutateWallet<TResponse>(
  env: string,
  uid: string,
  source: string,
  receipt: ReceiptKey,
  mutate: (wallet: WalletState) => WalletUpdate,
  finalize: (wallet: WalletPatch) => TResponse,
  guard?: WalletGuard,
): Promise<TResponse> {
  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }

  const reference = walletRef(db, env, uid);

  return withCountedTransaction(source, async (transaction) => {
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

    // 영수증 조회. 히트면 쓰기를 하나도 하지 않고 첫 응답을 그대로 돌려준다(자격 검사도 건너뛴다).
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

    // 자격 검사는 영수증 히트 **뒤**다. 히트한 재시도는 첫 호출이 이미 낙인했으므로
    // 여기서 다시 보면 자기 낙인에 걸려 already-claimed 로 떨어진다.
    // 읽기는 전부 쓰기 앞에 모여 있어야 한다(Firestore 트랜잭션 제약) — 그래서 mutate 앞이다.
    const guardSnapshot = guard === undefined ? null : await transaction.get(guard.ref);
    if (guard !== undefined && guardSnapshot !== null) guard.verify(guardSnapshot);

    // 트랜잭션을 콜백에 넘기지 않는다 — 넘기면 walletStore 밖에서 쓰는 콜백이 생겨
    // 브랜드 타입 강제가 뚫린다.
    const update = mutate(readWallet(snapshot));
    // 응답은 쓰기 전에 짓는다 — 그것 그대로가 영수증에 담겨야 재시도가 같은 답을 받는다.
    const response = finalize({rev: update.next.rev, balances: update.next.balances});
    writeWallet(
      transaction, reference, update, receipt, response, FieldValue.serverTimestamp());
    // 낙인은 지갑과 같은 커밋이다 — 갈라 놓으면 그 틈이 곧 이중 지급 창이다.
    if (guard !== undefined) transaction.set(guard.ref, guard.stamp, {merge: true});

    return response;
  });
}
