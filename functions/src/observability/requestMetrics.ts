import {AsyncLocalStorage} from "node:async_hooks";
import {performance} from "node:perf_hooks";
import {CallableRequest, CallableResponse} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";

type RequestMetrics = {counts: Record<string, number>; phasesMs: Record<string, number>};
const active = new AsyncLocalStorage<RequestMetrics>();

// Names come from source constants, never request fields or document paths.
export function recordMetric(name: string, count = 1): void {
  const metrics = active.getStore();
  if (metrics !== undefined) metrics.counts[name] = (metrics.counts[name] ?? 0) + count;
}

// Concurrent and nested phase times overlap: their sum is NOT request latency.
export async function measurePhase<T>(name: string, run: () => T | Promise<T>): Promise<T> {
  const metrics = active.getStore();
  if (metrics === undefined) return run();
  const started = performance.now();
  try {
    return await run();
  } finally {
    metrics.phasesMs[name] = (metrics.phasesMs[name] ?? 0) + performance.now() - started;
  }
}

// Observed documents/queued writes are not a billing estimate: failed RPCs and
// Firestore internal work are not visible. Shared spec I/O belongs to its creator.
export async function withRequestMetrics<T>(command: string, run: () => T | Promise<T>): Promise<T> {
  const metrics: RequestMetrics = {counts: {}, phasesMs: {}};
  return active.run(metrics, async () => {
    const started = performance.now();
    let succeeded = 0;
    try {
      const result = await run();
      succeeded = 1;
      return result;
    } finally {
      logger.info("request_cost", {
        command, succeeded, durationMs: performance.now() - started,
        counts: metrics.counts, phasesMs: metrics.phasesMs,
      });
    }
  });
}

export function measuredCallable<Request extends CallableRequest, Result, Stream = unknown>(
  command: string,
  handler: (request: Request, response?: CallableResponse<Stream>) => Result | Promise<Result>,
): (request: Request, response?: CallableResponse<Stream>) => Promise<Result> {
  return (request, response) => withRequestMetrics(command, () => handler(request, response));
}
