/**
 * 미션 문서(`envs/{env}/users/{uid}/missions/current`)를 아는 유일한 파일.
 *
 * 세이브 슬롯이 아니라 별도 문서인 이유: `save/current` 는 `revision +1` 강제 · `SCHEMA_VERSION` 검사 ·
 * 룰의 `isValidSave` 를 받는다. 미션 카운터 하나 올리자고 세이브 revision 을 태우면 다른 기기와의
 * 충돌 축이 하나 늘고, 그 충돌은 진행도가 아니라 **세이브 전체**를 막는다.
 *
 * `growth/tutorialGrants` 관용구를 따른다 — `db`·`transaction`·`now` 를 전부 인자로 받고 `HttpsError` 를 모른다.
 *
 * ## 읽기·쓰기 순서 (이 파일이 두 단계로 갈린 이유)
 * Firestore 트랜잭션은 **모든 읽기가 모든 쓰기보다 앞**이어야 한다. `mutateSave` 의 무조건 읽기
 * 3개(save · wallet · receipt)는 콜백 전에 끝나지만, `enhanceCard` 처럼 콜백 안에서 또 읽는 명령이 있다.
 * 그래서 미션도 **읽기는 콜백 맨 앞(beginMissionBump), 쓰기는 콜백 맨 끝(commitMissionBump)** 으로 갈라 둔다.
 * 한 함수로 뭉치면 그 함수 뒤에 오는 다른 읽기가 순서를 깬다.
 *
 * ## 중복 증가를 막는 것은 이 파일이 아니다
 * `mutateSave` 는 영수증이 히트하면 **콜백 자체를 건너뛰고** 캐시된 응답을 돌려준다.
 * 따라서 bump 를 콜백 **안**에 두는 한 재시도는 진행도를 두 번 올리지 않는다.
 * 콜백 밖(callable 본문)으로 옮기는 순간 그 보장이 사라진다.
 */

import type {
  DocumentReference,
  DocumentSnapshot,
  Firestore,
  Transaction,
} from "firebase-admin/firestore";
import {
  enabledMissions,
  MISSION_ID_PREFIX,
  MissionDef,
  MissionPeriodKind,
} from "./catalog";
import type {MissionPeriod} from "./period";

/** 미션 문서의 스키마 축. 세이브 SCHEMA_VERSION 과 별개로 승급한다. */
export const MISSION_SCHEMA_VERSION = 1;

/** 일반 일일 미션의 달성 수로 계산하는 파생 이벤트. 저장 카운터는 신뢰하지 않는다. */
export const DAILY_MISSION_COMPLETION_EVENT = "CompleteDailyMissions";
/** 일반 주간 미션의 달성 수로 계산하는 파생 이벤트. */
export const WEEKLY_MISSION_COMPLETION_EVENT = "CompleteWeeklyMissions";

/** 카운터 한 축의 상한. 저작 실수나 폭주가 있어도 문서가 무한히 커지지 않게 자른다. */
const COUNTER_MAX = 1000000;

/** 패스 경험치 상한. 같은 이유의 안전망이고, 패스가 붙을 때 실제 곡선이 이 아래여야 한다. */
const PASS_EXP_MAX = 100000000;

/** 미션 문서의 값. 문서가 없으면 이 모양의 빈 상태로 선다. */
export interface MissionState {
  dailyKey: string;
  weeklyKey: string;
  /** `daily.<event>` / `weekly.<event>` 키를 쓰는 단일 진행도 맵. */
  progress: Record<string, number>;
  /** 미션 id별 수령 낙인. */
  claimed: Record<string, boolean>;
  /** 배틀패스는 이번 범위 밖이다 — 자리만 두고 아무도 올리지 않는다. */
  passExp: number;
}

/** 기존 커맨드 응답에 선택 필드로 싣는 미션 상태. serverTimestamp는 문서에만 쓴다. */
export interface MissionResponse {
  dailyKey: string;
  weeklyKey: string;
  progress: Record<string, number>;
  claimed: Record<string, boolean>;
  passExp: number;
  dailyResetAtMs: number;
  weeklyResetAtMs: number;
}

