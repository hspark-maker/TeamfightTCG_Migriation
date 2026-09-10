import {HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {parseRankGradeRows} from "../payout";
import {currentRankSeason} from "../rank/rankSeason";
import {ensureRankSnapshot} from "../rank/rankStore";
import {hasRankPublicProfile, publicRankProfile} from "../rank/publicProfile";
import {isKnownEnv, requireUid} from "../save/saveDocument";
import {readSpecRows} from "../specs/specBlobReader";

/** 환경·시즌별 상위 100명과 내 공동순위. 비공개 세이브 필드는 응답에 포함하지 않는다. */
export const getRankLeaderboard = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  if (!isKnownEnv(env)) throw new HttpsError("invalid-argument", "Unknown environment.");
  const [gradeRows, seasonRows] = await Promise.all([
    readSpecRows(env, "RankGrade"), readSpecRows(env, "PassSeason"),
  ]);
  const grades = parseRankGradeRows(gradeRows);
  const season = currentRankSeason(seasonRows, Date.now());
  if (!grades.length || !season) throw new HttpsError("failed-precondition", "Rank season is unavailable.");
  const snapshot = await ensureRankSnapshot(db, env, uid, season.seasonId, grades);
  if (!snapshot) throw new HttpsError("failed-precondition", "Save document is missing.");
  const {state, profile: selfProfile} = snapshot;

  const minimum = grades[0].entryPoints;
  const board = db.collection(`envs/${env}/rankings`).where("seasonId", "==", season.seasonId);
  const [top, ahead] = await Promise.all([
    board.where("points", ">=", minimum).orderBy("points", "desc").limit(100).get(),
    state.points >= minimum ? board.where("points", ">", state.points).orderBy("points", "desc").count().get() : null,
  ]);
  const profileById = new Map(top.docs.map((doc) => [doc.id, doc.data().profile as unknown]));
  profileById.set(uid, selfProfile);
  // 아직 프로필이 없는 구 색인만 보완한다. 보정 완료 후 일반 경로의 추가 읽기는 0회다.
  const legacyIds = top.docs.filter((doc) => !hasRankPublicProfile(profileById.get(doc.id))).map((doc) => doc.id);
  if (legacyIds.length > 0) {
    const profiles = await db.getAll(
      ...legacyIds.map((id) => db.doc(`envs/${env}/users/${id}/save/current`)), {fieldMask: ["profile"]});
    legacyIds.forEach((id, index) => profileById.set(id, profiles[index].data()?.profile));
  }
  const entry = (id: string, points: number, rank: number) => {
    return {
      rank, points, isSelf: id === uid,
      ...publicRankProfile(profileById.get(id)),
    };
  };
  let previousPoints = -1;
  let rank = 0;
  const entries = top.docs.map((doc, index) => {
    const points = Number(doc.data().points);
    if (points !== previousPoints) rank = index + 1;
    previousPoints = points;
    return entry(doc.id, points, rank);
  });
  return {
    season: {seasonId: season.seasonId, endAtMs: season.endAtMs},
    entries,
    self: entry(uid, state.points, ahead ? ahead.data().count + 1 : 0),
  };
});
