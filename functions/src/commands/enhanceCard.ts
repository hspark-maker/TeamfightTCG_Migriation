import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {randomInt, randomUUID} from "node:crypto";
import {FieldValue} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {
  isKnownEnv,
  mutateSave,
  requireUid,
  SaveMutation,
} from "../save/saveDocument";
import {rejectDomain} from "../save/domainReject";
import {clientReceiptId, isClientReceiptId} from "../save/receiptId";
import {readSpecRows} from "../packs/packSpecReader";
import {canAfford, spend} from "../currency/wallet";
import {nextWallet} from "../currency/walletStore";
import {
  applyEnhanceLevel,
  growthSlot,
  levelOfCard,
  readGrowthEntries,
} from "../growth/cardGrowth";
import {
  cardEnhanceStep,
  parseCardEnhanceOverrides,
  parseCardEnhanceRule,
  rollSucceeded,
} from "../growth/enhanceRules";
import {
  grantsRef,
  hasFreeShot,
  readGrants,
  TutorialGrants,
  writeGrantUsed,
} from "../growth/tutorialGrants";

/**
 * 도메인 거절 사유. **와이어 계약**이다 — 클라 EEnhanceOutcome 의 이름과 그대로 대조된다.
 */
type EnhanceReject = "MaxLevel" | "NotAffordable" | "RuleUnavailable";

/** 무료 한 방이 걸린 축. 키워드 강화와 다른 축이라 따로 소진된다. */
const FREE_SHOT_AXIS = "enhanceCard";

/**
 * 도메인 거절. 던지기와 로그는 save/domainReject 한 곳이고, 여기 남은 것은 사유 오타를 막는 타입 관문이다.
 * @param {EnhanceReject} reason 사유 코드
 * @param {string} message 로그용 설명
 * @param {Record<string, unknown>} context 어느 값에 막혔는지
 */
function reject(reason: EnhanceReject, message: string, context: Record<string, unknown>): never {
  rejectDomain(reason, message, context);
}

/**
 * 카드 강화 1회. 비용 곡선·차감·성공 판정을 서버가 소유한다.
 *
 * 실패해도 비용은 나가고 레벨은 내려가지 않는다(클라 CardGrowthManager.TryEnhance 와 같은 규칙).
 * 무료 한 방은 **비용만 0으로** 만들고 성공률은 건드리지 않으며, 성공했을 때만 소진으로 찍는다
 * — 실패로 닫으면 온보딩이 시킨 성장을 유저가 제 돈으로 다시 해야 한다.
 */
export const enhanceCard = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const cardId = Number(request.data?.cardId ?? 0);
  const freeShotRequested = request.data?.freeShot === true;

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }
  if (!Number.isInteger(cardId) || cardId <= 0) {
    throw new HttpsError("invalid-argument", "cardId must be a positive integer.");
  }

  // 스펙 읽기는 트랜잭션 밖이다 — 유저 문서와 무관하고, 재실행마다 다시 읽으면 비용만 는다.
  const [ruleRows, overrideRows] = await Promise.all([
    readSpecRows(env, "CardEnhanceRule"),
    readSpecRows(env, "CardEnhance"),
  ]);

  const rule = parseCardEnhanceRule(ruleRows);
  if (rule === null) {
    // 곡선 없이 차감할 수는 없다. 이 로그가 뜨면 스펙 업로드가 빠진 것이고 강화가 통째로 막힌다.
    logger.error("CardEnhanceRule spec is unusable", {uid, env, rowCount: ruleRows.length});
    reject("RuleUnavailable", "Card enhance rule is not authored.", {uid, env, rowCount: ruleRows.length});
  }
  const overrides = parseCardEnhanceOverrides(overrideRows);

  let outcome: "Success" | "Failed" = "Failed";
  let level = 0;
  let currency = "";
  let cost = 0;
  let freeShotUsed = false;
  // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
  // finalize 안에서 뒤집는다 — 트랜잭션 재실행마다 다시 돌아도 결과가 같다.
  let replayed = true;

  // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
  const txId = clientReceiptId(request.data?.txId, randomUUID());

  const result = await mutateSave(env, uid, "enhanceCard", {kind: "client", txId},
    async (current, transaction, wallet): Promise<SaveMutation> => {
      // 트랜잭션이 재실행되면 이전 판정을 버리고 다시 굴린다 — 잔액·레벨과 정합해야 한다.
      const entries = readGrowthEntries(current.cardGrowth);
      const currentLevel = levelOfCard(entries, cardId);

      const step = cardEnhanceStep(rule, overrides, currentLevel + 1);
      if (step === null) {
        reject("MaxLevel", `Card ${cardId} is already at the max level.`,
          {uid, env, cardId, level: currentLevel, maxLevel: rule.maxLevel});
      }

      // freeShot 이 false 면 문서를 읽지도 쓰지도 않는다 — 매 강화마다 왕복을 더할 이유가 없다.
      // 읽기는 반드시 트랜잭션 안이다 — 동시 호출 둘이 같은 "미사용"을 보면 한 방이 두 번 나간다.
      const grantsReference = freeShotRequested ? grantsRef(db, env, uid) : null;
      let freeShot: TutorialGrants | null = null;
      if (grantsReference !== null) {
        const grants = readGrants(await transaction.get(grantsReference));
        if (hasFreeShot(grants, FREE_SHOT_AXIS)) freeShot = grants;
      }

      const charged = freeShot === null ? step.cost : 0;
      const balances = wallet.balances;
      if (!canAfford(balances, step.currency, charged)) {
        reject("NotAffordable", `Not enough ${step.currency} to enhance card ${cardId}.`,
          {uid, env, cardId, level: currentLevel, currency: step.currency, cost: charged,
            balance: balances[step.currency]});
      }

      const succeeded = rollSucceeded(step.successPermille, randomInt);
      if (succeeded && grantsReference !== null && freeShot !== null) {
        writeGrantUsed(transaction, grantsReference, FREE_SHOT_AXIS, freeShot, FieldValue.serverTimestamp());
      }

      outcome = succeeded ? "Success" : "Failed";
      level = succeeded ? step.level : currentLevel;
      currency = step.currency;
      cost = charged;
      freeShotUsed = succeeded && freeShot !== null;

      return {
        slots: {
          cardGrowth: growthSlot(succeeded ? applyEnhanceLevel(entries, cardId, step.level) : entries),
        },
        wallet: nextWallet(wallet, spend(balances, step.currency, charged), "enhanceCard"),
      };
    },
    (adopted) => {
      replayed = false;
      return {...adopted, outcome, level, currency, cost, freeShotUsed};
    });

  if (replayed) {
    logger.info("receipt replay", {uid, env, source: "enhanceCard", txId, revision: result.revision});
  } else {
    logger.info("enhanceCard", {
      uid, env, cardId, outcome, level, currency, cost,
      freeShotRequested, freeShotUsed,
      revision: result.revision,
      txIdSource: isClientReceiptId(request.data?.txId) ? "client" : "server",
    });
  }

  return result;
});