/** 콜백 맨 앞에서 읽어 둔 상태. 쓰기는 이 손잡이로만 한다. */
export interface MissionBump {
  ref: DocumentReference;
  /** 기간 리셋까지 반영한 상태. */
  state: MissionState;
  period: MissionPeriod;
}

/**
 * 미션 문서 참조. 세이브·지갑과 같은 유저 경로 아래 선다.
 * @param {Firestore} db 명명 DB 핸들
 * @param {string} env 환경 id
 * @param {string} uid 사용자 id
 * @return {DocumentReference} 미션 문서 참조
 */
export function missionsRef(db: Firestore, env: string, uid: string): DocumentReference {
  return db.doc(`envs/${env}/users/${uid}/missions/current`);
}

/**
 * 정수 카운터 맵을 안전하게 읽는다. 음수·비정수·상한 초과는 버린다.
 * @param {unknown} value 문서의 맵 값
 * @return {Record<string, number>} 정리된 카운터
 */
function readCounters(value: unknown): Record<string, number> {
  if (value === null || typeof value !== "object") return {};

  const counters: Record<string, number> = {};
  for (const [key, raw] of Object.entries(value as Record<string, unknown>)) {
    const count = Number(raw);
    if (!Number.isInteger(count) || count <= 0) continue;
    counters[key] = Math.min(count, COUNTER_MAX);
  }
  return counters;
}

/**
 * boolean 낙인 맵을 안전하게 읽는다. 이전 실험 스키마의 문자열 배열도 같은 맵으로 접는다.
 * @param {unknown} value 문서의 맵 또는 레거시 리스트 값
 * @return {Record<string, boolean>} 정리된 낙인
 */
function readClaimed(value: unknown): Record<string, boolean> {
  const claimed: Record<string, boolean> = {};
  // 1차 구현이 배포된 적이 있어도 새 스키마로 안전하게 접을 수 있게 배열을 한동안 읽어 준다.
  if (Array.isArray(value)) {
    for (const entry of value) {
      if (typeof entry !== "string" || entry.trim().length === 0) continue;
      claimed[entry.trim()] = true;
    }
    return claimed;
  }
  if (value === null || typeof value !== "object") return claimed;
  for (const [key, raw] of Object.entries(value as Record<string, unknown>)) {
    if (key.trim().length > 0 && raw === true) claimed[key] = true;
  }
  return claimed;
}

/**
 * 진행도 맵의 키. 주기 접두사를 붙이는 자리는 여기 하나다 — 이 규칙이 흩어지면
 * bump 가 쓰는 키와 조회·리셋이 보는 키가 조용히 갈린다(진행도가 영영 0으로 보인다).
 *
 * 집계기 호출부는 이 함수를 모른다. 스펙대로 `bump(uid, "OpenPack", 1)` 만 넘긴다.
 * @param {MissionPeriodKind} kind 주기
 * @param {string} event 이벤트 문자열
 * @return {string} `daily.OpenPack` 모양의 키
 */
export function progressKey(kind: MissionPeriodKind, event: string): string {
  return MISSION_ID_PREFIX[kind] + event;
}

/**
 * 이 키가 그 주기 축에 속하는가. 진행도 키와 미션 id 가 같은 접두사를 쓰므로
 * 리셋 필터는 둘 다 이 판정 하나로 거른다.
 * @param {string} key 진행도 키 또는 미션 id
 * @param {MissionPeriodKind} kind 주기
 * @return {boolean} 그 주기 축이면 true
 */
function belongsTo(key: string, kind: MissionPeriodKind): boolean {
  return key.startsWith(MISSION_ID_PREFIX[kind]);
}

/**
 * 접두사 없는 옛 주기별 맵을 새 단일 진행도 맵으로 접는다.
 * @param {Record<string, number>} current 지금까지 접은 진행도
 * @param {MissionPeriodKind} prefix 옛 맵의 주기
 * @param {unknown} legacy 옛 문서의 daily/weekly 맵
 * @return {Record<string, number>} 접두사가 붙은 진행도
 */
function prefixedProgress(
  current: Record<string, number>, prefix: MissionPeriodKind, legacy: unknown,
): Record<string, number> {
  const result = {...current};
  for (const [event, count] of Object.entries(readCounters(legacy))) {
    const key = progressKey(prefix, event);
    if (result[key] === undefined) result[key] = count;
  }
  return result;
}

