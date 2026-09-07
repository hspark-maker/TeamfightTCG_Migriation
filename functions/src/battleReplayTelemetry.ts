import * as logger from "firebase-functions/logger";
import {DocumentReference, FieldValue} from "firebase-admin/firestore";
import {db} from "./firebaseApp";

export type ReplayDailyDelta = {
  settled: number;
  replayOk: number;
  replayFailed: number;
  unavailable: number;
  divergent: number;
  outcomeMismatch: number;
  hashMismatch: number;
};

const DAY_MS = 24 * 60 * 60 * 1000;
/** 조회 가능한 최대 일수. 하루 문서 1개라 이 값이 곧 getAll 의 문서 수다. */
export const MAX_REPLAY_DAYS = 31;

/**
 * UTC 기준 날짜 키. 집계 문서 ID 이자 문서 안의 day 필드다.
 * @param {number} offset 오늘로부터 며칠 전인지(0 = 오늘)
 * @return {string} yyyy-MM-dd
 */
export function utcDay(offset = 0): string {
  return new Date(Date.now() - offset * DAY_MS).toISOString().slice(0, 10);
}

/**
 * 일별 집계 문서 참조.
 * @param {string} env 환경 id
 * @param {string} day utcDay 가 만든 yyyy-MM-dd
 * @return {DocumentReference} 해당 일자 카운터 문서
 */
export function replayDayRef(env: string, day: string): DocumentReference {
  // Firestore는 collection/document 교대가 필요하므로 replayDaily 아래에 days 컬렉션을 둔다.
  return db.doc(`envs/${env}/telemetry/replayDaily/days/${day}`);
}

/**
 * 정산 트랜잭션과 분리된 best-effort 집계다. 실패해도 보상 정산은 되돌리지 않는다.
 * @param {string} env 환경 id
 * @param {ReplayDailyDelta} delta 이번 제출이 더할 카운터
 * @return {Promise<void>} 실패는 로그로만 남는다
 */
export async function recordReplayDaily(env: string, delta: ReplayDailyDelta): Promise<void> {
  try {
    const increments: Record<string, unknown> = {
      day: utcDay(),
      updatedAt: FieldValue.serverTimestamp(),
    };
    for (const [key, value] of Object.entries(delta)) {
      if (value !== 0) increments[key] = FieldValue.increment(value);
    }
    await replayDayRef(env, increments.day as string).set(increments, {merge: true});
  } catch (error) {
    logger.error("battle_replay_telemetry_write_failed", {env, delta, error});
  }
}
