import * as logger from "firebase-functions/logger";
import {Transaction} from "firebase-admin/firestore";
import {db} from "../firebaseApp";

type AttemptMetrics = {
  reads: number;
  writes: number;
};

type AnyMethod = (...args: unknown[]) => unknown;

function createCountedTransaction(
  raw: Transaction,
  attempt: AttemptMetrics,
  onRead: (count: number) => void,
): Transaction {
  const proxy: Transaction = new Proxy(raw, {
    get(target, property) {
      if (property === "get") {
        return async (...args: unknown[]) => {
          const method = target.get as unknown as AnyMethod;
          const result = await method.apply(target, args);
          const querySize = (result as {size?: unknown})?.size;
          const count = typeof querySize === "number" ? Math.max(1, querySize) : 1;
          attempt.reads += count;
          onRead(count);
          return result;
        };
      }

      if (property === "getAll") {
        return async (...args: unknown[]) => {
          const method = target.getAll as unknown as AnyMethod;
          const result = await method.apply(target, args) as unknown[];
          attempt.reads += result.length;
          onRead(result.length);
          return result;
        };
      }

      if (property === "create" || property === "set" || property === "update" || property === "delete") {
        return (...args: unknown[]) => {
          attempt.writes++;
          const method = Reflect.get(target, property, target) as unknown as AnyMethod;
          method.apply(target, args);
          return proxy;
        };
      }

      const value = Reflect.get(target, property, target);
      return typeof value === "function" ? value.bind(target) : value;
    },
  }) as Transaction;
  return proxy;
}

/**
 * 도메인 거절인가. permission-denied · already-exists 두 코드만 도메인 축으로 예약돼 있다
 * (failed-precondition 은 스키마 드리프트·문서 없음에 이미 쓰여 세션 문제와 못 가른다).
 * @param {unknown} error 트랜잭션이 던진 값
 * @return {boolean} 정상 거절이면 true
 */
function isDomainRejection(error: unknown): boolean {
  const code = (error as {code?: unknown})?.code;
  return code === "permission-denied" || code === "already-exists";
}

export async function withCountedTransaction<T>(
  command: string,
  run: (transaction: Transaction) => Promise<T>,
  extra: Record<string, unknown> = {},
): Promise<T> {
  const startedAtMs = Date.now();
  let attempts = 0;
  let lastAttempt: AttemptMetrics = {reads: 0, writes: 0};
  let totalObservedReads = 0;

  try {
    const result = await db.runTransaction(async (raw) => {
      attempts++;
      const attempt: AttemptMetrics = {reads: 0, writes: 0};
      lastAttempt = attempt;
      const transaction = createCountedTransaction(
        raw,
        attempt,
        (count) => {
          totalObservedReads += count;
        },
      );
      const value = await run(transaction);
      return value;
    });

    logger.info("tx_cost", {
      ...extra,
      command,
      status: "committed",
      attempts,
      lastAttemptReads: lastAttempt.reads,
      lastAttemptWrites: lastAttempt.writes,
      totalObservedReads,
      durationMs: Date.now() - startedAtMs,
    });
    return result;
  } catch (error) {
    // 도메인 거절은 실패가 아니라 정상 결과다(재화 부족·이미 수령 …). warn 으로 찍으면
    // 평범한 플레이가 경보 대상으로 올라오고, 진짜 인프라 실패가 그 잡음에 묻힌다.
    // 축은 클라 CloudFailureClassifier 가 예약해 둔 두 코드와 같게 맞춘다 —
    // 여기서 다른 기준을 세우면 같은 사건을 서버와 클라가 다르게 부른다.
    const rejected = isDomainRejection(error);
    const payload = {
      ...extra,
      command,
      status: rejected ? "rejected" : "failed",
      attempts,
      lastAttemptReads: lastAttempt.reads,
      lastAttemptWrites: lastAttempt.writes,
      totalObservedReads,
      durationMs: Date.now() - startedAtMs,
      error: error instanceof Error ? error.message : String(error),
    };
    if (rejected) logger.info("tx_cost", payload);
    else logger.warn("tx_cost", payload);
    throw error;
  }
}
