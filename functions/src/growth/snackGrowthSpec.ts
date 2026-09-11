import {HttpsError} from "firebase-functions/v2/https";
import {parseCardEnhanceRule} from "./enhanceRules";
import {LimitBreakCurve, parseLimitBreakCurve} from "./limitBreakTable";

/**
 * 자동 성장은 수동 한계돌파의 표 해석을 공유한다. 정의 누락은 지급 전체를 실패시킨다.
 * @param {Array} ruleRows CardEnhanceRule 표
 * @param {Array} curveRows CardLimitBreak 표
 * @return {LimitBreakCurve} 코드 상한까지 검증한 연속 곡선
 */
export function requireSnackGrowthCurve(
  ruleRows: Record<string, unknown>[], curveRows: Record<string, unknown>[],
): LimitBreakCurve {
  const rule = parseCardEnhanceRule(ruleRows);
  const curve = rule === null ? null : parseLimitBreakCurve(curveRows, rule.maxLimitBreak);
  // 중간 정의 누락을 정상 최대 단계로 오인해 간식만 쌓지 않는다.
  if (rule === null || curve === null || curve.maxStage !== rule.maxLimitBreak) {
    throw new HttpsError("failed-precondition", "RuleUnavailable: snack growth rule or curve is not authored.");
  }
  return curve;
}
