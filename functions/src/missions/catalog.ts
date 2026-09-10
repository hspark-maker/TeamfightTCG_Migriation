/** 발행된 Mission 표의 파싱·검증과 순수 조회. 요청마다 같은 정의 목록을 전달한다. */

/** 미션 주기. 낙인·카운터가 서로 다른 리셋 축을 타므로 값 하나로 뭉치지 않는다. */
export type MissionPeriodKind = "daily" | "weekly" | "guide";

/** 미션 한 건의 정의. */
export interface MissionDef {
  /**
   * 미션 id. **낙인 키**로 그대로 쓰이므로 주기별 네임스페이스를 접두사로 강제한다
   * (`daily.` / `weekly.`) — 안 가르면 일일 리셋이 주간 낙인을 지워 주간 보상을 매일 탈 수 있다.
   */
  id: string;
  period: MissionPeriodKind;
  /**
   * 집계기가 올리는 문자열 키. 하드코딩된 union 이 아니라 자유 문자열이다 —
   * 전투·룰렛 이벤트를 붙일 때 미션 시스템 코드를 고치지 않기 위한 축이다.
   */
  event: string;
  /** 달성에 필요한 누적 횟수. */
  target: number;
  title: string;
  description: string;
  passExp: number;
  sortOrder: number;
  /**
   * 꺼진 미션은 목록과 수령에서 제외한다. 이벤트 카운터는 미션과 분리돼 계속 쌓이므로,
   * 활성화는 기간 경계 직후에 수행해 소급 달성을 막는다.
   */
  enabled: boolean;
}

/** 낙인 네임스페이스. 진행도 키 접두사와 같은 값을 공유한다(`missionStore.progressKey`). */
export const MISSION_ID_PREFIX: Record<MissionPeriodKind, string> = {
  daily: "daily.",
  weekly: "weekly.",
  guide: "guide.",
};

/** 미션 id 최대 길이. 문서 키로 쓰이므로 상한을 둔다. */
export const MAX_MISSION_ID_LENGTH = 64;

/**
 * 발행된 Mission 행을 해석한다. 결손·잘못된 저작은 이전 번들로 대체하지 않고 거절한다.
 * @param {Array} rows 스펙 리더가 읽은 행
 * @return {Array} 검증된 전체 미션 정의
 */
export function parseMissionCatalog(rows: readonly Record<string, unknown>[]): MissionDef[] {
  if (rows.length === 0) throw new Error("Mission spec has no rows.");
  const integer = (value: unknown): number =>
    (typeof value === "number" || (typeof value === "string" && value.trim() !== "")) ?
      Number(value) : Number.NaN;
  const catalog = rows.map((row): MissionDef => {
    const enabled = integer(row.enabled);
    if (enabled !== 0 && enabled !== 1) throw new Error(`Mission '${row.missionId}' has invalid enabled.`);
    return {
      id: String(row.missionId ?? ""),
      period: String(row.period ?? "") as MissionPeriodKind,
      event: String(row.eventKey ?? ""),
      target: integer(row.targetCount),
      title: String(row.title ?? ""),
      description: String(row.description ?? ""),
      passExp: integer(row.passExp),
      sortOrder: integer(row.sortOrder),
      enabled: enabled === 1,
    };
  });
  const issues = missionCatalogIssues(catalog);
  if (issues.length > 0) throw new Error(`Invalid Mission spec: ${issues.join("; ")}`);
  return catalog;
}

/**
 * 화면에 낼 정의만. 꺼진 미션은 목록에서 빠진다.
 * @param {Array} catalog 이번 요청의 정의 목록
 * @return {Array} 켜진 미션 정의
 */
export function enabledMissions(catalog: readonly MissionDef[]): readonly MissionDef[] {
  return catalog.filter((mission) => mission.enabled);
}

/**
 * id 로 정의 하나를 찾는다. **꺼진 미션도 찾힌다** — 수령 거절 사유를 가르기 위해서다.
 * @param {string} missionId 미션 id
 * @param {Array} catalog 이번 요청의 정의 목록
 * @return {MissionDef | null} 없으면 null
 */
export function findMission(missionId: string, catalog: readonly MissionDef[]): MissionDef | null {
  return catalog.find((mission) => mission.id === missionId) ?? null;
}

/**
 * 저작 규약 위반 목록. 비어 있으면 정상이다.
 *
 * **모듈 적재 시점에 던지지 않는다** — Cloud Functions 에서 적재 중 예외는 그 인스턴스의
 * 모든 callable 을 죽인다. 미션 저작 실수 하나가 팩 개봉·강화까지 함께 멈추면 안 된다.
 * 표를 읽은 요청의 파싱 시점에만 검증한다.
 * @param {Array} catalog 검사할 정의 목록
 * @return {Array} 문제 설명 목록
 */
export function missionCatalogIssues(
  catalog: readonly MissionDef[],
): string[] {
  const issues: string[] = [];
  const seen = new Set<string>();

  for (const mission of catalog) {
    if (seen.has(mission.id)) issues.push(`Duplicated mission id: ${mission.id}`);
    seen.add(mission.id);

    if (!["daily", "weekly", "guide"].includes(mission.period)) {
      issues.push(`Mission '${mission.id}' has an invalid period.`);
    } else if (!mission.id.startsWith(MISSION_ID_PREFIX[mission.period])) {
      issues.push(`Mission '${mission.id}' must start with '${MISSION_ID_PREFIX[mission.period]}'.`);
    }
    if (mission.id.length > MAX_MISSION_ID_LENGTH) {
      issues.push(`Mission id '${mission.id}' is too long.`);
    }
    if (mission.event.trim().length === 0) {
      issues.push(`Mission '${mission.id}' has an empty event key.`);
    }
    if (!Number.isSafeInteger(mission.target) || mission.target <= 0) {
      issues.push(`Mission '${mission.id}' has a non-positive target.`);
    }
    if (mission.title.trim().length === 0 || mission.description.trim().length === 0) {
      issues.push(`Mission '${mission.id}' has empty display text.`);
    }
    if (!Number.isSafeInteger(mission.passExp) || mission.passExp < 0) {
      issues.push(`Mission '${mission.id}' has an invalid passExp.`);
    }
    if (!Number.isSafeInteger(mission.sortOrder) || mission.sortOrder <= 0) {
      issues.push(`Mission '${mission.id}' has an invalid sortOrder.`);
    }
  }
  return issues;
}
