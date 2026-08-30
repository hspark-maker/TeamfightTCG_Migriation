/**
 * 지갑 문서(`envs/{env}/users/{uid}/wallet/current`)와 그 **영수증**
 * (`.../wallet/current/receipts/{txId}`)을 아는 유일한 파일.
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
 *
 * **영수증은 잔액 쓰기의 일부다.** nextWallet 이 브랜드 타입 WalletUpdate 를 내고 writeWallet 이
 * 그것만 받으므로, 잔액만 쓰고 영수증을 빠뜨리는 경로가 타입 수준에서 존재하지 않는다.
 */

import type {
  DocumentReference,
  DocumentSnapshot,
  Firestore,
  Transaction,
} from "firebase-admin/firestore";
import {CURRENCY_KEYS} from "./currencyKeys";
import {Balances, normalizeBalances} from "./wallet";
import {intOf} from "../save/saveValues";

/** 지갑 문서의 스키마 축. 세이브 SCHEMA_VERSION 과 별개로 승급한다. */
export const WALLET_SCHEMA_VERSION = 1;

/**
 * 지갑 문서 한 벌.
 *
 * paidBalances 는 balances 안에서 실화폐로 산 몫이다 — balances 와 **같은 평면**에 둔다.
 * 재화별로 {free, paid} 로 중첩하면 `Balances = Record<string, number>` 가 깨져
 * 순수 산술(wallet.ts)·룰·클라가 전부 유니온을 다뤄야 한다.
 * 불변식: 모든 키에서 paidBalances[k] <= balances[k]. 빈 맵이 "전부 무상"의 정규형이다.
 */
export interface WalletState {
  rev: number;
  balances: Balances;
  paidBalances: Balances;
}

/**
 * 클라 응답에 싣는 지갑. **paidBalances 는 절대 싣지 않는다** — 유상분은 서버 정책의
 * 내부 상태라 클라가 알 이유가 없고, 한 번 내보내면 와이어 계약이 되어 되돌릴 수 없다.
 *
 * 지갑을 쓰는 codebase 가 둘(default·currency)이라 선언을 여기 둔다 — 응답 모양이
 * codebase 마다 갈리면 클라가 같은 지갑을 두 가지로 읽는다.
 */
export interface WalletPatch {
  rev: number;
  balances: Balances;
}

// declare 라 값을 방출하지 않는다 — 미러의 "firebase 모듈 미적재" 계약과 무관하게 순수하다.
declare const walletUpdateBrand: unique symbol;

/**
 * 잔액 이동 1건. **nextWallet 만이 만들고 writeWallet 만이 소비한다.**
 * 브랜드 필드가 있어 호출부가 손으로 조립할 수 없고, 그래서 영수증 없는 잔액 쓰기가 성립하지 않는다.
 */
export interface WalletUpdate {
  readonly [walletUpdateBrand]: void;
  readonly next: WalletState;
  /** 영수증의 source. 명령 이름 또는 walletCreate:* 다. */
  readonly source: string;
  readonly before: Balances;
  /** after − before. 4키 전부 싣는다(무변화는 0). */
  readonly changes: Balances;
}

/**
 * 부트가 지갑을 세우는 두 자리. 잔액 이동이 아니라 개설이라 축이 다르다.
 * createWallet 은 string 을 받는다 — 개설과 이동이 한 트랜잭션에 겹치면 영수증에는
 * 돈을 움직인 **명령 이름**이 들어가야 하기 때문이다. 이 유니온은 그 두 자리의 오타만 막는다.
 */
export type WalletCreateSource =
  | "walletCreate:migration"
  | "walletCreate:freshAccount";

