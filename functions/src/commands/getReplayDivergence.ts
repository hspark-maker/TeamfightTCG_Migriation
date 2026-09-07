import {HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {isKnownEnv} from "../save/environments";
import {MAX_REPLAY_DAYS, replayDayRef, utcDay} from "../battleReplayTelemetry";

// 발산 실측 조회 창구. 매치 문서를 훑지 않고 일별 카운터만 읽는다 —
// 이 수치가 재생 권위를 켜고 끄는(릴리즈 관리 창) 유일한 판단 근거다.

function counter(data: Record<string, unknown> | undefined, key: string): number {
  const value = data?.[key];
  return typeof value === "number" ? value : 0;
}

export const getReplayDivergence = onCall({enforceAppCheck: false}, async (request) => {
  if (!request.auth) throw new HttpsError("unauthenticated", "authentication required");
  if (request.auth.token.admin !== true) throw new HttpsError("permission-denied", "admin claim required");

  const raw = request.data as Record<string, unknown> | null;
  const env = raw?.env;
  const requestedDays = raw?.days;
  if (typeof env !== "string" || !isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", "known env required");
  }
  const days = requestedDays == null ? 7 : requestedDays;
  if (!Number.isInteger(days) || (days as number) < 1 || (days as number) > MAX_REPLAY_DAYS) {
    throw new HttpsError("invalid-argument", `days must be 1..${MAX_REPLAY_DAYS}`);
  }

  const ids = Array.from({length: days as number}, (_, index) => utcDay(index));
  const snapshots = await db.getAll(...ids.map((day) => replayDayRef(env, day)));
  return {
    env,
    days: snapshots.map((snapshot, index) => {
      const data = snapshot.data();
      return {
        day: ids[index],
        settled: counter(data, "settled"),
        replayOk: counter(data, "replayOk"),
        replayFailed: counter(data, "replayFailed"),
        unavailable: counter(data, "unavailable"),
        divergent: counter(data, "divergent"),
        outcomeMismatch: counter(data, "outcomeMismatch"),
        hashMismatch: counter(data, "hashMismatch"),
      };
    }),
  };
});
