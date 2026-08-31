// CardLimitBreak 표 해석 — 한계돌파 단계별 **누적** 체력 가산과 간식 비용의 서버 단일 진실원.
// 상한의 진실원은 이 표가 아니라 CardEnhanceRule.maxLimitBreak 다(표 툴팁 규약).
//
// 순수 모듈 제약: firebase-admin · HttpsError 를 들이지 마라. functions/scripts 의 회귀가
// lib/ 를 직접 require 하고 돈다.

import {intOf} from "../save/saveValues";

/** 한계돌파 한 단계. hpGain 은 그 단계에서 **더해지는** 몫이다(총량이 아니다). */
export interface LimitBreakStep {
  stage: number;
  hpGain: number;
  snackCost: number;
}

/** 단계 1..maxStage 가 빈틈없이 채워진 곡선. */
export interface LimitBreakCurve {
  maxStage: number;
  steps: Map<number, LimitBreakStep>;
}

/**
 * CardLimitBreak 표를 곡선으로 읽는다. 표를 못 읽으면 null — 곡선 없이 간식을 물릴 수는 없다.
 *
 * readSpecRows 는 id 로만 정렬하므로 **stage 순서를 가정하지 않는다**. 행을 훑어 stage 키 맵에 넣고,
 * 같은 stage 가 둘이면 먼저 온 행(id 가 작은 쪽)이 이긴다(parseCardEnhanceOverrides 와 같은 규약).
 *
 * 연속성 검사가 fail-closed 다: hpGain 이 누적이라 중간에 구멍이 나면 그 위 단계의 합이 뜻을 잃는다.
 * 1 부터 끊기지 않는 최장 구간까지로 maxStage 를 깎아 돌려주고, stage 1 이 없으면 null 이다.
 * @param {Record<string, unknown>[]} rows CardLimitBreak 표(id 오름차순)
 * @param {number} maxStage CardEnhanceRule.maxLimitBreak 가 말하는 상한
 * @return {LimitBreakCurve | null} 곡선
 */
export function parseLimitBreakCurve(
  rows: Record<string, unknown>[],
  maxStage: number,
): LimitBreakCurve | null {
  if (maxStage <= 0) return null;

  const steps = new Map<number, LimitBreakStep>();
  for (const row of rows) {
    const stage = intOf(row.stage);
    if (stage <= 0 || stage > maxStage || steps.has(stage)) continue;

    steps.set(stage, {
      stage,
      hpGain: Math.max(0, intOf(row.hpGain)),
      // 표 툴팁: 1 미만은 1로 올라간다. 클램프가 없으면 공란 저작이 공짜 한계돌파가 된다.
      snackCost: Math.max(1, intOf(row.snackCost)),
    });
  }

  let continuous = 0;
  while (steps.has(continuous + 1)) continuous++;
  if (continuous === 0) return null;

  // 구멍 위쪽 단계는 통째로 버린다 — 곡선이 말하는 상한과 맵의 내용이 갈리면 안 된다.
  for (const stage of [...steps.keys()]) {
    if (stage > continuous) steps.delete(stage);
  }

  return {maxStage: continuous, steps};
}

/**
 * 단계 stage 로 올리는 한 스텝. 범위 밖이면 null(= 최대 단계).
 * @param {LimitBreakCurve} curve 곡선
 * @param {number} stage 올라갈 단계
 * @return {LimitBreakStep | null} 스텝
 */
export function limitBreakStep(curve: LimitBreakCurve, stage: number): LimitBreakStep | null {
  const step = curve.steps.get(stage);
  return step === undefined ? null : {...step};
}

/**
 * 그 단계까지의 체력 가산 **누적합**. 표 툴팁의 "3단계 카드는 1~3단계 합을 받는다"가 이 함수다.
 * 곡선 상한 위 단계는 상한까지만 센다(음수 단계는 0).
 * @param {LimitBreakCurve} curve 곡선
 * @param {number} stage 현재 단계
 * @return {number} 체력 가산
 */
export function limitBreakHpBonus(curve: LimitBreakCurve, stage: number): number {
  const top = Math.min(stage, curve.maxStage);
  let result = 0;
  for (let current = 1; current <= top; current++) {
    result += curve.steps.get(current)?.hpGain ?? 0;
  }
  return result;
}