/**
 * 스냅샷에서 상태를 읽는다. **문서 부재는 정상이다** — `ensureAccount` 가 이 문서를 만들지 않으므로
 * 모든 기존 계정과 신규 계정이 여기서 시작한다. 못 읽었다고 명령을 막지 않는다.
 * @param {DocumentSnapshot} snapshot 미션 문서 스냅샷
 * @return {MissionState} 문서 값(없으면 빈 상태)
 */
export function readMissions(snapshot: DocumentSnapshot): MissionState {
  const data = snapshot.exists ? snapshot.data() : undefined;
  let progress = readCounters(data?.progress);
  progress = prefixedProgress(progress, "daily", data?.daily);
  progress = prefixedProgress(progress, "weekly", data?.weekly);
  return {
    dailyKey: typeof data?.dailyKey === "string" ? data.dailyKey :
      typeof data?.dailyPeriod === "string" ? data.dailyPeriod : "",
    weeklyKey: typeof data?.weeklyKey === "string" ? data.weeklyKey :
      typeof data?.weeklyPeriod === "string" ? data.weeklyPeriod : "",
    progress,
    claimed: readClaimed(data?.claimed),
    passExp: Number.isInteger(data?.passExp) ? Number(data?.passExp) :
      Number.isInteger(data?.battlePassXp) ? Number(data?.battlePassXp) : 0,
  };
}

/**
 * 기간이 바뀌었으면 그 축만 비운다.
 *
 * 낙인을 카운터와 **같이** 비우는 것이 핵심이다 — 카운터만 비우면 어제 수령한 일일 미션이
 * 오늘도 수령됨으로 남아 영영 못 받고, 낙인만 비우면 어제 카운터로 오늘 보상을 또 받는다.
 * 접두사로 축을 갈라 일일 리셋이 주간 낙인을 건드리지 않게 한다.
 * @param {MissionState} state 읽어 온 상태
 * @param {MissionPeriod} period 이번 호출의 기간
 * @return {MissionState} 리셋을 반영한 상태(입력을 변형하지 않는다)
 */
export function applyPeriodReset(state: MissionState, period: MissionPeriod): MissionState {
  const dailyStale = state.dailyKey !== period.daily;
  const weeklyStale = state.weeklyKey !== period.weekly;
  if (!dailyStale && !weeklyStale) return state;

  const dropped = new Set<MissionPeriodKind>();
  if (dailyStale) dropped.add("daily");
  if (weeklyStale) dropped.add("weekly");

  return {
    dailyKey: period.daily,
    weeklyKey: period.weekly,
    progress: Object.fromEntries(Object.entries(state.progress).filter(
      ([key]) => ![...dropped].some((kind) => belongsTo(key, kind)))),
    claimed: Object.fromEntries(Object.entries(state.claimed).filter(
      ([key]) => ![...dropped].some((kind) => belongsTo(key, kind)))),
    passExp: state.passExp,
  };
}

/**
 * 콜백 **맨 앞**에서 부른다. 미션 문서를 읽고 기간 리셋까지 반영해 손잡이를 만든다.
 *
 * 여기서 아무것도 쓰지 않는다 — 콜백 뒤쪽에 또 읽는 명령(enhanceCard 의 grants)이 있어서다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {Firestore} db 명명 DB 핸들
 * @param {string} env 환경 id
 * @param {string} uid 사용자 id
 * @param {MissionPeriod} period 이번 호출의 기간(callable 본문이 한 번만 잰 값)
 * @return {Promise<MissionBump>} 쓰기에 쓸 손잡이
 */
export async function beginMissionBump(
  transaction: Transaction,
  db: Firestore,
  env: string,
  uid: string,
  period: MissionPeriod,
): Promise<MissionBump> {
  const ref = missionsRef(db, env, uid);
  return missionBumpFromSnapshot(ref, await transaction.get(ref), period);
}

/**
 * 일괄 조회한 미션 문서에도 단일 조회와 같은 기간 리셋을 적용한다. 읽기·쓰기는 하지 않는다.
 * @param {DocumentReference} ref 조회한 미션 문서 참조
 * @param {DocumentSnapshot} snapshot 같은 트랜잭션에서 읽은 스냅샷
 * @param {MissionPeriod} period 이번 호출의 기간
 * @return {MissionBump} 쓰기에 쓸 손잡이
 */
