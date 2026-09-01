import {randomInt} from "node:crypto";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {drawAiDeck, parseAiDeckRows} from "../matchmaking/aiDeckDraw";
import {parseRankGradeRows, resolveTierIndex} from "../payout";
import {isKnownEnv, requireUid, saveDocument} from "../save/saveDocument";
import {readSpecRows} from "../specs/specBlobReader";

/** 실시간 상대를 찾지 못한 싱글 전투의 AI 덱과 공통 카드 레벨을 서버에서 확정한다. */
export const findAiMatch = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  if (!isKnownEnv(env)) throw new HttpsError("invalid-argument", `Unknown env: ${env}`);

  const [snapshot, rankRows, deckRows] = await Promise.all([
    saveDocument(env, uid).get(),
    readSpecRows(env, "RankGrade"),
    readSpecRows(env, "AIDeck"),
  ]);
  if (!snapshot.exists) throw new HttpsError("failed-precondition", "Save document is missing.");

  const rank = snapshot.data()?.rank as Record<string, unknown> | undefined;
  const rawPoints = rank?.points;
  const points = Number.isSafeInteger(rawPoints) ? rawPoints as number : 0;
  const grades = parseRankGradeRows(rankRows);
  if (grades.length === 0) throw new HttpsError("failed-precondition", "RankGrade spec is empty.");

  try {
    const tierIndex = resolveTierIndex(points, grades);
    const draw = drawAiDeck(parseAiDeckRows(deckRows), tierIndex, randomInt);
    if (draw === null) throw new Error("AIDeck spec is empty");
    logger.info("AI match deck selected", {uid, env, tierIndex, deckId: draw.deckId});
    return draw;
  } catch (error) {
    logger.error("AI match deck selection failed", {uid, env, error});
    throw new HttpsError("failed-precondition", "AIDeck spec is invalid.");
  }
});
