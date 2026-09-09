/**
 * 미션 수령 자격 판정. **순수 함수다** — Firestore 도 HttpsError 도 모른다.
 *
 * `claimReward` 가 `judgeRewardClaim` 을 빼 둔 것과 같은 이유다: 판정이 callable 안에 있으면
 * 에뮬레이터 없이는 한 줄도 테스트할 수 없다. 이 프로젝트의 테스트 러너는 전부
 * `lib/` 를 직접 require 하는 순수 회귀라, 판정이 여기 있어야 회귀가 붙는다.
 */

import {findMission, missionCatalog, MissionDef} from "./catalog";
import {isClaimed, MissionState, progressOf} from "./missionStore";

/**
 * 거절 사유. **와이어 계약**이다 — 클라가 이 문자열을 그대로 대조한다.
 *
 * `MissionNotFound` 와 `MissionDisabled` 를 가르는 이유: 전자는 구 클라가 삭제된 미션을
 * 부른 것이고, 후자는 저작이 꺼 둔 것이다. 운영이 로그로 둘을 구분해야 한다.
 */
export type MissionClaimReject =
  | "MissionNotFound"
  | "MissionDisabled"
  | "RewardNotFound"
  | "AlreadyClaimed"
  | "NotEligible";

/** 판정 결과. 통과면 지급할 정의가 함께 온다. */
export type MissionClaimVerdict =
  | {allow: true; mission: MissionDef; progress: number}
  | {allow: false; reason: MissionClaimReject; mission: MissionDef | null; progress: number};

/**
 * 이 미션을 지금 수령할 수 있는가.
 *
 * 순서가 계약이다 — **Claimed 검사가 달성 검사보다 먼저**다(`claimReward` 와 같은 순서).
 * 나중에 target 을 올리면 이미 받은 미션이 "미달성"으로 되돌아가는데, 그때 재수령 창구가
 * 열리면 안 된다.
 *
 * 꺼진 미션도 거절한다. 목록에서만 빼고 수령을 열어 두면 구 클라가 id 를 직접 보내 받는다.
 * @param {string} missionId 수령 요청한 미션 id
 * @param {MissionState} state 기간 리셋까지 반영한 상태
 * @return {MissionClaimVerdict} 판정
 */
export function judgeMissionClaim(missionId: string, state: MissionState): MissionClaimVerdict {
  const mission = findMission(missionId);
  if (mission === null) {
    return {allow: false, reason: "MissionNotFound", mission: null, progress: 0};
  }

  const progress = progressOf(state, mission);

  if (!mission.enabled) {
    return {allow: false, reason: "MissionDisabled", mission, progress};
  }
  if (isClaimed(state, mission.id)) {
    return {allow: false, reason: "AlreadyClaimed", mission, progress};
  }
  if (mission.period === "guide" && missionCatalog().some((entry) => entry.enabled &&
    entry.period === "guide" && entry.sortOrder < mission.sortOrder && !isClaimed(state, entry.id))) {
    return {allow: false, reason: "NotEligible", mission, progress};
  }
  if (progress < mission.target) {
    return {allow: false, reason: "NotEligible", mission, progress};
  }

  return {allow: true, mission, progress};
}