export function missionBumpFromSnapshot(
  ref: DocumentReference,
  snapshot: DocumentSnapshot,
  period: MissionPeriod,
): MissionBump {
  return {ref, state: applyPeriodReset(readMissions(snapshot), period), period};
}

/**
 * 손잡이의 상태를 문서에 통째로 쓴다. `update` 가 아니라 `set` 이다 — 문서 부재가 정상이라
 * `update` 로 쓰면 첫 진행이 통째로 실패한다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {MissionBump} bump 손잡이
 * @param {unknown} now 서버 시각(FieldValue.serverTimestamp()) — 호출부가 넘긴다
 * @return {void}
 */
export function commitMissionProgress(transaction: Transaction, bump: MissionBump, now: unknown): void {
  transaction.set(bump.ref, {
    schemaVersion: MISSION_SCHEMA_VERSION,
    dailyKey: bump.period.daily,
    weeklyKey: bump.period.weekly,
    progress: bump.state.progress,
    claimed: bump.state.claimed,
    passExp: bump.state.passExp,
    updatedAt: now,
  });
}

/**
 * 디버그 초기화: 일일 진행도와 수령 낙인만 비운다. 지급된 보상과 패스 경험치는 유지한다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {MissionBump} bump 기간 리셋까지 반영한 손잡이
 * @param {unknown} now 서버 시각
 */
export function commitDailyMissionReset(transaction: Transaction, bump: MissionBump, now: unknown): void {
  bump.state.progress = Object.fromEntries(
    Object.entries(bump.state.progress).filter(([key]) => !belongsTo(key, "daily")));
  bump.state.claimed = Object.fromEntries(
    Object.entries(bump.state.claimed).filter(([key]) => !belongsTo(key, "daily")));
  commitMissionProgress(transaction, bump, now);
}

/**
 * 콜백 **맨 끝**에서 부른다. 이벤트 카운터를 올리고 문서를 쓴다.
 *
 * 일일·주간을 함께 올린다 — 같은 행동이 두 주기에 다 잡히는 것이 미션의 정의다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {MissionBump} bump beginMissionBump 가 만든 손잡이
 * @param {string} event 올릴 이벤트 문자열
 * @param {number} amount 증가량(1 이상)
 * @param {unknown} now 서버 시각(FieldValue.serverTimestamp())
 * @return {void}
 */
export function commitMissionBump(
  transaction: Transaction,
  bump: MissionBump,
  event: string,
  amount: number,
  now: unknown,
): void {
  commitMissionBumps(transaction, bump, [{event, amount}], now);
}

/**
 * 여러 이벤트의 일일·주간 카운터를 누적하고 같은 트랜잭션에서 문서를 한 번만 쓴다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {MissionBump} bump 기간 리셋까지 반영한 손잡이
 * @param {Array} increments 이벤트별 증가량. 빈 목록이면 쓰지 않는다
 * @param {unknown} now 서버 시각
 * @return {void}
 */
export function commitMissionBumps(
  transaction: Transaction,
  bump: MissionBump,
  increments: readonly {event: string; amount: number}[],
  now: unknown,
): void {
  if (increments.length === 0) return;
  for (const {event, amount} of increments) {
    applyMissionIncrement(bump, event, amount);
  }
  commitMissionProgress(transaction, bump, now);
}

/**
 * 기존 미션 쓰기에 함께 실을 이벤트를 누적한다. DB I/O는 하지 않는다.
 * @param {MissionBump} bump 기간 리셋한 상태
 * @param {string} event 이벤트 이름
 * @param {number} amount 증가량
 */
export function applyMissionIncrement(bump: MissionBump, event: string, amount: number): void {
  const step = Number.isInteger(amount) && amount > 0 ? amount : 1;
  for (const kind of ["daily", "weekly"] as const) {
    const key = progressKey(kind, event);
    bump.state.progress[key] = Math.min((bump.state.progress[key] ?? 0) + step, COUNTER_MAX);
  }
}

