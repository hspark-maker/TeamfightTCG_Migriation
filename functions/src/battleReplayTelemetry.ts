import {DocumentReference, FieldValue, Timestamp, Transaction} from "firebase-admin/firestore";
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
 * 정산과 같은 커밋에 집계 이벤트를 남긴다. 일별 카운터의 경합은 응답 경로에서 분리한다.
 * @param {Transaction} transaction 정산 트랜잭션
 * @param {string} env 환경 id
 * @param {string} eventId 호출마다 발급하고 트랜잭션 재시도 동안 유지하는 고유 ID
 * @param {ReplayDailyDelta} delta 이번 제출이 더할 카운터
 * @return {void}
 */
export function enqueueReplayDaily(
  transaction: Transaction, env: string, eventId: string, delta: ReplayDailyDelta,
): void {
  const createdAt = Timestamp.now();
  transaction.create(db.doc(`envs/${env}/replayTelemetryEvents/${eventId}`), {
    day: new Date(createdAt.toMillis()).toISOString().slice(0, 10), delta, createdAt,
  });
}

/**
 * 카운터 증가와 이벤트 삭제를 원자적으로 처리한다. 중복 배달은 삭제된 이벤트를 보고 끝난다.
 * 실패는 트리거에 전파해 재시도하며, 미처리 이벤트는 삭제하지 않는다.
 * @param {string} env 환경 id
 * @param {DocumentReference} eventRef 집계 이벤트 문서 참조
 * @return {Promise<void>} 집계 커밋 완료
 */
export async function consumeReplayDaily(env: string, eventRef: DocumentReference): Promise<void> {
  await db.runTransaction(async (transaction) => {
    // 생성 이벤트의 사본이 아니라 현재 문서를 읽어 중복·동시 배달을 걸러 낸다.
    const snapshot = await transaction.get(eventRef);
    if (!snapshot.exists) return;
    const {day, delta} = snapshot.data() as {day: string; delta: ReplayDailyDelta};
    const increments: Record<string, unknown> = {
      day, // 배달이 자정을 넘겨도 정산 당시 UTC 날짜로 집계한다.
      updatedAt: FieldValue.serverTimestamp(),
    };
    for (const [key, value] of Object.entries(delta)) {
      if (value !== 0) increments[key] = FieldValue.increment(value);
    }
    transaction.set(replayDayRef(env, day), increments, {merge: true});
    transaction.delete(eventRef);
  });
}
