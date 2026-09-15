import {HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {isKnownEnv, requireUid} from "../save/saveDocument";
import {readSpecRows} from "../packs/packSpecReader";
import {parseRewardRows} from "../rewardTable";
import {attendanceDays} from "../attendance/attendanceSpec";
import {attendanceResponse, readAttendance} from "../attendance/attendanceState";
import {attendanceRef} from "../attendance/attendanceStore";

export const getAttendance = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  if (!isKnownEnv(env)) throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  const [snapshot, rows] = await Promise.all([
    attendanceRef(db, env, uid).get(), readSpecRows(env, "Reward"),
  ]);
  const days = attendanceDays(parseRewardRows(rows));
  return {attendance: attendanceResponse(readAttendance(snapshot.data()), Date.now()), days};
});
