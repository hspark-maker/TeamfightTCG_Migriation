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
 * 3개(save → wallet → receipt)는 콜백 전에 끝나지만, `enhanceCard` 처럼 콜백 안에서 또 읽는 명령이 있다.
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
  MISSION_ID_PREFIX,
  MissionDef,
  MissionEvent,
  MissionPeriodKind,
} from "./catalog";
import type {MissionPeriod} from "./period";

/** 미션 문서의 스키마 축. 세이브 SCHEMA_VERSION 과 별개로 승급한다. */
export const MISSION_SCHEMA_VERSION = 1;

/** 카운터 한 축의 상한. 저작 실수나 폭주가 있어도 문서가 무한히 커지지 않게 자른다. */
const COUNTER_MAX = 1000000;

/** 미션 문서의 값. 문서가 없으면 이 모양의 빈 상태로 선다. */
export interface MissionState {
  dailyPeriod: string;
  weeklyPeriod: string;
  /** 일일 카운터. 키는 MissionEvent 문자열. */
  daily: Record<string, number>;
  weekly: Record<string, number>;
  /** 수령 낙인. `daily.` / `weekly.` 접두사로 리셋 축이 갈린다. */
  claimed: string[];
  /** 배틀패스는 이번 범위 밖이다 — 자리만 두고 아무도 올리지 않는다. */
  battlePassXp: number;
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
 * 낙인 목록을 안전하게 읽는다. 빈 값·비문자열·중복은 버리고 순서는 보존한다.
 * @param {unknown} value 문서의 리스트 값
 * @return {string[]} 정리된 낙인
 */
function readClaimed(value: unknown): string[] {
  if (!Array.isArray(value)) return [];

  const seen = new Set<string>();
  for (const entry of value) {
    if (typeof entry !== "string") continue;
    const key = entry.trim();
    if (key.length === 0) continue;
    seen.add(key);
  }
  return [...seen];
}

/**
 * 스냅샷에서 상태를 읽는다. **문서 부재는 정상이다** — `ensureAccount` 가 이 문서를 만들지 않으므로
 * 모든 기존 계정과 신규 계정이 여기서 시작한다. 못 읽었다고 명령을 막지 않는다.
 * @param {DocumentSnapshot} snapshot 미션 문서 스냅샷
 * @return {MissionState} 문서 값(없으면 빈 상태)
 */
export function readMissions(snapshot: DocumentSnapshot): MissionState {
  const data = snapshot.exists ? snapshot.data() : undefined;
  return {
    dailyPeriod: typeof data?.dailyPeriod === "string" ? data.dailyPeriod : "",
    weeklyPeriod: typeof data?.weeklyPeriod === "string" ? data.weeklyPeriod : "",
    daily: readCounters(data?.daily),
    weekly: readCounters(data?.weekly),
    claimed: readClaimed(data?.claimed),
    battlePassXp: Number.isInteger(data?.battlePassXp) ? Number(data?.battlePassXp) : 0,
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
  const dailyStale = state.dailyPeriod !== period.daily;
  const weeklyStale = state.weeklyPeriod !== period.weekly;
  if (!dailyStale && !weeklyStale) return state;

  const dropped = new Set<MissionPeriodKind>();
  if (dailyStale) dropped.add("daily");
  if (weeklyStale) dropped.add("weekly");

  return {
    dailyPeriod: period.daily,
    weeklyPeriod: period.weekly,
    daily: dailyStale ? {} : state.daily,
    weekly: weeklyStale ? {} : state.weekly,
    claimed: state.claimed.filter((key) => {
      for (const kind of dropped) {
        if (key.startsWith(MISSION_ID_PREFIX[kind])) return false;
      }
      return true;
    }),
    battlePassXp: state.battlePassXp,
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
  const state = applyPeriodReset(readMissions(await transaction.get(ref)), period);
  return {ref, state, period};
}

/**
 * 손잡이의 상태를 문서에 통째로 쓴다. `update` 가 아니라 `set` 이다 — 문서 부재가 정상이라
 * `update` 로 쓰면 첫 진행이 통째로 실패한다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {MissionBump} bump 손잡이
 * @param {unknown} now 서버 시각(FieldValue.serverTimestamp()) — 호출부가 넘긴다
 * @return {void}
 */
function write(transaction: Transaction, bump: MissionBump, now: unknown): void {
  transaction.set(bump.ref, {
    schemaVersion: MISSION_SCHEMA_VERSION,
    dailyPeriod: bump.period.daily,
    weeklyPeriod: bump.period.weekly,
    daily: bump.state.daily,
    weekly: bump.state.weekly,
    claimed: bump.state.claimed,
    battlePassXp: bump.state.battlePassXp,
    updatedAt: now,
  });
}

/**
 * 콜백 **맨 끝**에서 부른다. 이벤트 카운터를 올리고 문서를 쓴다.
 *
 * 일일·주간을 함께 올린다 — 같은 행동이 두 주기에 다 잡히는 것이 미션의 정의다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {MissionBump} bump beginMissionBump 가 만든 손잡이
 * @param {MissionEvent} event 올릴 이벤트
 * @param {number} amount 증가량(1 이상)
 * @param {unknown} now 서버 시각(FieldValue.serverTimestamp())
 * @return {void}
 */
export function commitMissionBump(
  transaction: Transaction,
  bump: MissionBump,
  event: MissionEvent,
  amount: number,
  now: unknown,
): void {
  const step = Number.isInteger(amount) && amount > 0 ? amount : 1;
  bump.state.daily[event] = Math.min((bump.state.daily[event] ?? 0) + step, COUNTER_MAX);
  bump.state.weekly[event] = Math.min((bump.state.weekly[event] ?? 0) + step, COUNTER_MAX);
  write(transaction, bump, now);
}

/**
 * 수령 낙인을 찍고 문서를 쓴다. 카운터는 건드리지 않는다 —
 * 수령이 진행도를 소비하면 같은 이벤트의 다른 미션(주간)이 같이 깎인다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {MissionBump} bump 손잡이
 * @param {string} missionId 수령한 미션 id
 * @param {unknown} now 서버 시각(FieldValue.serverTimestamp())
 * @return {void}
 */
export function commitMissionClaim(
  transaction: Transaction,
  bump: MissionBump,
  missionId: string,
  now: unknown,
): void {
  if (!bump.state.claimed.includes(missionId)) bump.state.claimed.push(missionId);
  write(transaction, bump, now);
}

/**
 * 이 미션의 현재 진행도. 주기 축의 카운터만 본다.
 * @param {MissionState} state 리셋을 반영한 상태
 * @param {MissionDef} mission 미션 정의
 * @return {number} 누적 횟수
 */
export function progressOf(state: MissionState, mission: MissionDef): number {
  const counters = mission.period === "daily" ? state.daily : state.weekly;
  return counters[mission.event] ?? 0;
}

/**
 * 이미 수령했는가.
 * @param {MissionState} state 리셋을 반영한 상태
 * @param {string} missionId 미션 id
 * @return {boolean} 수령했으면 true
 */
export function isClaimed(state: MissionState, missionId: string): boolean {
  return state.claimed.includes(missionId);
}
