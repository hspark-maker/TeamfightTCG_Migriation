// 지갑 문서 픽스처.
//
// 진실원: functions/src/currency/walletStore.ts 의 WALLET_SCHEMA_VERSION · createWallet ·
//         writeWallet · writeReceipt (필드 이름·모양) + currency/wallet.ts 의 normalizeBalances(4키).
// 세이브 픽스처(fixtures/saveDocument.js)와 쌍둥이 대상이 다르다 — 저쪽은 클라
// PlayerSaveDocument.ToFieldMap, 이쪽은 서버 walletStore 다. 한쪽을 고쳐도 다른 쪽은 안 따라간다.
//
// 룰에는 지갑 값 검증이 없다(write: if false 라 클라 쓰기가 없다). 그래서 이 픽스처는
// "룰이 값을 본다"를 위한 게 아니라, 거부 케이스가 **실재하는 문서** 위에서 거부되는지를
// 보기 위한 seed 재료다 — 문서가 없으면 거부가 아니라 부재로 실패해 통과처럼 보인다.
import { serverTimestamp } from 'firebase/firestore';

/** walletStore.ts 의 WALLET_SCHEMA_VERSION 쌍둥이 상수. 세이브 SCHEMA_VERSION 과 별개 축이다. */
export const WALLET_SCHEMA_VERSION = 1;

/**
 * createWallet 이 만드는 첫 지갑(rev 1). balances 는 normalizeBalances 가 항상 4키로 채운다.
 * Gold 100 은 스타터 지급값(serverFreshAccountDocument 와 같은 값).
 */
export function walletDocument(_overrides = {}) {
  return {
    schemaVersion: WALLET_SCHEMA_VERSION,
    rev: 1,
    balances: { Gold: 100, Diamond: 0, Energy: 0, Shard: 0 },
    updatedAt: serverTimestamp(),
    ..._overrides,
  };
}

/**
 * writeReceipt 가 만드는 영수증 한 장. changes 는 부호 있는 증감 map, before·after 는
 * 이동 전후 4키 잔액, rev 는 이 기록 직후의 지갑 rev 다.
 * storeReceipt 는 인앱결제가 붙기 전까지 null 자리다.
 */
export function receiptDocument(_overrides = {}) {
  return {
    txId: 'tx1',
    source: 'openPack',
    changes: { Gold: -110, Diamond: 0, Energy: 0, Shard: 0 },
    before: { Gold: 110, Diamond: 0, Energy: 0, Shard: 0 },
    after: { Gold: 0, Diamond: 0, Energy: 0, Shard: 0 },
    rev: 2,
    result: null,
    storeReceipt: null,
    createdAt: serverTimestamp(),
    ..._overrides,
  };
}
