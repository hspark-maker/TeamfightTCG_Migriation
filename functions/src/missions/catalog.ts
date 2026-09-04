/**
 * 미션 정의의 진실원. 1차는 스펙 표가 아니라 서버 상수다.
 *
 * 표로 올리려면 시트 반영 → CS 코드젠 → CSV 임포터 → SpecPayloadCodec.TableNames → 업로더 →
 * 서버 packSpecReader 까지 6단을 건드려야 한다. 그건 이 기능의 두 배 크기라 2차로 미룬다.
 * 대신 **클라에 사본을 두지 않는다** — 클라는 이 정의를 서버 응답으로 받아 그린다.
 * 사본을 두는 순간 밸런스 수정이 앱 배포에 묶인다.
 */

import type {CurrencyGain} from "../currency/wallet";

/**
 * 진행도를 올리는 이벤트. **와이어 계약**이다 — 문서의 counters 키와 같은 문자열이고
 * 클라가 이 이름으로 진행도를 읽는다.
 *
 * 전투 축(CompleteBattle · DestroyEnemyCard · WinRankedBattle)은 이번 범위 밖이다 —
 * 재시뮬 검증 배관이 아직 없어 서버가 완전히 검증하지 못한다. SpinRoulette 은 콘텐츠가 없다.
 */
export const MISSION_EVENTS = [
  "OpenPack",
  "EnhanceCard",
  "LimitBreakCard",
  "ClaimReward",
] as const;

export type MissionEvent = typeof MISSION_EVENTS[number];

/** 미션 주기. 낙인·카운터가 서로 다른 리셋 축을 타므로 값 하나로 뭉치지 않는다. */
export type MissionPeriodKind = "daily" | "weekly";

/** 미션 한 건의 정의. */
export interface MissionDef {
  /**
   * 미션 id. **낙인 키**로 그대로 쓰이므로 주기별 네임스페이스를 접두사로 강제한다
   * (`daily.` / `weekly.`) — 안 가르면 일일 리셋이 주간 낙인을 지워 주간 보상을 매일 탈 수 있다.
   */
  id: string;
  period: MissionPeriodKind;
  event: MissionEvent;
  /** 달성에 필요한 누적 횟수. */
  target: number;
  reward: CurrencyGain[];
}

/** 낙인 네임스페이스. `missionId` 검증과 리셋이 같은 규칙을 봐야 한다. */
export const MISSION_ID_PREFIX: Record<MissionPeriodKind, string> = {
  daily: "daily.",
  weekly: "weekly.",
};

/** 미션 id 최대 길이. 문서 키로 쓰이므로 상한을 둔다. */
export const MAX_MISSION_ID_LENGTH = 64;

const CATALOG: MissionDef[] = [
  {
    id: "daily.openPack1",
    period: "daily",
    event: "OpenPack",
    target: 1,
    reward: [{currency: "Gold", amount: 100}],
  },
  {
    id: "daily.enhanceCard3",
    period: "daily",
    event: "EnhanceCard",
    target: 3,
    reward: [{currency: "Gold", amount: 150}],
  },
  {
    id: "daily.claimReward1",
    period: "daily",
    event: "ClaimReward",
    target: 1,
    reward: [{currency: "Gold", amount: 100}],
  },
  {
    id: "weekly.openPack10",
    period: "weekly",
    event: "OpenPack",
    target: 10,
    reward: [{currency: "Diamond", amount: 30}],
  },
  {
    id: "weekly.enhanceCard20",
    period: "weekly",
    event: "EnhanceCard",
    target: 20,
    reward: [{currency: "Shard", amount: 50}],
  },
  {
    id: "weekly.limitBreakCard3",
    period: "weekly",
    event: "LimitBreakCard",
    target: 3,
    reward: [{currency: "Diamond", amount: 50}],
  },
];

/**
 * 정의 목록. 사본을 돌려주지 않는다 — 호출부가 읽기만 한다는 전제다.
 * @return {MissionDef[]} 전체 미션 정의(읽기 전용으로 다룬다)
 */
export function missionCatalog(): readonly MissionDef[] {
  return CATALOG;
}

/**
 * id 로 정의 하나를 찾는다.
 * @param {string} missionId 미션 id
 * @return {MissionDef | null} 없으면 null
 */
export function findMission(missionId: string): MissionDef | null {
  return CATALOG.find((mission) => mission.id === missionId) ?? null;
}

/**
 * 저작 자체가 규약을 어겼는지 본다. 배포 전에 터지도록 모듈 적재 시점에 한 번 돈다 —
 * id 접두사가 주기와 어긋나면 리셋 축이 조용히 뒤섞이는데, 그건 유저 문서가 망가진 뒤에야 드러난다.
 */
(function assertCatalog(): void {
  const seen = new Set<string>();
  for (const mission of CATALOG) {
    if (seen.has(mission.id)) {
      throw new Error(`Duplicated mission id: ${mission.id}`);
    }
    seen.add(mission.id);

    if (!mission.id.startsWith(MISSION_ID_PREFIX[mission.period])) {
      throw new Error(
        `Mission '${mission.id}' must start with '${MISSION_ID_PREFIX[mission.period]}'.`);
    }
    if (mission.id.length > MAX_MISSION_ID_LENGTH) {
      throw new Error(`Mission id '${mission.id}' is too long.`);
    }
    if (!Number.isInteger(mission.target) || mission.target <= 0) {
      throw new Error(`Mission '${mission.id}' has a non-positive target.`);
    }
    if (mission.reward.some((gain) => !Number.isInteger(gain.amount) || gain.amount <= 0)) {
      throw new Error(`Mission '${mission.id}' authors a non-positive reward.`);
    }
  }
})();
