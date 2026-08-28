/**
 * 전역 재화 지갑 — 잔액 읽기·여력 판정·차감·지급. 순수(Firestore·HttpsError 모름).
 *
 * 재화가 오가는 산술은 changeBalances 하나뿐이다. 거기서 [0, CURRENCY_MAX] 로 자르는 것이 핵심인데,
 * 서버는 Admin SDK 라 룰을 우회하므로 상한을 넘긴 문서를 쓰면 그 계정의 이후 클라 저장이
 * 전부 PERMISSION_DENIED 가 된다.
 *
 * 거절은 여기서 정하지 않는다 — 도메인마다 클라가 파싱하는 사유 코드가 다르므로
 * command 가 canAfford 로 묻고 save/domainReject 로 던진다.
 */

import {CURRENCY_KEYS, CURRENCY_MAX, CurrencyKey} from "./currencyKeys";
import {intOf} from "../save/saveValues";

/** 재화 4키 잔액. */
export type Balances = Record<string, number>;

/** 재화 획득 한 건. 클라 CurrencyGain 과 같은 뜻이라 이름을 맞춘다(양수만 뜻이 있다). */
export interface CurrencyGain {
  currency: CurrencyKey;
  amount: number;
}

/**
 * 4키로 다시 짓는다. 부분 입력이면 빠진 키가 0 이고 모르는 키는 버린다.
 * 지갑 문서 코덱(currency/walletStore)이 슬롯 밖에서도 같은 정규화를 써야 해서 공개한다.
 * @param {Balances} balances 잔액(부분·오염 가능)
 * @return {Balances} 4키 잔액
 */
export function normalizeBalances(balances: Balances): Balances {
  return normalize(balances);
}

/**
 * 4키로 다시 짓는다. 부분 입력이면 빠진 키가 0 이고 **모르는 키는 버린다**
 * — 룰의 balances.hasOnly 가 정확히 4키를 요구하기 때문이다.
 * @param {Balances} balances 잔액(부분·오염 가능)
 * @return {Balances} 4키 잔액
 */
function normalize(balances: Balances): Balances {
  const next: Balances = {};
  for (const key of CURRENCY_KEYS) {
    const value = intOf(balances[key]);
    next[key] = value < 0 ? 0 : Math.min(value, CURRENCY_MAX);
  }
  return next;
}

/**
 * 재화 증감의 **유일한 산술 지점**. amount 는 부호가 있다(차감은 음수).
 * @param {Balances} balances 잔액
 * @param {CurrencyGain[]} changes 증감 목록
 * @return {Balances} 갱신된 4키 잔액
 */
function changeBalances(balances: Balances, changes: CurrencyGain[]): Balances {
  const next = normalize(balances);
  for (const change of changes) {
    const moved = next[change.currency] + intOf(change.amount);
    next[change.currency] = moved < 0 ? 0 : Math.min(moved, CURRENCY_MAX);
  }
  return next;
}

/**
 * 세이브 문서의 currency 슬롯에서 잔액을 읽는다. v8 부터 잔액의 진실원은 지갑 문서라
 * 남은 호출자는 이관(currency/walletMigration) 하나뿐이다.
 * @param {unknown} currency 문서의 currency 슬롯
 * @return {Balances} 4키 잔액
 */
export function readBalances(currency: unknown): Balances {
  const source = (currency as {balances?: Balances} | undefined)?.balances ?? {};
  return normalize(source);
}

/**
 * 낼 수 있는가.
 * @param {Balances} balances 잔액
 * @param {CurrencyKey} currency 결제 재화
 * @param {number} amount 가격
 * @return {boolean} 잔액이 가격 이상이면 true
 */
export function canAfford(balances: Balances, currency: CurrencyKey, amount: number): boolean {
  return normalize(balances)[currency] >= amount;
}

/**
 * 차감. 여력 검사는 호출부가 이미 끝냈다 — 여기서는 하한 0 으로만 자른다.
 * @param {Balances} balances 결제 전 잔액
 * @param {CurrencyKey} currency 결제 재화
 * @param {number} amount 가격
 * @return {Balances} 결제 후 잔액
 */
export function spend(balances: Balances, currency: CurrencyKey, amount: number): Balances {
  return changeBalances(balances, [{currency, amount: -amount}]);
}

/**
 * 지급(다건). 보상 하나에 재화가 여럿 걸려도 호출 한 번으로 끝난다.
 * @param {Balances} balances 지급 전 잔액
 * @param {CurrencyGain[]} gains 획득 목록(양수만 반영한다)
 * @return {Balances} 지급 후 잔액
 */
export function grant(balances: Balances, gains: CurrencyGain[]): Balances {
  return changeBalances(balances, gains.filter((gain) => gain.amount > 0));
}
