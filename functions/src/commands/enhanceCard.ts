import {measuredCallable} from "../observability/requestMetrics";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {randomUUID} from "node:crypto";
import {FieldValue} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {EVENTS} from "../analytics/eventNames";
import {recordEvent} from "../observability/analyticsEvent";
import {
  beginMissionBump,
  commitMissionBump,
  missionResponse,
  MissionResponse,
} from "../missions/missionStore";
import {missionPeriod} from "../missions/period";
import {readMissionCatalog} from "../missions/missionSpec";
import {applyGuideProgress, readGuideCards} from "../missions/guideMutation";
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
  feedShard,
  growthSlot,
  levelOfCard,
  readGrowthEntries,
  shardRequirement,
} from "../growth/cardGrowth";
import {
  cardEnhanceStep,
  parseCardEnhanceOverrides,
  parseCardEnhanceRule,
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

/** Feed up to the next evolution. The tutorial grant fills the remaining amount. */
export const enhanceCard = onCall(measuredCallable("enhanceCard", async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const cardId = Number(request.data?.cardId ?? 0);
  const freeShotRequested = request.data?.freeShot === true;
  const amount = request.data?.amount === undefined ? 1 : request.data.amount;

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }
  if (!Number.isInteger(cardId) || cardId <= 0) {
    throw new HttpsError("invalid-argument", "cardId must be a positive integer.");
  }
  if (!Number.isInteger(amount) || amount < 1 || amount > 150) {
    throw new HttpsError("invalid-argument", "amount must be an integer from 1 to 150.");
  }

  // 스펙 읽기는 트랜잭션 밖이다 — 유저 문서와 무관하고, 재실행마다 다시 읽으면 비용만 는다.
  const [ruleRows, overrideRows, catalog, guideCards] = await Promise.all([
    readSpecRows(env, "CardEnhanceRule"),
    readSpecRows(env, "CardEnhance"),
    readMissionCatalog(env),
    readGuideCards(env),
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
  let shardProgress = 0;
  let shardRequired = 0;
  let evolved = false;
  let appliedShards = 0;
  let currency = "";
  let cost = 0;
  let freeShotUsed = false;
  let missionState: MissionResponse | undefined;
  // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
  // finalize 안에서 뒤집는다 — 트랜잭션 재실행마다 다시 돌아도 결과가 같다.
  let replayed = true;

  // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
  const txId = clientReceiptId(request.data?.txId, randomUUID());

  // 기간은 여기서 한 번만 잰다 — 콜백은 재실행되므로 그 안에서 재면 경계에 걸린 호출이 흔들린다.
  const period = missionPeriod(Date.now());

  const result = await mutateSave(env, uid, "enhanceCard", {kind: "client", txId},
    async (current, transaction, wallet): Promise<SaveMutation> => {
      // 미션 읽기가 콜백의 첫 줄이다. 아래 grants 읽기와는 둘 다 읽기라 순서를 다투지 않지만,
      // 미션 **쓰기**는 그 grants 읽기보다 뒤여야 해서 콜백 맨 끝으로 갈라 두었다.
      const missions = await beginMissionBump(transaction, db, env, uid, period);

      // Retry from the committed growth and wallet state.
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

      const paid = freeShot === null && step.cost > 0;
      const balances = wallet.balances;
      if (paid && !canAfford(balances, step.currency, 1)) {
        reject("NotAffordable", `Not enough ${step.currency} to enhance card ${cardId}.`,
          {uid, env, cardId, level: currentLevel, currency: step.currency, cost: 1,
            balance: balances[step.currency]});
      }

      const availableAmount = paid ? Math.min(amount, balances[step.currency] ?? 0) : amount;
      const fed = feedShard(entries, cardId, step.cost, freeShot !== null, availableAmount);
      const charged = paid ? fed.appliedShards : 0;
      if (grantsReference !== null && freeShot !== null) {
        writeGrantUsed(transaction, grantsReference, FREE_SHOT_AXIS, FieldValue.serverTimestamp());
      }

      outcome = "Success";
      level = fed.level;
      shardProgress = fed.shardProgress;
      const nextStep = cardEnhanceStep(rule, overrides, level + 1);
      shardRequired = nextStep === null ? 0 : shardRequirement(nextStep.cost);
      evolved = fed.evolved;
      appliedShards = fed.appliedShards;
      currency = step.currency;
      cost = charged;
      freeShotUsed = freeShot !== null;

      const slots = {
        cardGrowth: growthSlot(fed.entries),
      };
      applyGuideProgress(missions, current, slots, guideCards, catalog);

      // Count each accepted shard feed, including feeds below the evolution threshold.
      // All mission writes follow the optional tutorial-grant read.
      commitMissionBump(transaction, missions, EVENTS.cardEnhanceResolved.missionKey,
        freeShotUsed ? 1 : appliedShards, FieldValue.serverTimestamp());
      missionState = missionResponse(missions.state, period, catalog);

      return {
        slots,
        wallet: nextWallet(wallet, spend(balances, step.currency, charged), "enhanceCard"),
      };
    },
    (adopted) => {
      replayed = false;
      return {...adopted, outcome, level, shardProgress, shardRequired, evolved, appliedShards,
        currency, cost, freeShotUsed, missions: missionState};
    });

  if (replayed) {
    logger.info("receipt replay", {uid, env, source: "enhanceCard", txId, revision: result.revision});
  } else {
    recordEvent(EVENTS.cardEnhanceResolved.name, {
      uid, env, eventId: txId, sourceCommand: "enhanceCard", result: outcome,
      cardId, outcome, level, shardProgress, shardRequired, evolved, appliedShards, currency, cost,
      freeShotRequested, freeShotUsed,
      revision: result.revision,
      txIdSource: isClientReceiptId(request.data?.txId) ? "client" : "server",
    });
  }

  return result;
}));
