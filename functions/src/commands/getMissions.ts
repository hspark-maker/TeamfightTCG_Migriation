import {HttpsError, onCall} from "firebase-functions/v2/https";
import {isKnownEnv, requireUid} from "../save/saveDocument";
import {db} from "../firebaseApp";
import {enabledMissions} from "../missions/catalog";
import {
  applyPeriodReset,
  missionsRef,
  missionResponse,
  readMissions,
} from "../missions/missionStore";
import {missionPeriod} from "../missions/period";
import {readSpecRows} from "../packs/packSpecReader";
import {parseRewardRows, resolveRewards} from "../rewardTable";

/**
 * 미션 화면이 그릴 것 전부를 한 번에 돌려준다 — 정의 · 진행도 · 수령 여부 · 다음 리셋 시각.
 *
 * **정의를 클라에 두지 않기 위한 창구다.** 클라에 사본을 두면 밸런스 수정이 앱 배포에 묶이고,
 * 서버 카탈로그와 갈리는 순간 유저는 화면에 보이는 목표와 다른 조건으로 거절당한다.
 *
 * 변경 커맨드는 응답의 선택 필드 `missions` 로 **같은 모양의** 상태 봉투를 돌려주고, 정의 목록이
 * 필요할 때만 이 명령을 부른다. 영수증 재생은 같은 save revision 에서만 성공하므로,
 * 캐시된 봉투도 그 시점 문서와 어긋나지 않는다.
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
  const [missionSnapshot, rewardSpecRows] = await Promise.all([
    missionsRef(db, env, uid).get(),
    readSpecRows(env, "Reward"),
  ]);
  const state = applyPeriodReset(readMissions(missionSnapshot), period);
  const rewardRows = parseRewardRows(rewardSpecRows);

  return {
    // `missions` 는 변경 커맨드의 선택 필드와 **같은 타입**이다. 이름이 같은데 모양이 다르면
    // 클라가 응답마다 다른 파싱을 해야 하고, 그 분기는 언젠가 한쪽만 갱신된다.
    missions: missionResponse(state, period),
    // 정의만 싣는다 — 진행도·수령 여부는 위 봉투가 유일한 출처다. 같은 응답에 두 벌을 실으면
    // 클램프 유무 같은 사소한 차이로 둘이 어긋나고, 화면은 둘 중 아무거나 읽는다.
    // 완료 판정은 `missions.progress[period + "." + event] >= target` 이다.
    // 꺼진 미션은 목록에서 뺀다. 진행도는 그래도 쌓이고 있으므로(bump 는 enabled 를 보지 않는다)
    // 나중에 켜는 날 유저가 0부터 시작하지 않는다.
    definitions: enabledMissions().map((mission) => ({
      id: mission.id,
      period: mission.period,
      event: mission.event,
      target: mission.target,
      title: mission.title,
      description: mission.description,
      sortOrder: mission.sortOrder,
      reward: {
        currencies: resolveRewards(rewardRows, "Mission", mission.id).gains,
        passExp: mission.passExp,
      },
    })),
  };
});
