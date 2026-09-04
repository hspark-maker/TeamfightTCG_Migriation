/**
 * 튜토리얼 무료 한 방 문서(`envs/{env}/users/{uid}/grants/current`)를 아는 유일한 파일.
 *
 * 신규 계정 지급은 Gold 뿐이라 온보딩이 시키는 카드 강화(Shard)·키워드 강화(Energy)는 낼 돈이 없다.
 * 그 한 방을 클라 정적 필드가 들고 있으면 앱 재시작으로 되살아나므로 계정당 1회를 서버가 소유한다.
 *
 * 같은 문서가 튜토리얼 무료 팩의 지급 낙인(`packs`)도 든다 — 계정당 팩 1회라는 축이 같고,
 * 세이브에 두면 클라가 지워 재청구할 수 있다(이 문서는 룰에서 클라 쓰기가 봉쇄돼 있다).
 *
 * walletStore 관용구를 따른다 — `db`·`transaction`·`now` 를 전부 인자로 받고 `HttpsError` 를 모른다.
 * 세이브 문서와 별도 경로라 세이브 스키마(룰 isValidSave 의 15키)와 무관하게 승급한다.
 */

import type {
  DocumentReference,
  DocumentSnapshot,
  Firestore,
  Transaction,
} from "firebase-admin/firestore";

/** 무료 한 방 문서의 스키마 축. 세이브 SCHEMA_VERSION 과 별개로 승급한다. */
export const GRANT_SCHEMA_VERSION = 1;

/** 무료 한 방이 걸린 성장 축. 축마다 계정당 1회다. */
export type GrantAxis = "enhanceCard" | "enhanceKeyword";

/** 축별 소진 여부. true 면 그 축의 무료 한 방은 이미 썼다. */
export interface TutorialGrants {
  enhanceCard: boolean;
  enhanceKeyword: boolean;
}

/**
 * 무료 한 방 문서 참조. 세이브·지갑과 같은 유저 경로 아래 선다.
 * @param {Firestore} db 명명 DB 핸들
 * @param {string} env 환경 id
 * @param {string} uid 사용자 id
 * @return {DocumentReference} 무료 한 방 문서 참조
 */
export function grantsRef(db: Firestore, env: string, uid: string): DocumentReference {
  return db.doc(`envs/${env}/users/${uid}/grants/current`);
}

/**
 * 스냅샷에서 소진 여부를 읽는다. 문서가 없거나 필드가 깨졌으면 **미사용**으로 선다
 * — 무료 한 방을 못 읽었다고 온보딩을 막으면 낼 돈이 없는 신규 계정이 그 자리에서 멈춘다.
 * @param {DocumentSnapshot} snapshot 무료 한 방 문서 스냅샷
 * @return {TutorialGrants} 축별 소진 여부
 */
export function readGrants(snapshot: DocumentSnapshot): TutorialGrants {
  const data = snapshot.exists ? snapshot.data() : undefined;
  return {
    enhanceCard: data?.enhanceCard === true,
    enhanceKeyword: data?.enhanceKeyword === true,
  };
}

/**
 * 이 축의 무료 한 방이 남아 있는가.
 * @param {TutorialGrants} grants 축별 소진 여부
 * @param {GrantAxis} axis 성장 축
 * @return {boolean} 아직 안 썼으면 true
 */
export function hasFreeShot(grants: TutorialGrants, axis: GrantAxis): boolean {
  return !grants[axis];
}

/**
 * 이 축을 소진으로 찍는다. **자기 축만** merge 로 쓴다 — 문서 전체를 set 하면 다른 축의 낙인과
 * 팩 낙인(packs)이 함께 지워진다. 문서 단위 경합 감지는 merge 여부와 무관해서, 두 호출이
 * 겹치면 트랜잭션이 재실행되는 보장은 그대로다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {DocumentReference} ref 무료 한 방 문서 참조
 * @param {GrantAxis} axis 소진할 축
 * @param {unknown} now 서버 시각(FieldValue.serverTimestamp()) — 호출부가 넘긴다
 * @return {void}
 */
