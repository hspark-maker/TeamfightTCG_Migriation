import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {randomInt} from "node:crypto";
import {
  isKnownEnv,
  mutateSave,
  requireUid,
  SlotPatch,
} from "../save/saveDocument";
import {loadCatalogIds} from "../packs/cardCatalog";
import {DrawnCard, drawPack, resolveDropPool} from "../packs/packDraw";
import {buildOwnershipSlot, readOwnedIds} from "../packs/packSlots";
import {canAfford, currencySlot, readBalances, spend} from "../currency/wallet";
import {addSnack, growthSlot, readGrowthEntries} from "../growth/cardGrowth";
import {rejectDomain} from "../save/domainReject";
import {
  readCardPackRow,
  readDropRows,
  readRankGradeRows,
} from "../packs/packSpecReader";
import {
  entryPointsFromRows,
  FALLBACK_ENTRY_POINTS,
  gradeOf,
  isRanked,
  parseRequiredGrade,
} from "../packs/rankGrade";

/**
 * 도메인 거절 사유. 클라 EPackOpenResult 의 이름과 같아야 한다
 * — 클라가 이 문자열을 그대로 파싱해 실패 팝업을 고른다.
 */
type PackReject = "PackNotFound" | "RankLocked" | "EmptyPool" | "InsufficientGold";

/**
 * 도메인 거절. 던지기와 로그는 save/domainReject 한 곳이고, 여기 남은 것은 사유 오타를 막는 타입 관문이다.
 * @param {PackReject} reason 사유 코드
 * @param {string} message 로그용 설명
 * @param {Record<string, unknown>} context 어느 값에 막혔는지
 */
function reject(reason: PackReject, message: string, context: Record<string, unknown>): never {
  rejectDomain(reason, message, context);
}

/**
 * 카드팩 구매·개봉. 잠금 판정·풀 해석·차감·추첨·지급을 서버가 소유한다.
 *
 * 클라(CardPackOpener)는 같은 검사를 사전에 한 번 더 하지만 그건 왕복을 아끼는 낙관 검사이고,
 * 판정의 진실원은 여기다.
 */
export const openPack = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const packId = String(request.data?.packId ?? "");

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }
  if (packId.length === 0 || packId.length > 64) {
    throw new HttpsError("invalid-argument", "packId must be a non-empty string.");
  }

  // 스펙 읽기는 트랜잭션 밖이다 — 유저 문서와 무관하고, 재실행마다 다시 읽으면 비용만 는다.
  const pack = await readCardPackRow(env, packId);
  if (pack === null) {
    // 클라는 시트에 행이 없으면 SO 인스펙터 값으로 폴백하지만 서버는 SO 를 못 본다.
    // 이 로그가 뜨면 시트 저작이 빠진 것이고, 그 팩은 서버에서 영영 못 연다.
    logger.error("pack row missing from the CardPack spec", {uid, env, packId});
    reject("PackNotFound", `Pack '${packId}' is not authored in the CardPack spec.`, {uid, env, packId});
  }
  if (pack.refundAmount > 0) {
    // 환급 경로는 클라·서버 양쪽에서 죽어 있다(중복 보상은 간식). 저작 실수를 조용히 삼키지 않는다.
    logger.warn("pack authors a refund that is never paid out", {env, packId, refundAmount: pack.refundAmount});
  }

  const [dropRows, gradeRows, catalogIds] = await Promise.all([
    readDropRows(env, packId),
    readRankGradeRows(env),
    loadCatalogIds(env),
  ]);

  const entryPoints = entryPointsFromRows(gradeRows);
  if (entryPoints === null) {
    // 임계치가 없으면 잠금이 통째로 어긋난다 — 폴백으로 돌되 반드시 보이게 남긴다.
    logger.error("RankGrade spec is unusable, falling back to built-in thresholds", {env, rowCount: gradeRows.length});
  }
  const thresholds = entryPoints ?? FALLBACK_ENTRY_POINTS;

  let drawn: DrawnCard[] = [];
  let goldBefore = 0;
  let goldAfter = 0;
  let poolSize = 0;

  const result = await mutateSave(env, uid, (current): SlotPatch => {
    // 트랜잭션이 재실행되면 이전 추첨을 버리고 다시 뽑는다 — 잔액·소유와 정합해야 한다.
    const points = Number((current.rank as {points?: unknown} | undefined)?.points ?? 0);
    const grade = gradeOf(thresholds, points);

    const required = parseRequiredGrade(pack.minRankGrade);
    if (required !== null && (!isRanked(thresholds, points) || grade < required)) {
      reject("RankLocked", `Pack '${packId}' requires rank grade ${required}.`,
        {uid, env, packId, points, grade, required});
    }

    const pool = resolveDropPool(dropRows, grade, catalogIds);
    if (pool.length === 0) {
      reject("EmptyPool", `Pack '${packId}' has no drawable card at grade ${grade}.`,
        {uid, env, packId, grade, dropRowCount: dropRows.length, catalogSize: catalogIds.size});
    }
    poolSize = pool.length;

    const balances = readBalances(current.currency);
    if (!canAfford(balances, pack.priceType, pack.price)) {
      reject("InsufficientGold", `Not enough ${pack.priceType} for pack '${packId}'.`,
        {uid, env, packId, priceType: pack.priceType, price: pack.price, balance: balances[pack.priceType]});
    }

    const owned = readOwnedIds(current.ownership);
    const ownedSet = new Set(owned);
    drawn = drawPack(pool, pack.drawCount, pack.uniqueDraw, catalogIds, ownedSet, randomInt);

    const currency = currencySlot(spend(balances, pack.priceType, pack.price));
    goldBefore = balances[pack.priceType];
    goldAfter = currency.balances[pack.priceType];

    return {
      currency,
      ownership: buildOwnershipSlot(owned, drawn),
      cardGrowth: growthSlot(drawn.reduce(
        (entries, card) => addSnack(entries, card.cardId, card.snack),
        readGrowthEntries(current.cardGrowth))),
    };
  });

  logger.info("openPack", {
    uid, env, packId,
    priceType: pack.priceType, price: pack.price,
    drawCount: pack.drawCount, uniqueDraw: pack.uniqueDraw, poolSize,
    drawn: drawn.map((card) => `${card.cardId}${card.isNew ? "+" : "="}`).join(","),
    goldBefore, goldAfter,
    specSource: entryPoints === null ? "rankFallback" : "spec",
    revision: result.revision,
  });

  return {
    ...result,
    packId,
    cards: drawn,
    refundType: pack.refundType,
  };
});
