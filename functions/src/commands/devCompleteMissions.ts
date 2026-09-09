import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {FieldValue} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {requireUid} from "../save/saveDocument";
import {enabledMissions} from "../missions/catalog";
import {missionPeriod} from "../missions/period";
import {beginMissionBump, commitMissionBump, progressKey} from "../missions/missionStore";

/**
 * 디버그 전용: 활성 일일·주간 미션의 진행도를 목표까지 채운다.
 *
 * 가이드 미션은 건드리지 않는다 — 그 진행도는 세이브에서 매 조회마다 재계산되므로
 * 여기서 써 봐야 다음 getMissions 가 되덮는다.
 *
 * 수령 낙인은 찍지 않는다 — "완료됐는데 아직 안 받은" 상태를 만드는 것이 목적이라
 * 수령 흐름(claimMission)을 그대로 검증할 수 있다.
 */
export const devCompleteMissions = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");

  // 라이브 문서는 어떤 경우에도 이 함수가 건드리지 않는다 (devBumpRevision 과 같은 규율).
  if (env !== "test") {
    throw new HttpsError(
      "permission-denied",
      "devCompleteMissions is available on the test env only.",
    );
  }

  const period = missionPeriod(Date.now());
  const bumped = await db.runTransaction(async (transaction) => {
    const bump = await beginMissionBump(transaction, db, env, uid, period);

    // 같은 이벤트를 일일·주간이 공유하므로 이벤트별로 부족량의 최대치를 모은다.
    // commitMissionBump 가 두 축을 같이 올려 한쪽이 목표를 넘칠 수 있지만, 표시는 목표에서 멈춘다.
    const steps = new Map<string, number>();
    for (const mission of enabledMissions()) {
      if (mission.period === "guide") continue;
      const key = progressKey(mission.period, mission.event);
      const needed = mission.target - (bump.state.progress[key] ?? 0);
      if (needed <= 0) continue;
      steps.set(mission.event, Math.max(steps.get(mission.event) ?? 0, needed));
    }

    for (const [event, step] of steps) {
      commitMissionBump(transaction, bump, event, step, FieldValue.serverTimestamp());
    }
    return steps.size;
  });

  logger.info("devCompleteMissions", {uid, env, bumpedEvents: bumped});
  return {bumpedEvents: bumped};
});
