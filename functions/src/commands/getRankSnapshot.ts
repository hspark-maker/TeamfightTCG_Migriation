import {HttpsError, onCall} from "firebase-functions/v2/https";
import {defineSecret} from "firebase-functions/params";
import {db} from "../firebaseApp";
import {signMatchTicket, TICKET_TTL_SECONDS} from "../match/matchTicket";
import {parseRankGradeRows, RankGradeRow, resolveTierIndex} from "../payout";
import {currentRankSeason, RankSeasonDef} from "../rank/rankSeason";
import {
  ensureRankState,
  rankProgressResponse,
} from "../rank/rankStore";
import {isKnownEnv, requireUid} from "../save/saveDocument";
import {readSpecRows} from "../specs/specBlobReader";

const matchTicketSecret = defineSecret("MATCH_TICKET_SECRET");

/** Returns server-owned seasonal rank progress and a signed matchmaking ticket. */
export const getRankSnapshot = onCall({secrets: [matchTicketSecret]}, async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  if (!isKnownEnv(env)) throw new HttpsError("invalid-argument", `Unknown env: ${env}`);

  let grades: RankGradeRow[] = [];
  let season: RankSeasonDef | null = null;
  try {
    const [gradeRows, seasonRows] = await Promise.all([
      readSpecRows(env, "RankGrade"),
      readSpecRows(env, "PassSeason"),
    ]);
    grades = parseRankGradeRows(gradeRows);
    season = currentRankSeason(seasonRows, Date.now());
    if (grades.length === 0 || season === null) throw new Error("rank season is unavailable");
  } catch (error) {
    throw new HttpsError("failed-precondition", `RANK_SPEC_INVALID ${String(error)}`);
  }

  const state = await ensureRankState(db, env, uid, season!.seasonId, grades);
  if (state === null) throw new HttpsError("failed-precondition", "Save document is missing.");
  const progress = rankProgressResponse(state);

  const tierIndex = resolveTierIndex(progress.points, grades);
  const issueTicket = request.data?.issueTicket !== false;
  const nowSeconds = Math.floor(Date.now() / 1000);
  return {
    ...progress,
    tierIndex,
    ticket: issueTicket ? signMatchTicket(
      {uid, tier: tierIndex, env, exp: nowSeconds + TICKET_TTL_SECONDS},
      matchTicketSecret.value()) : "",
  };
});
