import {HttpsError, onCall} from "firebase-functions/v2/https";
import {defineSecret} from "firebase-functions/params";
import {isKnownEnv, requireUid, saveDocument} from "../save/saveDocument";
import {parseRankGradeRows, resolveTierIndex} from "../payout";
import {readSpecRows} from "../specs/specBlobReader";
import {signMatchTicket, TICKET_TTL_SECONDS} from "../match/matchTicket";

const matchTicketSecret = defineSecret("MATCH_TICKET_SECRET");

/**
 * 매칭 시작 시 쓰는 랭크 스냅샷. 세이브 문서의 rank.points 를 서버가 직접 읽어
 * 서버 스펙표(RankGrade)로 티어를 계산해 돌려준다.
 *
 * 이 호출이 막는 것: 로컬 세이브 조작과 낡은 캐시. 초기화 때 채택한 값이 세션 내내 갱신되지 않아
 * 승급 직후 매칭이 옛 티어로 걸리던 문제도 같이 사라진다.
 *
 * 함께 발급하는 티켓은 **상대가 verifyMatchTicket 으로 검증한다** — 표시·매칭에 쓰는 티어를
 * 자기신고에서 서버 서명으로 옮기는 축이다. 세션 프로퍼티에는 절대 싣지 않는다(로비 목록은 공개라
 * 아무나 주워 자기 것처럼 쓸 수 있다). 티켓은 매칭이 성립한 뒤 상대에게만 직접 보낸다.
 */
export const getRankSnapshot = onCall({secrets: [matchTicketSecret]}, async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }

  const snapshot = await saveDocument(env, uid).get();
  if (!snapshot.exists) {
    throw new HttpsError("failed-precondition", "Save document is missing.");
  }

  const rank = snapshot.data()?.rank as Record<string, unknown> | undefined;
  const rawPoints = rank?.points;
  const points = Number.isSafeInteger(rawPoints) ? (rawPoints as number) : 0;

  const grades = parseRankGradeRows(await readSpecRows(env, "RankGrade"));
  if (grades.length === 0) {
    throw new HttpsError("failed-precondition", "RankGrade spec is empty.");
  }

  const tierIndex = resolveTierIndex(points, grades);
  const nowSeconds = Math.floor(Date.now() / 1000);

  // 티켓은 상대가 서버에 들고 와 검증한다(verifyMatchTicket) — 그래야 티어가 자기신고를 벗어난다.
  return {
    points,
    tierIndex,
    ticket: signMatchTicket(
      {uid, tier: tierIndex, env, exp: nowSeconds + TICKET_TTL_SECONDS},
      matchTicketSecret.value()),
  };
});
