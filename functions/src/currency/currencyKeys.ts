/**
 * 전역 재화(클라 ECurrencyType)의 키 목록과 상한. 순수 — Firestore 도 HttpsError 도 모른다.
 *
 * 이 4키는 firestore.rules 의 isValidSave 가 `balances.keys().hasOnly([...])` 로 못박은 것과
 * **같은 목록이어야 한다.** 갈리면 그 계정의 이후 모든 클라 저장이 영구 거부되고,
 * 룰은 delete: if false 라 복구 경로가 없다.
 */

/** 클라 ECurrencyType 의 이름. CurrencyCode.TryParse 가 통과시키는 값이 이 넷뿐이다. */
export const CURRENCY_KEYS = ["Gold", "Diamond", "Energy", "Shard"] as const;

/** 재화 키 하나. */
export type CurrencyKey = typeof CURRENCY_KEYS[number];

/** 룰 isValidSave 가 재화 하나에 거는 상한. */
export const CURRENCY_MAX = 1000000000000;

/**
 * 재화 이름을 정규화한다. 클라 CardPackData.ParseCurrency 재현이다
 * — 대소문자를 안 가리고, 못 읽으면 Gold 로 떨어진다(팩 가격은 오타여도 화면이 서야 한다).
 *
 * packs/rankGrade.ts 의 GRADE_KEYS 에도 "Gold"·"Diamond" 가 있지만 저긴 랭크 등급 축이라
 * 다른 것이다 — 두 목록을 합치지 마라.
 * @param {string} value priceType·refundType 열 값
 * @return {CurrencyKey} 재화 키
 */
export function parseCurrency(value: string): CurrencyKey {
  const lowered = value.trim().toLowerCase();
  const key = CURRENCY_KEYS.find((k) => k.toLowerCase() === lowered);
  return key ?? "Gold";
}
