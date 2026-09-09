import {HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {parseRankGradeRows} from "../payout";
import {currentRankSeason} from "../rank/rankSeason";
import {ensureRankState} from "../rank/rankStore";
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
  const state = await ensureRankState(db, env, uid, season.seasonId, grades);
  if (!state) throw new HttpsError("failed-precondition", "Save document is missing.");

  const minimum = grades[0].entryPoints;
  const board = db.collection(`envs/${env}/rankings`).where("seasonId", "==", season.seasonId);
  const [top, ahead] = await Promise.all([
    board.where("points", ">=", minimum).orderBy("points", "desc").limit(100).get(),
    state.points >= minimum ? board.where("points", ">", state.points).orderBy("points", "desc").count().get() : null,
  ]);
  const ids = [...new Set([...top.docs.map((doc) => doc.id), uid])];
  const profiles = await db.getAll(...ids.map((id) => db.doc(`envs/${env}/users/${id}/save/current`)));
  const profileById = new Map(ids.map((id, index) => [id, profiles[index].data()?.profile]));
  const entry = (id: string, points: number, rank: number) => {
    const profile = profileById.get(id);
    return {
      rank, points, isSelf: id === uid,
      nickname: typeof profile?.nickname === "string" ? profile.nickname.slice(0, 12) : "플레이어",
      avatarId: typeof profile?.avatarId === "string" ? profile.avatarId : "",
      frameId: typeof profile?.frameId === "string" ? profile.frameId : "",
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
