import {HttpsError} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";

/**
 * 도메인 거절. **반드시 permission-denied 여야 한다.**
 *
 * 클라 CloudFailureClassifier 는 permission-denied·already-exists 만 "거절"로 보고,
 * failed-precondition·invalid-argument 는 "이 세션은 못 쓴다"로 보아 BlockSession 을 건다.
 * 잔액 부족으로 세션을 끊을 수는 없다.
 *
 * reason 은 **와이어 계약**이다 — 클라가 문자열을 그대로 enum 이름에 대조한다
 * (카드팩 "InsufficientGold" ↔ EPackOpenResult · 강화 "NotAffordable" ↔ EEnhanceOutcome).
 * 사유 목록은 도메인마다 다르므로 여기서 정하지 않는다. 각 command 가 자기 union 을 갖는다.
 *
 * 거절은 **무조건 로그를 남긴다** — 잔액 부족이냐 자격 미달이냐를 응답만 보고는 가를 수 없고,
 * 아무것도 안 남기면 functions:log 가 3~4분 늦는 것과 겹쳐 "호출이 안 왔다"로 오진하게 된다.
 * @param {string} reason 사유 코드
 * @param {string} message 로그용 설명
 * @param {Record<string, unknown>} context 어느 값에 막혔는지(가격·잔액·등급 등)
 */
export function rejectDomain(
  reason: string,
  message: string,
  context: Record<string, unknown> = {},
): never {
  logger.warn("domain rejected", {reason, ...context});
  throw new HttpsError("permission-denied", message, {reason});
}
