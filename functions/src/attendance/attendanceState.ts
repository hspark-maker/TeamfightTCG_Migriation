import {missionPeriod} from "../missions/period";

export const ATTENDANCE_DAYS = 7;

export interface AttendanceState {
  cycle: number;
  claimedDays: number;
  lastClaimDailyKey: string;
}

export interface AttendanceResponse {
  dailyKey: string;
  nextResetAtMs: number;
  serverNowMs: number;
  cycle: number;
  claimedDays: number;
  claimDay: number;
  canClaim: boolean;
}

export interface AttendanceClaimRequest {
  dailyKey: string;
  cycle: number;
  day: number;
}

/**
 * Missing documents represent a new account; malformed stored progress must never grant again.
 * @param {Record<string, unknown> | undefined} raw Stored attendance document.
 * @return {AttendanceState} Validated progress, or initial progress when absent.
 */
export function readAttendance(raw: Record<string, unknown> | undefined): AttendanceState {
  if (raw === undefined) return {cycle: 1, claimedDays: 0, lastClaimDailyKey: ""};
  const {cycle, claimedDays, lastClaimDailyKey} = raw;
  if (!Number.isSafeInteger(cycle) || Number(cycle) < 1 ||
      !Number.isSafeInteger(claimedDays) || Number(claimedDays) < 0 || Number(claimedDays) > ATTENDANCE_DAYS ||
      typeof lastClaimDailyKey !== "string" ||
      (Number(claimedDays) > 0 ? !/^\d{4}-\d{2}-\d{2}$/.test(lastClaimDailyKey) : lastClaimDailyKey !== "")) {
    throw new Error("Attendance progress is invalid.");
  }
  return {cycle: Number(cycle), claimedDays: Number(claimedDays), lastClaimDailyKey};
}

export function attendanceResponse(state: AttendanceState, nowMs: number): AttendanceResponse {
  const period = missionPeriod(nowMs);
  const resetCycle = state.claimedDays === ATTENDANCE_DAYS && state.lastClaimDailyKey < period.daily;
  const claimedDays = resetCycle ? 0 : state.claimedDays;
  return {
    dailyKey: period.daily,
    nextResetAtMs: period.dailyResetAtMs,
    serverNowMs: nowMs,
    cycle: state.cycle + (resetCycle ? 1 : 0),
    claimedDays,
    claimDay: Math.min(claimedDays + 1, ATTENDANCE_DAYS),
    // A transaction retry from yesterday must not overwrite a newer committed claim.
    canClaim: state.lastClaimDailyKey < period.daily && claimedDays < ATTENDANCE_DAYS,
  };
}

export type AttendanceVerdict = {allow: true; state: AttendanceState} |
  {allow: false; reason: "AttendanceStale" | "AlreadyClaimed"};

export function judgeAttendanceClaim(
  state: AttendanceState, request: AttendanceClaimRequest, nowMs: number,
): AttendanceVerdict {
  const view = attendanceResponse(state, nowMs);
  if (request.dailyKey !== view.dailyKey || request.cycle !== view.cycle) {
    return {allow: false, reason: "AttendanceStale"};
  }
  if (!view.canClaim) return {allow: false, reason: "AlreadyClaimed"};
  if (request.day !== view.claimDay) return {allow: false, reason: "AttendanceStale"};
  return {allow: true, state: {
    cycle: view.cycle, claimedDays: view.claimDay, lastClaimDailyKey: view.dailyKey,
  }};
}