export function writeGrantUsed(
  transaction: Transaction,
  ref: DocumentReference,
  axis: GrantAxis,
  now: unknown,
): void {
  transaction.set(ref, {
    schemaVersion: GRANT_SCHEMA_VERSION,
    [axis]: true,
    updatedAt: now,
  }, {merge: true});
}

/**
 * 이 계정이 이미 받은 튜토리얼 무료 팩 목록. 필드가 없거나 맵이 아니면 **빈 집합**으로 선다
 * — readGrants 와 같은 fail-open 이고, 낙인을 못 읽었다고 지급을 막으면 온보딩이 그 자리에서 멈춘다.
 *
 * readGrants 와 갈라 두는 이유: 그쪽 반환 모양을 test-enhance.js 가 deepEqual 로 못박고 있다.
 * @param {DocumentSnapshot} snapshot 무료 한 방 문서 스냅샷
 * @return {ReadonlySet<string>} 이미 지급한 packId 집합
 */
export function readPackGrants(snapshot: DocumentSnapshot): ReadonlySet<string> {
  const packs = snapshot.exists ? snapshot.data()?.packs : undefined;
  if (packs === null || typeof packs !== "object" || Array.isArray(packs)) return new Set<string>();

  const granted = new Set<string>();
  for (const [packId, used] of Object.entries(packs as Record<string, unknown>)) {
    if (used === true) granted.add(packId);
  }
  return granted;
}

/**
 * 이 팩을 지급 완료로 찍는다. 중첩 맵을 merge 로 쓰므로 형제 packId 낙인이 살아남는다.
 *
 * packId 를 맵 키로 쓰기 때문에 Firestore 가 거부하는 이름(빈 문자열·예약 접두 `__`)과
 * 경로로 오해할 여지가 있는 이름은 낙인을 건너뛴다 — 쓰기가 던지면 지급 트랜잭션 전체가
 * 말려 들어가는데, 낙인은 지급의 부가 기록이라 그 대가를 치를 값이 아니다.
 * 건너뛴 팩은 낙인 없이 매번 지급을 다시 타지만 소유가 유니온이라 결과는 같다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {DocumentReference} ref 무료 한 방 문서 참조
 * @param {string} packId 지급한 팩 id
 * @param {unknown} now 서버 시각(FieldValue.serverTimestamp()) — 호출부가 넘긴다
 * @return {boolean} 낙인을 찍었으면 true
 */
export function writePackGranted(
  transaction: Transaction,
  ref: DocumentReference,
  packId: string,
  now: unknown,
): boolean {
  if (packId === "" || packId.startsWith("__") || packId.includes("/")) return false;

  transaction.set(ref, {
    schemaVersion: GRANT_SCHEMA_VERSION,
    packs: {[packId]: true},
    updatedAt: now,
  }, {merge: true});
  return true;
}

/**
 * 무료 한 방 문서를 통째로 지운다. QA 되감기(devResetSave)가 계정을 첫실행으로 되돌릴 때 쓴다.
 *
 * 필드를 false 로 되쓰지 않고 삭제하는 이유: merge set 으로는 `packs` 의 맵 키를 지울 수 없어
 * 전체 set 이 필요해지는데, 그러면 축끼리 서로의 낙인을 지우는 문제가 되살아난다.
 * "문서 부재 = 미사용" 은 readGrants 와 클라 TutorialGrantsCloud 가 이미 합의한 의미라,
 * 되감은 계정이 신규 계정과 같은 모양이 된다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {DocumentReference} ref 무료 한 방 문서 참조
 * @return {void}
 */
export function clearGrants(transaction: Transaction, ref: DocumentReference): void {
  transaction.delete(ref);
}
