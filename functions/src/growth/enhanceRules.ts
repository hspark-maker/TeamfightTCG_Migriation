/**
 * 강화 곡선(비용·성공률)의 서버 쪽 재현. 순수(Firestore·HttpsError 모름) — 표 행을 그대로 받아 읽는다.
 *
 * 진실원은 클라 Assets/Scripts/OutGame/Growth/GrowthRules.cs 다. 화면이 보여 준 값과 실제 차감이
 * 갈리면 안 되므로, 여기 공식이 저쪽과 다르면 그건 버그다.
 *
 * 카드 비용의 두 갈래:
 *  - CardEnhance 표에 그 레벨 행이 있으면 **그 값이 이긴다**(실측 저작 25/75/150 = 클라 CostAt 과 같다).
 *  - 행이 없으면 CardEnhanceRule 의 선형 폴백 baseEnhanceCost + (N-2) × costGrowthPerLevel 이다.
 *    이 폴백은 클라의 계단 누적 곡선과 레벨 4 에서 갈린다(125 vs 150) — 그래서 전 레벨(2·3·4)이
 *    오버라이드로 저작돼 있고 폴백은 지금 도달하지 않는다. 폴백을 쓰게 되는 저작이 생기면
 *    클라 GrowthRules.CostAt 을 먼저 맞춰야 한다.
 */

import {CURRENCY_KEYS, CurrencyKey} from "../currency/currencyKeys";
import {intOf} from "../save/saveValues";
import {BASE_LEVEL} from "./cardGrowth";

/** 성공률 1000분율의 분모. */
export const PERMILLE = 1000;

/**
 * 카드 강화 상한의 천장. 클라 GrowthRules.MaxLevel(= CardSpec.MaxHpCurveLevel) 과 같다 —
 * 카드 체력 곡선이 hp2~hp4 까지만 저작되므로 표가 더 큰 값을 말해도 클라는 여기서 자른다.
 */
export const CARD_MAX_LEVEL_CEILING = 4;

/** 카드 강화 기본 결제 재화. CardEnhanceRule 표에는 재화 열이 없다(클라는 ECurrencyType.Shard 고정). */
const CARD_DEFAULT_CURRENCY: CurrencyKey = "Shard";

/** 난수원. 0 이상 max 미만의 정수를 낸다(crypto.randomInt 형태). */
export type RollFn = (max: number) => number;

/** 강화 한 스텝. 클라 GrowthStep 과 같은 뜻이다 — 체력 증가분은 서버가 쓸 일이 없어 없다. */
export interface EnhanceStep {
  level: number;
  currency: CurrencyKey;
  cost: number;
  successPermille: number;
}

/** CardEnhanceRule 표의 전역 1행. */
export interface CardEnhanceRule {
  maxLevel: number;
  baseEnhanceCost: number;
  costGrowthPerLevel: number;
}

/**
 * 재화 이름을 **엄격하게** 읽는다. 못 읽으면 축의 기본 재화로 떨어진다
 * — 여기서 Gold 로 폴백하면 조각으로 표시된 강화가 골드를 문다.
 * @param {unknown} value costCurrency 열 값
 * @param {CurrencyKey} fallback 못 읽었을 때 쓸 재화
 * @return {CurrencyKey} 재화 키
 */
function costCurrency(value: unknown, fallback: CurrencyKey): CurrencyKey {
  const lowered = String(value ?? "").trim().toLowerCase();
  return CURRENCY_KEYS.find((key) => key.toLowerCase() === lowered) ?? fallback;
}

/**
 * 카드 강화 전역 규칙. 표를 못 읽으면 null — 곡선 없이 차감하면 안 되므로 호출부가 거절한다.
 * @param {Record<string, unknown>[]} rows CardEnhanceRule 표(id 오름차순)
 * @return {CardEnhanceRule | null} 전역 규칙
 */
export function parseCardEnhanceRule(rows: Record<string, unknown>[]): CardEnhanceRule | null {
  const row = rows[0];
  if (row === undefined) return null;

  const maxLevel = intOf(row.maxLevel);
  if (maxLevel <= BASE_LEVEL) return null;

  return {
    maxLevel: maxLevel > CARD_MAX_LEVEL_CEILING ? CARD_MAX_LEVEL_CEILING : maxLevel,
    baseEnhanceCost: intOf(row.baseEnhanceCost),
    costGrowthPerLevel: intOf(row.costGrowthPerLevel),
  };
}

/**
 * 레벨별 오버라이드. 같은 레벨이 두 줄이면 id 가 작은 줄이 이긴다(행은 id 오름차순으로 들어온다).
 * @param {Record<string, unknown>[]} rows CardEnhance 표(id 오름차순)
 * @return {Map<number, EnhanceStep>} 레벨 → 스텝
 */
export function parseCardEnhanceOverrides(rows: Record<string, unknown>[]): Map<number, EnhanceStep> {
  const overrides = new Map<number, EnhanceStep>();
  for (const row of rows) {
    const level = intOf(row.level);
    // 바닥 레벨은 강화로 도달하는 레벨이 아니다(표 툴팁: 1 이하는 무시).
    if (level <= BASE_LEVEL || overrides.has(level)) continue;

    const cost = intOf(row.cost);
    overrides.set(level, {
      level,
      currency: costCurrency(row.costCurrency, CARD_DEFAULT_CURRENCY),
      cost: cost > 0 ? cost : 0,
      successPermille: PERMILLE,
    });
  }
  return overrides;
}

/**
 * 레벨 level 로 올리는 한 스텝. 범위 밖이면 null(= 만렙).
 * @param {CardEnhanceRule} rule 전역 규칙
 * @param {Map<number, EnhanceStep>} overrides 레벨별 오버라이드
 * @param {number} level 올라갈 레벨
 * @return {EnhanceStep | null} 스텝
 */
export function cardEnhanceStep(
  rule: CardEnhanceRule,
  overrides: Map<number, EnhanceStep>,
  level: number,
): EnhanceStep | null {
  if (level <= BASE_LEVEL || level > rule.maxLevel) return null;

  const override = overrides.get(level);
  // 카드 강화는 항상 성공한다. 표의 구 확률 열은 사용하지 않는다.
  if (override !== undefined) return {...override, successPermille: PERMILLE};

  const steps = level - BASE_LEVEL - 1;
  const cost = rule.baseEnhanceCost + steps * rule.costGrowthPerLevel;
  return {
    level,
    currency: CARD_DEFAULT_CURRENCY,
    cost: cost > 0 ? cost : 0,
    successPermille: PERMILLE,
  };
}

/**
 * 성공 판정. 양 끝에서는 난수를 뽑지 않는다 — 결과가 정해진 자리에서 굴리면 재현이 어려워진다.
 * @param {number} successPermille 성공률 1000분율
 * @param {RollFn} roll 난수원
 * @return {boolean} 성공이면 true
 */
export function rollSucceeded(successPermille: number, roll: RollFn): boolean {
  if (successPermille >= PERMILLE) return true;
  if (successPermille <= 0) return false;
  return roll(PERMILLE) < successPermille;
}