/**
 * 영수증 번호와 그 발급 경로.
 *
 * client 는 `create` 다 — 같은 txId 가 두 번 커밋되면 트랜잭션이 재실행되어 중복 집행이 막힌다.
 * boot 는 `set` 이다 — 지갑 문서 자체가 `transaction.create` 라 1회성은 이미 그쪽이 보장하는데,
 * 여기서까지 create 로 두면 **지갑만 지워지고 영수증이 남은 계정**이 재생성에서 ALREADY_EXISTS 로
 * 영구 실패한다. 감사 기록 한 줄 때문에 계정을 굳힐 수는 없다.
 */
export type ReceiptKey =
  | {kind: "client"; txId: string}
  | {kind: "boot"; txId: string};

/**
 * 아직 호출자가 없다 — C8-2 의 영수증 pre-read 가 붙는 자리다.
 * 영수증 조회 결과. hit 이면 그 요청은 이미 처리됐다.
 */
export type ReceiptLookup =
  | {hit: false}
  | {hit: true; source: string; result: unknown};

/**
 * 유상분을 잔액 이하로 자른다. **이 클램프가 "무상 먼저 소진" 정책 전부다**
 * — 잔액이 줄면 유상분이 새 잔액까지 따라 깎이므로, 감소분은 무상분에서 먼저 나간 셈이 된다.
 * 0 이하인 키는 아예 뺀다(빈 맵 = 전부 무상).
 * @param {Balances} paid 유상 잔액(부분·오염 가능)
 * @param {Balances} balances 정규화된 전체 잔액
 * @return {Balances} 잘린 유상 잔액
 */
function clampPaid(paid: Balances, balances: Balances): Balances {
  const source = paid ?? {};
  const next: Balances = {};
  for (const key of CURRENCY_KEYS) {
    const value = Math.min(intOf(source[key]), intOf(balances[key]));
    if (value > 0) next[key] = value;
  }
  return next;
}

/**
 * 재화별 증감. **호출부가 넘기지 않고 여기서 차분한다** — 손으로 넘기게 두면 그것이
 * 영수증이 거짓말할 수 있는 유일한 축이 된다(spend/grant 는 상·하한에서 자르므로
 * "의도한 증감"과 "실제 증감"이 다를 수 있고, 영수증은 실제를 적어야 한다).
 * @param {Balances} before 이동 전 잔액
 * @param {Balances} after 이동 후 잔액
 * @return {Balances} 4키 증감(무변화는 0)
 */
function diffBalances(before: Balances, after: Balances): Balances {
  const changes: Balances = {};
  for (const key of CURRENCY_KEYS) {
    changes[key] = intOf(after[key]) - intOf(before[key]);
  }
  return changes;
}

/**
 * 다음 지갑 상태를 만드는 **유일한 출구**. 명령이 {rev, balances, paidBalances} 를 손으로
 * 조립하기 시작하면 유상분 불변식이 명령마다 갈린다.
 *
 * rev 도 여기서 올린다 — writeWallet 은 받은 값을 그대로 싣는 직렬화기일 뿐이라,
 * 호출부가 rev+1 을 손으로 얹게 두면 빠뜨린 명령의 쓰기가 앞선 쓰기를 덮는다.
 * 세이브 revision 과 달리 "정확히 +1" 은 계약이 아니다(결제 웹훅처럼 클라가 모르는
 * 정당한 쓰기가 생긴다) — 여기서 보장하는 것은 단조 증가뿐이다.
 * @param {WalletState} current 현재 상태
 * @param {Balances} balances 반영 후 잔액
 * @param {string} source 영수증에 적을 명령 이름
 * @return {WalletUpdate} 다음 상태와 그것을 설명하는 영수증 재료
 */