/**
 * 수령 낙인을 찍고 문서를 쓴다. 카운터는 건드리지 않는다 —
 * 수령이 진행도를 소비하면 같은 이벤트의 다른 미션(주간)이 같이 깎인다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {MissionBump} bump 손잡이
 * @param {string} missionId 수령한 미션 id
 * @param {number} passExp 이번 수령으로 붙는 패스 경험치(0 이면 안 붙는다)
 * @param {unknown} now 서버 시각(FieldValue.serverTimestamp())
 * @return {void}
 */
export function commitMissionClaim(
  transaction: Transaction,
  bump: MissionBump,
  missionId: string,
  passExp: number,
  now: unknown,
): void {
  bump.state.claimed[missionId] = true;
  // 패스 경험치는 지갑이 아니라 이 문서에 쌓인다 — 낙인과 같은 트랜잭션이어야
  // "수령은 됐는데 경험치는 안 붙은" 상태가 저장되지 않는다.
  const gain = Number.isInteger(passExp) && passExp > 0 ? passExp : 0;
  bump.state.passExp = Math.min(bump.state.passExp + gain, PASS_EXP_MAX);
  commitMissionProgress(transaction, bump, now);
}

/**
 * 해당 주기의 활성 일반 미션 달성 수. 보상 수령 여부와 무관하며 누적 보상은 제외한다.
 * @param {MissionState} state 리셋을 반영한 상태
 * @param {MissionPeriodKind} period 집계할 주기
 * @param {Array} catalog 이번 요청의 정의 목록
 * @return {number} 달성한 일반 미션 수
 */
function completedMissions(state: MissionState, period: MissionPeriodKind, catalog: readonly MissionDef[]): number {
  return enabledMissions(catalog).filter((mission) => mission.period === period &&
    mission.event !== DAILY_MISSION_COMPLETION_EVENT &&
    mission.event !== WEEKLY_MISSION_COMPLETION_EVENT &&
    (state.progress[progressKey(period, mission.event)] ?? 0) >= mission.target).length;
}

/**
 * 이 미션의 현재 진행도. 누적 완료 보상은 일반 미션 카운터에서 파생한다.
 * @param {MissionState} state 리셋을 반영한 상태
 * @param {MissionDef} mission 미션 정의
 * @param {Array} catalog 이번 요청의 정의 목록
 * @return {number} 누적 횟수
 */
export function progressOf(state: MissionState, mission: MissionDef, catalog: readonly MissionDef[]): number {
  if ((mission.period === "daily" && mission.event === DAILY_MISSION_COMPLETION_EVENT) ||
      (mission.period === "weekly" && mission.event === WEEKLY_MISSION_COMPLETION_EVENT)) {
    return completedMissions(state, mission.period, catalog);
  }
  return state.progress[progressKey(mission.period, mission.event)] ?? 0;
}

/**
 * 이미 수령했는가.
 * @param {MissionState} state 리셋을 반영한 상태
 * @param {string} missionId 미션 id
 * @return {boolean} 수령했으면 true
 */
export function isClaimed(state: MissionState, missionId: string): boolean {
  return state.claimed[missionId] === true;
}

/**
 * 커맨드 응답과 조회 응답이 공유하는 현재 미션 상태 봉투.
 *
 * 맵을 복사해서 낸다 — 손잡이의 상태는 이 뒤에도 변형될 수 있는데, 참조를 그대로 실으면
 * 응답이 나중 변형을 따라간다.
 * @param {MissionState} state 리셋·bump 까지 반영한 상태
 * @param {MissionPeriod} period 이번 호출의 기간
 * @param {Array} catalog 이번 요청의 정의 목록
 * @return {MissionResponse} 응답에 실을 상태 봉투
 */
export function missionResponse(
  state: MissionState, period: MissionPeriod, catalog: readonly MissionDef[],
): MissionResponse {
  return {
    dailyKey: period.daily,
    weeklyKey: period.weekly,
    progress: {
      ...state.progress,
      [progressKey("daily", DAILY_MISSION_COMPLETION_EVENT)]: completedMissions(state, "daily", catalog),
      [progressKey("weekly", WEEKLY_MISSION_COMPLETION_EVENT)]: completedMissions(state, "weekly", catalog),
    },
    claimed: {...state.claimed},
    passExp: state.passExp,
    dailyResetAtMs: period.dailyResetAtMs,
    weeklyResetAtMs: period.weeklyResetAtMs,
  };
}
