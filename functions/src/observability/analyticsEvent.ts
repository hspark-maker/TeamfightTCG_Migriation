import * as logger from "firebase-functions/logger";
import {AnalyticsEventName} from "../analytics/eventNames";

export const EVENT_SCHEMA_VERSION = 1;

export type AnalyticsEventFields = {
  uid: string;
  env: string;
  eventId?: string;
  sourceCommand: string;
  result: string;
  [field: string]: unknown;
};

/**
 * 커밋이 확정된 행동 하나를 구조화 로그로 남긴다. 저장소를 쓰지 않으므로 비용은 로그뿐이다.
 *
 * 호출부 계약: **트랜잭션 밖**에서, **영수증 재생이 아닌 경우에만** 부른다
 * (트랜잭션은 재실행되고, 재생은 이미 집계된 행동이다).
 * @param {AnalyticsEventName} name 이벤트 이름(eventNames.ts 의 단일 진실원)
 * @param {AnalyticsEventFields} fields 공통 봉투 + 도메인 필드
 */
export function recordEvent(name: AnalyticsEventName, fields: AnalyticsEventFields): void {
  logger.info(name, {
    ...fields,
    event: name,
    schemaVersion: EVENT_SCHEMA_VERSION,
  });
}