export function nextWallet(
  current: WalletState, balances: Balances, source: string,
): WalletUpdate {
  const before = normalizeBalances(current.balances);
  const nextBalances = normalizeBalances(balances);

  return {
    next: {
      rev: current.rev + 1,
      balances: nextBalances,
      paidBalances: clampPaid(current.paidBalances, nextBalances),
    },
    source,
    before,
    changes: diffBalances(before, nextBalances),
  } as WalletUpdate;
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
 * 아직 프로덕션 호출자가 없다 — C8-2 의 영수증 pre-read 가 붙는 자리다(내부 writeReceipt 는 이미 쓴다).
 * 영수증 문서 참조. 지갑 아래에 두므로 호출부는 지갑 ref 만 알면 된다.
 * @param {DocumentReference} wallet 지갑 문서 참조
 * @param {string} txId 영수증 번호
 * @return {DocumentReference} 영수증 문서 참조
 */
export function receiptRef(wallet: DocumentReference, txId: string): DocumentReference {
  return wallet.collection("receipts").doc(txId);
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
  const balances = normalizeBalances((data?.balances ?? {}) as Balances);

  return {
    rev: Number.isFinite(rev) && rev > 0 ? Math.trunc(rev) : 0,
    balances,
    // paidBalances 가 없는 문서는 유상 지급 이전의 지갑이라 전부 무상이다.
    paidBalances: clampPaid((data?.paidBalances ?? {}) as Balances, balances),
  };
}

/**
 * 아직 호출자가 없다 — C8-2 의 영수증 pre-read 가 붙는 자리다.
 * 영수증을 읽는다. 있으면 그 요청은 이미 처리됐고, 기록된 result 를 그대로 돌려주면 된다.
 *
 * **깨진 result 는 던진다.** 미스로 강등하면 재집행이 열려 이 장치의 목적이 통째로 무너진다
 * — 되풀이되는 실패가 조용한 이중 과금보다 낫다.
 * @param {DocumentSnapshot} snapshot 영수증 문서 스냅샷
 * @return {ReceiptLookup} 조회 결과
 */
export function readReceipt(snapshot: DocumentSnapshot): ReceiptLookup {
  if (!snapshot.exists) return {hit: false};

  const data = snapshot.data() ?? {};
  const raw = data.result;

  return {
    hit: true,
    source: String(data.source ?? ""),
    result: raw === null || raw === undefined ? null : JSON.parse(String(raw)),
  };
}

/**
 * 지갑과 영수증을 같은 트랜잭션에 싣는다. **받은 값을 그대로 싣는 직렬화기다**
 * — rev 를 올리는 것은 nextWallet 이다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {DocumentReference} ref 지갑 문서 참조
 * @param {WalletUpdate} update nextWallet 산물
 * @param {ReceiptKey} receipt 영수증 번호
 * @param {unknown} result 재시도가 그대로 돌려받을 응답(JSON 으로 싣는다)
 * @param {unknown} now 서버 시각(FieldValue.serverTimestamp()) — 호출부가 넘긴다
 * @return {WalletState} 쓰인 지갑 상태
 */
export function writeWallet(
  transaction: Transaction,
  ref: DocumentReference,
  update: WalletUpdate,
  receipt: ReceiptKey,
  result: unknown,
  now: unknown,
): WalletState {
  const balances = normalizeBalances(update.next.balances);

  transaction.set(ref, {
    schemaVersion: WALLET_SCHEMA_VERSION,
    rev: update.next.rev,
    balances,
    paidBalances: clampPaid(update.next.paidBalances, balances),
    updatedAt: now,
  });

  writeReceipt(transaction, ref, receipt, {
    source: update.source,
    before: update.before,
    after: balances,
    changes: update.changes,
    rev: update.next.rev,
  }, result, now);

  return update.next;
}

/**
 * 지갑을 새로 만들고 첫 영수증을 끊는다. `set` 이 아니라 `create` 라 경합하면 트랜잭션이
 * 재실행된다 — 두 부트가 겹쳐 잔액이 두 번 이관되는 것을 막는다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {DocumentReference} ref 지갑 문서 참조
 * @param {Balances} balances 최초 잔액
 * @param {string} source 개설 경로(WalletCreateSource) 또는 개설과 이동이 겹쳤을 때의 명령 이름
 * @param {ReceiptKey} receipt 영수증 번호
 * @param {unknown} result 재시도가 그대로 돌려받을 응답
 * @param {unknown} now 서버 시각
 * @return {WalletState} 만들어진 상태
 */
export function createWallet(
  transaction: Transaction,
  ref: DocumentReference,
  balances: Balances,
  source: string,
  receipt: ReceiptKey,
  result: unknown,
  now: unknown,
): WalletState {
  // 이관으로 선 지갑은 전부 무상이다 — 유상분은 결제가 처음으로 채운다.
  const created: WalletState = {
    rev: 1,
    balances: normalizeBalances(balances),
    paidBalances: {},
  };

  transaction.create(ref, {
    schemaVersion: WALLET_SCHEMA_VERSION,
    rev: created.rev,
    balances: created.balances,
    paidBalances: created.paidBalances,
    updatedAt: now,
  });

  const before = normalizeBalances({});
  writeReceipt(transaction, ref, receipt, {
    source,
    before,
    after: created.balances,
    changes: diffBalances(before, created.balances),
    rev: created.rev,
  }, result, now);

  return created;
}

/**
 * 지갑을 **쓰지 않은** 트랜잭션의 영수증. 낙인만 찍고 잔액은 그대로인 경로(claimPayout 의
 * 지급 0건 ack)가 여기 온다 — 영수증이 없으면 재시도가 첫 응답과 다른 답을 내민다.
 *
 * 규칙: **재화를 움직였거나, 재화 이동을 대신하는 낙인을 썼으면 그 txId 로 영수증을 끊는다.**
 * 재화와 무관한 쓰기(ensureSaveDocument 의 계정 복구 같은)는 여기 오지 않는다 — 적으면 감사 축이 오염된다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {DocumentReference} ref 지갑 문서 참조
 * @param {string} source 명령 이름
 * @param {WalletState} wallet 손대지 않은 현재 지갑
 * @param {ReceiptKey} receipt 영수증 번호
 * @param {unknown} result 재시도가 그대로 돌려받을 응답
 * @param {unknown} now 서버 시각
 * @return {void}
 */
export function writeReceiptOnly(
  transaction: Transaction,
  ref: DocumentReference,
  source: string,
  wallet: WalletState,
  receipt: ReceiptKey,
  result: unknown,
  now: unknown,
): void {
  const balances = normalizeBalances(wallet.balances);

  writeReceipt(transaction, ref, receipt, {
    source,
    before: balances,
    after: balances,
    changes: diffBalances(balances, balances),
    rev: wallet.rev,
  }, result, now);
}

/** 영수증 문서의 값 축. 세 진입점이 같은 모양을 쓰도록 한 곳에 모은다. */
interface ReceiptBody {
  source: string;
  before: Balances;
  after: Balances;
  changes: Balances;
  rev: number;
}

/**
 * 영수증 문서를 쓴다. 평탄하게 잡는다 — 중첩을 넣으면 나중에 웨어하우스로 내보낼 때 값을 치른다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {DocumentReference} wallet 지갑 문서 참조
 * @param {ReceiptKey} receipt 영수증 번호
 * @param {ReceiptBody} body 영수증 본문
 * @param {unknown} result 재시도가 그대로 돌려받을 응답
 * @param {unknown} now 서버 시각
 * @return {void}
 */
function writeReceipt(
  transaction: Transaction,
  wallet: DocumentReference,
  receipt: ReceiptKey,
  body: ReceiptBody,
  result: unknown,
  now: unknown,
): void {
  const reference = receiptRef(wallet, receipt.txId);
  const document = {
    txId: receipt.txId,
    source: body.source,
    changes: body.changes,
    before: body.before,
    after: body.after,
    rev: body.rev,
    result: result === undefined ? null : JSON.stringify(result),
    // 인앱결제가 처음으로 채운다 — 스토어 영수증 원문 자리다.
    storeReceipt: null,
    createdAt: now,
  };

  if (receipt.kind === "client") transaction.create(reference, document);
  else transaction.set(reference, document);
}
