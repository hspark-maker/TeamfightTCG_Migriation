import {randomUUID} from "node:crypto";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {
  isKnownEnv,
  mutateSave,
  requireUid,
  SaveMutation,
} from "../save/saveDocument";
import {rejectDomain} from "../save/domainReject";
import {clientReceiptId, isClientReceiptId} from "../save/receiptId";
import {readSpecRows} from "../packs/packSpecReader";
import {readOwnedIds} from "../packs/packSlots";
import {
  applyLimitBreak,
  canAffordSnack,
  growthSlot,
  readGrowthEntries,
} from "../growth/cardGrowth";
import {authoredMaxLimitBreak, parseCardEnhanceRule} from "../growth/enhanceRules";
import {
  limitBreakStep,
  parseLimitBreakCurve,
} from "../growth/limitBreakTable";

/**
 * 도메인 거절 사유. **와이어 계약**이다 — 클라가 이 문자열을 그대로 대조한다.
 */
type LimitBreakReject = "RuleUnavailable" | "CardNotOwned" | "MaxStage" | "NotEnoughSnack";

/**
 * 도메인 거절. 던지기와 로그는 save/domainReject 한 곳이고, 여기 남은 것은 사유 오타를 막는 타입 관문이다.
 * @param {LimitBreakReject} reason 사유 코드
 * @param {string} message 로그용 설명
 * @param {Record<string, unknown>} context 어느 값에 막혔는지
 */
function reject(reason: LimitBreakReject, message: string, context: Record<string, unknown>): never {
  rejectDomain(reason, message, context);
}

/**
 * 한계돌파 1단계. 단계 곡선·간식 차감·소유 검사를 서버가 소유한다.
 *
 * 지갑을 건드리지 않는다 — 간식은 전역 재화가 아니라 cardGrowth 슬롯 안 카드별 값이다.
 * 확률 실패가 없어(클라 TryLimitBreak 에 판정이 없다) 통과하면 항상 단계가 오른다.
 */
export const limitBreakCard = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const cardId = Number(request.data?.cardId ?? 0);

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }
  if (!Number.isInteger(cardId) || cardId <= 0) {
    throw new HttpsError("invalid-argument", "cardId must be a positive integer.");
  }

  // 스펙 읽기는 트랜잭션 밖이다 — 유저 문서와 무관하고, 재실행마다 다시 읽으면 비용만 는다.
  const [ruleRows, curveRows] = await Promise.all([
    readSpecRows(env, "CardEnhanceRule"),
    readSpecRows(env, "CardLimitBreak"),
  ]);

  const rule = parseCardEnhanceRule(ruleRows);
  if (rule === null) {
    // 표를 통째로 못 읽은 것이다 — 배포/업로드 사고이고 유저 잘못이 아니다.
    logger.error("CardEnhanceRule spec is unusable", {uid, env, rowCount: ruleRows.length});
    reject("RuleUnavailable", "Card enhance rule is not authored.", {uid, env, cardId, rowCount: ruleRows.length});
  }
  if (rule.maxLimitBreak <= 0) {
    // 표는 읽혔는데 한계돌파 열이 비어 있다 — 저작 미완이라 위 갈래와 로그 context 를 다르게 둔다.
    logger.error("CardEnhanceRule has no limit break axis", {uid, env, maxLimitBreak: 0});
    reject("RuleUnavailable", "Limit break axis is closed by the rule table.",
      {uid, env, cardId, maxLimitBreak: rule.maxLimitBreak});
  }

  // 천장 클램프는 조용하다 — 표가 더 큰 상한을 말했다는 사실은 여기서만 드러난다.
  // 잘린 채로 두면 저작은 4 단계인데 유저는 3 에서 MaxStage 로 막히고, 원인이 어디에도 남지 않는다.
  const authored = authoredMaxLimitBreak(ruleRows);
  if (authored !== null && authored > rule.maxLimitBreak) {
    logger.warn("CardEnhanceRule maxLimitBreak was clamped to the code ceiling",
      {uid, env, authored, clamped: rule.maxLimitBreak});
  }

  const curve = parseLimitBreakCurve(curveRows, rule.maxLimitBreak);
  if (curve === null) {
    logger.error("CardLimitBreak spec is unusable", {uid, env, rowCount: curveRows.length});
    reject("RuleUnavailable", "Limit break curve is not authored.",
      {uid, env, cardId, rowCount: curveRows.length, maxLimitBreak: rule.maxLimitBreak});
  }

  let stage = 0;
  let hpGain = 0;
  let snackCost = 0;
  let snackLeft = 0;
  // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
  // finalize 안에서 뒤집는다 — 트랜잭션 재실행마다 다시 돌아도 결과가 같다.
  let replayed = true;
  // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
  const txId = clientReceiptId(request.data?.txId, randomUUID());

  const result = await mutateSave(env, uid, "limitBreakCard", {kind: "client", txId},
    (current): SaveMutation => {
      // 트랜잭션이 재실행되면 이전 판정을 버리고 다시 잰다 — 간식·단계와 정합해야 한다.
      // 소유 게이트는 클라 TryGetNextLimitBreakStep 과 같은 자리다. 빼면 세이브에 진행도만
      // 남은 미소유 카드에 체력이 붙는다.
      if (!readOwnedIds(current.ownership).includes(cardId)) {
        reject("CardNotOwned", `Card ${cardId} is not owned.`, {uid, env, cardId});
      }

      const entries = readGrowthEntries(current.cardGrowth);
      const entry = entries[String(cardId)];
      const currentStage = entry === undefined || entry.limitBreak < 0 ? 0 : entry.limitBreak;
      const currentSnack = entry === undefined || entry.snack < 0 ? 0 : entry.snack;

      const next = currentStage + 1;
      const step = limitBreakStep(curve, next);
      if (step === null) {
        // 상한 도달과 저작 결손이 같은 사유로 나간다 — 운영이 갈라 보게 둘 다 싣는다.
        reject("MaxStage", `Card ${cardId} cannot break past stage ${currentStage}.`,
          {uid, env, cardId, stage: currentStage, maxStage: curve.maxStage, rowCount: curveRows.length});
      }
      if (!canAffordSnack(entries, cardId, step.snackCost)) {
        reject("NotEnoughSnack", `Not enough snack to break card ${cardId} to stage ${next}.`,
          {uid, env, cardId, stage: currentStage, snackCost: step.snackCost, snack: currentSnack});
      }

      stage = next;
      hpGain = step.hpGain;
      snackCost = step.snackCost;
      snackLeft = currentSnack - step.snackCost;

      // 지갑 키를 싣지 않는다 — 간식은 지갑 재화가 아니라 cardGrowth 슬롯 안 값이다.
      return {
        slots: {
          cardGrowth: growthSlot(applyLimitBreak(entries, cardId, next, step.snackCost)),
        },
      };
    },
    (adopted) => {
      replayed = false;
      return {...adopted, stage, hpGain, snackCost, snackLeft};
    });

  if (replayed) {
    logger.info("receipt replay", {uid, env, source: "limitBreakCard", txId, revision: result.revision});
  } else {
    logger.info("limitBreakCard", {
      uid, env, cardId, stage, hpGain, snackCost, snackLeft,
      revision: result.revision,
      txIdSource: isClientReceiptId(request.data?.txId) ? "client" : "server",
    });
  }

  return result;
});
