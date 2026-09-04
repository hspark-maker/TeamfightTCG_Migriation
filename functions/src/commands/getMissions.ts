import {HttpsError, onCall} from "firebase-functions/v2/https";
import {isKnownEnv, requireUid} from "../save/saveDocument";
import {db} from "../firebaseApp";
import {missionCatalog} from "../missions/catalog";
import {
  applyPeriodReset,
  isClaimed,
  missionsRef,
  progressOf,
  readMissions,
} from "../missions/missionStore";
import {missionPeriod} from "../missions/period";

/**
 * 미션 화면이 그릴 것 전부를 한 번에 돌려준다 — 정의 · 진행도 · 수령 여부 · 다음 리셋 시각.
 *
 * **정의를 클라에 두지 않기 위한 창구다.** 클라에 사본을 두면 밸런스 수정이 앱 배포에 묶이고,
 * 서버 카탈로그와 갈리는 순간 유저는 화면에 보이는 목표와 다른 조건으로 거절당한다.
 *
 * 진행도를 openPack 같은 커맨드 응답에 실어 보내지 않는 이유도 같은 자리에 있다 —
 * `mutateSave` 의 영수증 재생이 **첫 시도의 응답 그대로**를 되돌려주므로, 거기 실린 진행도는
 * 재시도에서 낡은 값이 된다. 화면은 언제나 이 명령이나 문서 자체를 봐야 한다.
 *
 * 쓰기가 없다 — `mutateSave` 를 타지 않고 영수증도 끊지 않는다. 기간 리셋은 **메모리에서만** 반영한다.
 * 조회가 문서를 쓰기 시작하면 화면을 열어 두기만 해도 쓰기 비용이 나간다. 실제 리셋은 다음
 * bump·수령이 확정하고, 그때까지 이 응답과 문서가 달라도 유저가 보는 값은 옳다.
 */
export const getMissions = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }

  const period = missionPeriod(Date.now());
  // 문서 부재는 정상이다 — ensureAccount 가 만들지 않으므로 첫 조회는 항상 빈 상태다.
  const state = applyPeriodReset(readMissions(await missionsRef(db, env, uid).get()), period);

  return {
    dailyPeriod: period.daily,
    weeklyPeriod: period.weekly,
    dailyResetAtMs: period.dailyResetAtMs,
    weeklyResetAtMs: period.weeklyResetAtMs,
    missions: missionCatalog().map((mission) => ({
      id: mission.id,
      period: mission.period,
      event: mission.event,
      target: mission.target,
      progress: Math.min(progressOf(state, mission), mission.target),
      claimed: isClaimed(state, mission.id),
      reward: mission.reward,
    })),
  };
});
