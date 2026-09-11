import {measuredCallable} from "../observability/requestMetrics";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {FieldValue} from "firebase-admin/firestore";
import {randomInt, randomUUID} from "node:crypto";
import {db} from "../firebaseApp";
import {EVENTS} from "../analytics/eventNames";
import {recordEvent} from "../observability/analyticsEvent";
import {
  commitMissionBump,
  missionBumpFromSnapshot,
  missionResponse,
  MissionResponse,
  missionsRef,
} from "../missions/missionStore";
import {missionPeriod} from "../missions/period";
import {readMissionCatalog} from "../missions/missionSpec";
import {applyGuideProgress} from "../missions/guideMutation";
import {
  isKnownEnv,
  mutateSave,
  requireUid,
  SaveMutation,
} from "../save/saveDocument";
import {loadCatalogIds} from "../packs/cardCatalog";
import {DrawnCard, drawPack, resolveDropPool} from "../packs/packDraw";
import {buildOwnershipSlot, readOwnedIds} from "../packs/packSlots";
import {canAfford, spend, grant, CurrencyGain} from "../currency/wallet";
import {applyDrawnSnackGrowth, duplicateGains} from "../rewards/itemGrant";
import {parseRewardRows} from "../rewardTable";
import {rankRef} from "../rank/rankStore";
import {nextWallet} from "../currency/walletStore";
import {growthSlot, readGrowthEntries} from "../growth/cardGrowth";
import {requireSnackGrowthCurve} from "../growth/snackGrowthSpec";
import {applySnackGrowthProgress} from "../missions/snackGrowthProgress";
import {rejectDomain} from "../save/domainReject";
import {clientReceiptId, isClientReceiptId} from "../save/receiptId";
import {
  readCardPackRow,
  readDropRows,
  readRankGradeRows,
  readSpecRows,
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
export const openPack = onCall(measuredCallable("openPack", async (request) => {
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

  const [dropRows, gradeRows, catalogIds, cardRows, rawRewards, catalog, ruleRows, curveRows] = await Promise.all([
    readDropRows(env, packId),
    readRankGradeRows(env),
    loadCatalogIds(env),
    readSpecRows(env, "Card"),
    pack.price > 0 ? readSpecRows(env, "Reward") : Promise.resolve([]),
    readMissionCatalog(env),
    readSpecRows(env, "CardEnhanceRule"),
    readSpecRows(env, "CardLimitBreak"),
  ]);
  const snackGrowthCurve = requireSnackGrowthCurve(ruleRows, curveRows);
  const duplicateRows = parseRewardRows(rawRewards);
  const cardGrades = new Map(cardRows.map((row) => [Number(row.id), String(row.grade)]));
  let granted: CurrencyGain[] = [];

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
  let missionState: MissionResponse | undefined;
  // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
  // finalize 안에서 뒤집는다 — 트랜잭션 재실행마다 다시 돌아도 결과가 같다.
  let replayed = true;

  // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
  const txId = clientReceiptId(request.data?.txId, randomUUID());

  // 기간은 여기서 **한 번만** 잰다 — 트랜잭션 콜백은 재실행되므로 그 안에서 재면
  // 경계에 걸린 호출이 어느 기간에 실릴지가 재실행 운에 달린다.
  const period = missionPeriod(Date.now());

  const result = await mutateSave(env, uid, "openPack", {kind: "client", txId},
    async (current, transaction, wallet): Promise<SaveMutation> => {
      // 독립 문서는 함께 읽고, 미션·지갑 쓰기 전에 모두 확보한다.
      const missionReference = missionsRef(db, env, uid);
      const [missionSnapshot, rankSnapshot] = await transaction.getAll(
        missionReference, rankRef(db, env, uid));
      const missions = missionBumpFromSnapshot(missionReference, missionSnapshot, period);
      // 트랜잭션이 재실행되면 이전 추첨을 버리고 다시 뽑는다 — 잔액·소유와 정합해야 한다.
      const points = Number(rankSnapshot.data()?.points ?? (current.rank as {points?: unknown} | undefined)?.points ?? 0);
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

      const balances = wallet.balances;
      if (!canAfford(balances, pack.priceType, pack.price)) {
        reject("InsufficientGold", `Not enough ${pack.priceType} for pack '${packId}'.`,
          {uid, env, packId, priceType: pack.priceType, price: pack.price, balance: balances[pack.priceType]});
      }

      const owned = readOwnedIds(current.ownership);
      const ownedSet = new Set(owned);
      drawn = drawPack(pool, pack.drawCount, pack.uniqueDraw, catalogIds, ownedSet, randomInt);

      granted = pack.price > 0 ? duplicateGains(drawn, cardGrades, duplicateRows) : [];
      const paid = grant(spend(balances, pack.priceType, pack.price), granted);
      goldBefore = balances[pack.priceType];
      goldAfter = paid[pack.priceType];

      const slots = {
        ownership: buildOwnershipSlot(owned, drawn),
        cardGrowth: growthSlot(applyDrawnSnackGrowth(readGrowthEntries(current.cardGrowth), drawn, snackGrowthCurve, cardGrades)),
      };
      applyGuideProgress(missions, current, slots, cardRows, catalog);
      applySnackGrowthProgress(missions, drawn);

      // 진행도는 콜백 **안**에서 올린다 — mutateSave 는 영수증이 히트하면 이 콜백을 통째로 건너뛰므로,
      // 그 덕에 재시도가 진행도를 두 번 올리지 않는다. 콜백 밖으로 옮기면 그 보장이 사라진다.
      commitMissionBump(transaction, missions, EVENTS.packOpened.missionKey, 1, FieldValue.serverTimestamp());
      missionState = missionResponse(missions.state, period, catalog);

      return {
        slots,
        wallet: nextWallet(wallet, paid, "openPack"),
      };
    },
    (adopted) => {
      replayed = false;
      return {...adopted, packId, cards: drawn, granted, refundType: pack.refundType, missions: missionState};
    });

  if (replayed) {
    logger.info("receipt replay", {uid, env, source: "openPack", txId, revision: result.revision});
  } else {
    recordEvent(EVENTS.packOpened.name, {
      uid, env, eventId: txId, sourceCommand: "openPack", result: "success", packId,
      priceType: pack.priceType, price: pack.price,
      drawCount: pack.drawCount, uniqueDraw: pack.uniqueDraw, poolSize,
      drawn: drawn.map((card) => `${card.cardId}${card.isNew ? "+" : "="}`).join(","),
      goldBefore, goldAfter,
      specSource: entryPoints === null ? "rankFallback" : "spec",
      revision: result.revision,
      txIdSource: isClientReceiptId(request.data?.txId) ? "client" : "server",
    });
  }

  return result;
}));
