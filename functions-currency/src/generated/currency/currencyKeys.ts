/**
 * 전역 재화(클라 ECurrencyType)의 키 목록과 상한. 순수 — Firestore 도 HttpsError 도 모른다.
 *
 * **firestore.rules 에 대응 검사는 없다.** 지갑 문서는 `allow write: if false` 라 클라가 아예
 * 쓰지 못하고, 세이브 문서에서 `currency` 는 오히려 금지 필드다. 키를 더할 때 룰을 고치러 가지 마라
 * — 예전 주석이 `balances.keys().hasOnly([...])` 와 맞추라고 적어 두었으나 그 검사는 실재하지 않는다.
 * 여기에 키를 더하면 normalize 가 그 키를 0 으로 세우고, 지갑 문서에 그대로 실린다.
 */

/** 클라 ECurrencyType 의 이름. 지갑 잔액에 실리는 키는 이 목록이 전부다. */
export const CURRENCY_KEYS = ["Gold", "Diamond", "Energy", "Shard", "RouletteTicket"] as const;

/** 재화 키 하나. */
export type CurrencyKey = typeof CURRENCY_KEYS[number];

/** 재화 하나에 두는 상한. */
export const CURRENCY_MAX = 1000000000000;

/**
 * 재화 이름을 정규화한다. 클라 CardPackData.ParseCurrency 재현이다
 * — 대소문자를 안 가리고, 못 읽으면 Gold 로 떨어진다(팩 가격은 오타여도 화면이 서야 한다).
 *
 * **지급 재화를 정하는 자리에서는 쓰지 마라** — Gold 폴백이 저작 오타를 조용히 금화로 바꾼다.
 * 그런 자리는 CURRENCY_KEYS.find 로 정확히 대조하고 못 찾으면 거절한다(claimBattleReward 관용구).
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
