/**
 * 지갑 문서(`envs/{env}/users/{uid}/wallet/current`)를 아는 유일한 파일.
 *
 * **이 파일은 functions-currency 로 미러된다**(`scripts/shared-files.js`). 그래서 두 가지를 지킨다.
 * 1. `../firebaseApp` 을 import 하지 않는다 — codebase 마다 자기 앱 인스턴스를 세우므로
 *    미러가 성립하지 않는다. `db`·`transaction`·`now` 를 전부 인자로 받는다.
 * 2. `HttpsError` 를 지지 않는다 — 순수 회귀(`scripts/`)가 `lib/` 를 직접 require 하는 관용구가 깨진다.
 *    env 검증과 거절은 호출부 callable 이 한다.
 *
 * 세이브 문서와 달리 지갑에는 룰 검증 블록이 없다(`write: if false` 라 클라 쓰기가 없다).
 * 그래서 "서버가 룰을 어긴 문서를 써서 그 계정이 영구 거부된다"는 세이브 쪽 함정이 여기엔 없고,
 * 클램프의 역할은 음수·NaN 이 화면을 깨는 것을 막는 데까지다.
 */

import type {
  DocumentReference,
  DocumentSnapshot,
  Firestore,
  Transaction,
} from "firebase-admin/firestore";
import {Balances, CurrencyGain, normalizeBalances} from "./wallet";

/** 지갑 문서의 스키마 축. 세이브 SCHEMA_VERSION 과 별개로 승급한다. */
export const WALLET_SCHEMA_VERSION = 1;

/** 지갑 문서 한 벌. */
export interface WalletState {
  rev: number;
  balances: Balances;
}

/**
 * 지갑 문서 참조. 경로는 클라 FirebaseRootPath.User + /wallet/current 와 같아야 한다.
 * @param {Firestore} db 명명 DB 핸들
 * @param {string} env 환경 id
 * @param {string} uid 사용자 id
 * @return {DocumentReference} 지갑 문서 참조
 */
export function walletRef(db: Firestore, env: string, uid: string): DocumentReference {
  return db.doc(`envs/${env}/users/${uid}/wallet/current`);
}

/**
 * 스냅샷에서 지갑을 읽는다. 문서가 없거나 필드가 깨져도 4키 0 · rev 0 으로 선다
 * — 판정은 호출부가 하고, 여기서 던지면 미러가 순수 계약을 잃는다.
 * @param {DocumentSnapshot} snapshot 지갑 문서 스냅샷
 * @return {WalletState} 지갑 상태
 */
export function readWallet(snapshot: DocumentSnapshot): WalletState {
  const data = snapshot.exists ? snapshot.data() : undefined;
  const rev = Number(data?.rev);

  return {
    rev: Number.isFinite(rev) && rev > 0 ? Math.trunc(rev) : 0,
    balances: normalizeBalances((data?.balances ?? {}) as Balances),
  };
}

/**
 * 지갑을 쓴다. **rev 를 올리는 유일한 지점**이다.
 *
 * rev 는 단조 증가만 보장한다(세이브 revision 과 달리 "정확히 +1" 이 아니다) — 지갑은 두 codebase 가
 * 쓰고, 장차 결제 웹훅처럼 클라가 모르는 정당한 쓰기가 생긴다. 거기에 +1 을 강제하면
 * 첫 결제에서 전 유저 세션이 끊긴다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {DocumentReference} ref 지갑 문서 참조
 * @param {WalletState} next 쓸 상태(rev 는 갱신 후 값)
 * @param {unknown} now 서버 시각(FieldValue.serverTimestamp()) — 호출부가 넘긴다
 * @return {void}
 */
export function writeWallet(
  transaction: Transaction,
  ref: DocumentReference,
  next: WalletState,
  now: unknown,
): void {
  transaction.set(ref, {
    schemaVersion: WALLET_SCHEMA_VERSION,
    rev: next.rev,
    balances: normalizeBalances(next.balances),
    updatedAt: now,
  });
}

/**
 * 지갑을 새로 만든다. `set` 이 아니라 `create` 라 경합하면 트랜잭션이 재실행된다
 * — 두 부트가 겹쳐 잔액이 두 번 이관되는 것을 막는다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {DocumentReference} ref 지갑 문서 참조
 * @param {Balances} balances 최초 잔액
 * @param {unknown} now 서버 시각
 * @return {WalletState} 만들어진 상태
 */
export function createWallet(
  transaction: Transaction,
  ref: DocumentReference,
  balances: Balances,
  now: unknown,
): WalletState {
  const created: WalletState = {rev: 1, balances: normalizeBalances(balances)};

  transaction.create(ref, {
    schemaVersion: WALLET_SCHEMA_VERSION,
    rev: created.rev,
    balances: created.balances,
    updatedAt: now,
  });

  return created;
}

/**
 * 원장 한 줄. **아직 호출자가 없다** — 인앱결제 착수 때 붙는 자리다.
 * txId 는 호출부가 정한다(결제는 스토어 주문 id, 도메인은 `{command}:{seed}`) — 그것이 멱등 키다.
 * @param {string} source 명령 이름
 * @param {CurrencyGain[]} changes 부호 있는 증감 목록
 * @param {Balances} after 반영 후 잔액
 * @param {number} rev 이 기록 직후의 지갑 rev
 * @param {unknown} now 서버 시각
 * @return {object} 원장 문서 값
 */
export function ledgerEntry(
  source: string,
  changes: CurrencyGain[],
  after: Balances,
  rev: number,
  now: unknown,
): object {
  const delta: Record<string, number> = {};
  for (const change of changes) {
    delta[change.currency] = (delta[change.currency] ?? 0) + change.amount;
  }

  return {
    source,
    changes: delta,
    after: normalizeBalances(after),
    rev,
    createdAt: now,
    receipt: null,
  };
}
