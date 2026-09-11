import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {FieldValue} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {requireUid} from "../save/saveDocument";
import {readMissionCatalog} from "../missions/missionSpec";
import {missionPeriod} from "../missions/period";
import {beginMissionBump, commitDailyMissionReset, missionResponse} from "../missions/missionStore";

/** 테스트 환경에서 호출자 본인의 일일 미션 진행도·수령 낙인을 초기화한다. */
export const devResetDailyMissions = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  if (env !== "test") {
    throw new HttpsError("permission-denied", "devResetDailyMissions is available on the test env only.");
  }

  const catalog = await readMissionCatalog(env);
  const period = missionPeriod(Date.now());
  const missions = await db.runTransaction(async (transaction) => {
    const bump = await beginMissionBump(transaction, db, env, uid, period);
    commitDailyMissionReset(transaction, bump, FieldValue.serverTimestamp());
    return missionResponse(bump.state, period, catalog);
  });
  logger.info("devResetDailyMissions", {uid, env, dailyKey: period.daily});
  return {missions};
});
