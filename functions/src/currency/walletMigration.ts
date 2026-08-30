/**
 * 세이브 문서의 currency 슬롯 → 지갑 문서 이관 계산. 순수(Firestore·HttpsError 모름).
 *
 * 삭제 센티널(FieldValue.delete())과 승급할 스키마 버전을 **인자로 받는다** — 이 파일이
 * `firebase-admin` 이나 save/saveDocument 를 import 하면 순수 회귀(`scripts/`)가 lib/ 를
 * 직접 require 하는 관용구가 깨지고, SCHEMA_VERSION 이 두 곳에 생긴다.
 *
 * 쓰기는 하지 않는다 — 지갑 생성(createWallet)과 세이브 갱신은 한 트랜잭션 안에서
 * 호출부가 묶는다. 그래야 "잔액은 지웠는데 지갑이 없는" 중간 상태가 없다.
 */

import {Balances, readBalances} from "./wallet";

/**
 * 세이브 문서에 얹을 갱신 값. save/saveDocument 의 SlotPatch 보다 넓다
 * — 슬롯 전체 값이 아니라 삭제 센티널·스칼라(schemaVersion)를 싣기 때문이다.
 */
export type SaveMigrationPatch = Record<string, unknown>;

/** 이관 계산 결과. */
export interface WalletMigration {
  balances: Balances;
  slotPatch: SaveMigrationPatch;
}

/**
 * 세이브 문서 현재값에서 지갑 초기 잔액과 세이브 쪽 패치를 낸다.
 *
 * 멱등하다 — 이미 이관된 문서(currency 없음)를 넣어도 같은 모양이 나온다. 다만 그때
 * balances 는 4키 0 이므로, 지갑이 두 번 서지 않도록 막는 것은 createWallet 의
 * `transaction.create` 다(이 함수가 아니다).
 * @param {Record<string, unknown>} current 세이브 문서 현재값
 * @param {unknown} deleteSentinel 필드 삭제 값(호출부가 FieldValue.delete() 를 넘긴다)
 * @param {number} schemaVersion 승급할 세이브 스키마 버전(save/saveDocument.SCHEMA_VERSION)
 * @return {WalletMigration} 지갑 초기 잔액과 세이브 패치
 */
export function migrateFromSaveSlot(
  current: Record<string, unknown>,
  deleteSentinel: unknown,
  schemaVersion: number,
): WalletMigration {
  return {
    balances: readBalances(current?.currency),
    slotPatch: {
      currency: deleteSentinel,
      schemaVersion,
    },
  };
}
