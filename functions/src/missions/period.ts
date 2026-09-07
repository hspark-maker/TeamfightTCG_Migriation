/**
 * 미션의 일일·주간 경계를 정하는 유일한 파일.
 *
 * 서버에 스케줄러가 없다(functions/src/index.ts 는 전부 onCall 이다). 그래서 리셋은 cron 이 아니라
 * **문서를 만지는 자리에서 period 키를 대조하는 lazy 방식**이다. 유저가 안 들어오면 리셋도 안 일어나고,
 * 그래도 맞다 — 안 들어온 사이의 미션은 존재하지 않는 것과 같다.
 *
 * 클라가 남은 시간을 그릴 때도 이 규칙을 따라야 하지만, **진실원은 서버의 이 파일 하나**다.
 * 클라가 자기 기기 시계로 재면 시계를 돌린 유저에게 표시와 실제가 갈린다.
 */

/** 리셋 기준 시간대. 한국 기준 서비스라 UTC 가 아니라 KST 로 자른다. */
const KST_OFFSET_MINUTES = 9 * 60;

/** 기획상 리셋 시각대. 계산은 고정 오프셋이지만 운영 계약은 IANA 이름으로 남긴다. */
export const MISSION_TIME_ZONE = "Asia/Seoul";

/**
 * 하루가 갈리는 시각(KST). 자정이 아니라 새벽 5시다 — 자정에 자르면 밤에 노는 유저가
 * 플레이 도중 미션이 갈아엎히는 것을 본다.
 */
const RESET_HOUR_KST = 5;

const MINUTE_MS = 60 * 1000;
const HOUR_MS = 60 * MINUTE_MS;
const DAY_MS = 24 * HOUR_MS;

/** 이번 기간을 가리키는 두 키. 값이 달라진 순간이 리셋 시점이다. */
export interface MissionPeriod {
  /** 일일 키. `2026-09-04` 모양. */
  daily: string;
  /** 주간 키. ISO 주차 대신 "그 주 월요일의 일일 키"를 쓴다 — 연말 주차 규칙을 끌어들이지 않는다. */
  weekly: string;
  /** 다음 일일 리셋 시각(epoch ms, UTC). 클라 카운트다운의 근거. */
  dailyResetAtMs: number;
  /** 다음 주간 리셋 시각(epoch ms, UTC). */
  weeklyResetAtMs: number;
}

/**
 * KST 기준 "리셋 하루"의 시작을 UTC epoch ms 로 돌려준다.
 * @param {number} nowMs 현재 시각(epoch ms)
 * @return {number} 이 시각이 속한 리셋 하루의 시작(epoch ms)
 */
function dayStartMs(nowMs: number): number {
  // KST 로 옮긴 뒤 리셋 시각만큼 더 당겨, 05:00 경계가 정수 일 경계에 오게 만든다.
  const shifted = nowMs + KST_OFFSET_MINUTES * MINUTE_MS - RESET_HOUR_KST * HOUR_MS;
  return Math.floor(shifted / DAY_MS) * DAY_MS - KST_OFFSET_MINUTES * MINUTE_MS + RESET_HOUR_KST * HOUR_MS;
}

/**
 * 리셋 하루의 시작을 `YYYY-MM-DD` 로 적는다. 표기는 KST 날짜다 —
 * UTC 로 적으면 05:00 이전 구간이 전날로 보여 로그를 읽는 사람이 매번 하루를 틀린다.
 * @param {number} dayStart 리셋 하루의 시작(epoch ms)
 * @return {string} `YYYY-MM-DD`
 */
function dayLabel(dayStart: number): string {
  // 경계 자체를 KST 로 옮기면 정확히 05:00 이 되므로, 그 날짜를 그대로 읽는다.
  const kst = new Date(dayStart + KST_OFFSET_MINUTES * MINUTE_MS);
  const year = kst.getUTCFullYear();
  const month = String(kst.getUTCMonth() + 1).padStart(2, "0");
  const day = String(kst.getUTCDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

/**
 * 이 시각이 속한 일일·주간 기간을 계산한다. 순수 함수다 — 호출부가 `nowMs` 를 넘긴다.
 *
 * 트랜잭션 콜백은 재실행될 수 있으므로 `Date.now()` 를 콜백 안에서 부르면 재실행마다 값이 달라진다.
 * 경계에 걸린 한 번이 기간을 갈아타면 카운터가 어느 기간에 실릴지가 재실행 운에 달린다 —
 * 그래서 호출부(callable 본문)가 **한 번만** 재고 그 값을 넘겨야 한다.
 * @param {number} nowMs 현재 시각(epoch ms)
 * @return {MissionPeriod} 기간 키와 다음 리셋 시각
 */
export function missionPeriod(nowMs: number): MissionPeriod {
  const today = dayStartMs(nowMs);

  // 주간은 월요일 05:00 KST 에 갈린다. KST 로 옮긴 요일을 본다(0=일요일).
  const kstWeekday = new Date(today + KST_OFFSET_MINUTES * MINUTE_MS).getUTCDay();
  const sinceMonday = (kstWeekday + 6) % 7;
  const weekStart = today - sinceMonday * DAY_MS;

  return {
    daily: dayLabel(today),
    weekly: dayLabel(weekStart),
    dailyResetAtMs: today + DAY_MS,
    weeklyResetAtMs: weekStart + 7 * DAY_MS,
  };
}
