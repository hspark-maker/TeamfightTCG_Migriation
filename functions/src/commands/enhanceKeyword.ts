import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {FieldValue} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {
  isKnownEnv,
  mutateSave,
  requireUid,
  SaveMutation,
} from "../save/saveDocument";
import {rejectDomain} from "../save/domainReject";
import {readSpecRows} from "../packs/packSpecReader";
import {canAfford, spend} from "../currency/wallet";
import {nextWallet} from "../currency/walletStore";
import {
  isSupportedKeyword,
  keywordGrowthSlot,
  levelOfKeyword,
  readKeywordLevels,
  setKeywordLevel,
} from "../growth/keywordGrowth";
import {keywordEnhanceStep, parseKeywordEnhanceRules} from "../growth/enhanceRules";
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
type EnhanceReject = "MaxLevel" | "NotAffordable" | "RuleUnavailable" | "KeywordNotSupported";

/** 무료 한 방이 걸린 축. 카드 강화와 다른 축이라 따로 소진된다. */
const FREE_SHOT_AXIS = "enhanceKeyword";

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
 * 키워드 강화 1회. 비용 곡선과 차감을 서버가 소유한다.
 *
 * 확률 실패가 없어 outcome 은 언제나 Success 다(클라 KeywordGrowthManager.TryEnhance 에 판정이 없다)
 * — 카드 강화와 응답 모양을 맞추려고 같은 필드를 싣는다.
 * 무료 한 방은 비용만 0으로 만들고, 성공했을 때만 소진으로 찍는다.
 */
export const enhanceKeyword = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const keyword = Number(request.data?.keyword ?? 0);
  const freeShotRequested = request.data?.freeShot === true;

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }
  // CardKeyword 플래그 정수다 — 이름이 아니라 1·2·4·8·16·64 로 들어온다.
  if (!Number.isInteger(keyword) || keyword <= 0) {
    throw new HttpsError("invalid-argument", "keyword must be a positive CardKeyword flag.");
  }
  if (!isSupportedKeyword(keyword)) {
    reject("KeywordNotSupported", `Keyword flag ${keyword} is not enhanceable.`, {uid, env, keyword});
  }

  // 스펙 읽기는 트랜잭션 밖이다 — 유저 문서와 무관하고, 재실행마다 다시 읽으면 비용만 는다.
  const rows = await readSpecRows(env, "KeywordEnhance");
  const rule = parseKeywordEnhanceRules(rows).get(keyword);
  if (rule === undefined) {
    // 곡선 없이 차감할 수는 없다. 이 로그가 뜨면 그 키워드 행의 저작·업로드가 빠진 것이다.
    logger.error("KeywordEnhance spec has no row for this keyword", {uid, env, keyword, rowCount: rows.length});
    reject("RuleUnavailable", `Keyword ${keyword} is not authored in the KeywordEnhance spec.`,
      {uid, env, keyword, rowCount: rows.length});
  }

  let level = 0;
  let currency = "";
  let cost = 0;
  let freeShotUsed = false;

  const result = await mutateSave(env, uid, "enhanceKeyword", async (current, transaction, wallet): Promise<SaveMutation> => {
    const levels = readKeywordLevels(current.keywordGrowth);
    const currentLevel = levelOfKeyword(levels, keyword);

    const step = keywordEnhanceStep(rule, currentLevel);
    if (step === null) {
      reject("MaxLevel", `Keyword ${keyword} is already at the max level.`,
        {uid, env, keyword, level: currentLevel, maxLevel: rule.maxLevel});
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
      reject("NotAffordable", `Not enough ${step.currency} to enhance keyword ${keyword}.`,
        {uid, env, keyword, level: currentLevel, currency: step.currency, cost: charged,
          balance: balances[step.currency]});
    }

    if (grantsReference !== null && freeShot !== null) {
      writeGrantUsed(transaction, grantsReference, FREE_SHOT_AXIS, freeShot, FieldValue.serverTimestamp());
    }

    level = step.level;
    currency = step.currency;
    cost = charged;
    freeShotUsed = freeShot !== null;

    return {
      slots: {
        keywordGrowth: keywordGrowthSlot(setKeywordLevel(levels, keyword, step.level)),
      },
      wallet: nextWallet(wallet, spend(balances, step.currency, charged), "enhanceKeyword"),
    };
  });

  logger.info("enhanceKeyword", {
    uid, env, keyword, level, currency, cost,
    freeShotRequested, freeShotUsed,
    revision: result.revision,
  });

  return {...result, outcome: "Success", level, currency, cost, freeShotUsed};
});
