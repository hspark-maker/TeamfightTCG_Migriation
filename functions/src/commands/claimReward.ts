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
import {
  parseRankGradeRows,
  RankGradeRow,
  rankTierCount,
  requiredPointsForTier,
} from "../payout";
import {
  appendClaimedTier,
  isChapterOwnerId,
  judgeRewardClaim,
  MAX_CLAIMED_TIERS,
  parseRewardRows,
} from "../rewardTable";
import {
  AlbumEntryRow,
  albumScopeCardIds,
  AlbumThemeRow,
  isCompleted,
  lockedThemeIds,
  missingCount,
  parseAlbumEntryRows,
  parseAlbumScope,
  parseAlbumThemeRows,
} from "../completionTable";
import {
  ChapterNodeRow,
  chapterNodeIds,
  hasNode,
  MAX_NODE_ID_LENGTH,
  parseChapterNodeRows,
  readNodeIdList,
} from "../adventureTable";
import {readOwnedIds} from "../packs/packSlots";
import {grant} from "../currency/wallet";
import {nextWallet} from "../currency/walletStore";

/**
 * 도메인 거절 사유. **와이어 계약**이다 — 클라가 이 문자열을 그대로 대조한다.
 * 전부 permission-denied 로 나간다(save/domainReject): 보상 수령 실패로 세션을 끊지 않는다.
 */
type ClaimReject = "AlreadyClaimed" | "NotEligible" | "RewardNotFound";

/**
 * 수령 가능한 보상 소유자 축. 모험는 **정점과 챕터 완주가 같은 축**이고 ownerId 의
 * chapter_ 접두사로만 갈린다(Reward 표의 ownerType 열을 나누지 않으려는 계약이다).
 */
type ClaimOwnerType = "Rank" | "Adventure" | "Album";

/** 소유자 키 하나의 최대 길이. 저작 키는 node_01 · p:Theme_Nature/P1 처럼 짧다. */
const MAX_OWNER_ID_LENGTH = MAX_NODE_ID_LENGTH;

/** 판정·거절 로그가 공통으로 싣는 요청 맥락. */
interface ClaimContext {
  uid: string;
  env: string;
  ownerType: ClaimOwnerType;
  ownerId: string;
}

/**
 * 도메인 거절. 던지기와 로그는 save/domainReject 한 곳이고, 여기 남은 것은 사유 오타를 막는 타입 관문이다.
 * @param {ClaimReject} reason 사유 코드
 * @param {string} message 로그용 설명
 * @param {Record<string, unknown>} context 어느 값에 막혔는지
 */
function reject(reason: ClaimReject, message: string, context: Record<string, unknown>): never {
  rejectDomain(reason, message, context);
}

/**
 * 문자열 키 목록을 안전하게 읽는다. 빈 값·비문자열·중복은 버리고 나머지 순서는 보존한다
 * — 낙인 목록은 순서에 뜻이 없지만 되쓸 때 흔들 이유도 없다.
 * @param {unknown} value 문서의 리스트 값
 * @return {string[]} 정리된 키 목록
 */
// 정제 규약은 adventureTable 이 소유한다 — 상한을 한쪽만 고치면 갈린다.
const readIdList = readNodeIdList;

/**
 * 수령한 티어 목록. 티어 범위 밖 값을 걸러 낸다. 룰 상한(MAX_CLAIMED_TIERS)은 여기가 아니라
 * 쓰기 직전 appendClaimedTier 가 건다 — 읽기에서 잘라 내면 낙인이 조용히 사라진다.
 * @param {unknown} rank 문서의 rank 슬롯
 * @param {number} tierCount 전체 티어 수
 * @return {number[]} 오름차순 티어 인덱스
 */
function readClaimedTiers(rank: unknown, tierCount: number): number[] {
  const raw = (rank as {claimedTiers?: unknown} | undefined)?.claimedTiers;
  if (!Array.isArray(raw)) return [];

  const seen = new Set<number>();
  for (const entry of raw) {
    const tier = Number(entry);
    if (!Number.isInteger(tier) || tier < 0 || tier >= tierCount) continue;
    seen.add(tier);
  }
  return [...seen].sort((a, b) => a - b);
}

/**
 * 랭크 등급 표. 못 읽으면 자격을 잴 수 없으므로 NotEligible 로 떨어뜨린다
 * — failed-precondition 으로 던지면 클라 CloudFailureClassifier 가 세션을 끊는다.
 * @param {ClaimContext} context 요청 맥락
 * @return {Promise<RankGradeRow[]>} 등급 표
 */
async function loadRankGrades(context: ClaimContext): Promise<RankGradeRow[]> {
  const rows = await readSpecRows(context.env, "RankGrade");
  let grades: RankGradeRow[] = [];
  try {
    grades = parseRankGradeRows(rows);
  } catch (error) {
    logger.error("RankGrade spec is unreadable", {...context, rowCount: rows.length, error});
    reject("NotEligible", "Rank grade spec is unreadable.", {...context, specRowCount: rows.length});
  }
  if (grades.length === 0) {
    logger.error("RankGrade spec is empty", {...context});
    reject("NotEligible", "Rank grade spec is empty.", {...context, specRowCount: rows.length});
  }
  return grades;
}

/**
 * 도감 칸 표. 못 읽으면 완성 여부를 잴 수 없으므로 NotEligible 로 떨어뜨린다
 * — 여기서 통과시키면 모수 0 이 "다 모았다"로 읽혀 보상이 통째로 샌다.
 * @param {ClaimContext} context 요청 맥락
 * @return {Promise<AlbumEntryRow[]>} 칸 목록
 */
async function loadAlbumEntries(context: ClaimContext): Promise<AlbumEntryRow[]> {
  const rows = await readSpecRows(context.env, "AlbumEntry");
  const entries = parseAlbumEntryRows(rows);
  if (entries.length === 0) {
    logger.error("AlbumEntry spec is empty or unreadable", {...context, rowCount: rows.length});
    reject("NotEligible", "Album entry spec is unreadable.", {...context, specRowCount: rows.length});
  }
  return entries;
}

/**
 * 도감 테마 표. 준비 중(locked) 테마를 가리는 유일한 근거라 못 읽으면 NotEligible 로 떨어뜨린다
 * — 여기서 통과시키면 전체 완성이 준비 중 테마의 칸까지 요구해 영영 수령할 수 없게 된다.
 * @param {ClaimContext} context 요청 맥락
 * @return {Promise<AlbumThemeRow[]>} 테마 목록
 */
async function loadAlbumThemes(context: ClaimContext): Promise<AlbumThemeRow[]> {
  const rows = await readSpecRows(context.env, "AlbumThemeInfo");
  const themes = parseAlbumThemeRows(rows);
  if (themes.length === 0) {
    logger.error("AlbumThemeInfo spec is empty or unreadable", {...context, rowCount: rows.length});
    reject("NotEligible", "Album theme spec is unreadable.", {...context, specRowCount: rows.length});
  }
  return themes;
}

/**
 * 챕터↔정점 대응 표. 못 읽으면 완주를 잴 수 없으므로 NotEligible 로 떨어뜨린다.
 * @param {ClaimContext} context 요청 맥락
 * @return {Promise<ChapterNodeRow[]>} 대응 목록
 */
async function loadChapterNodes(context: ClaimContext): Promise<ChapterNodeRow[]> {
  const rows = await readSpecRows(context.env, "AdventureChapter");
  const entries = parseChapterNodeRows(rows);
  if (entries.length === 0) {
    logger.error("AdventureChapter spec is empty or unreadable", {...context, rowCount: rows.length});
    reject("NotEligible", "Adventure chapter spec is unreadable.", {...context, specRowCount: rows.length});
  }
  return entries;
}

/**
 * 랭크 티어 수령 — 낙인은 claimedTiers 다. rank 슬롯 **전체 값**을 돌려준다.
 *
 * Claimed 검사가 도달 검사보다 먼저다(클라 RankRewardManager.StateOf 와 같은 순서) —
 * 강등으로 도달 티어가 내려간 구간에서 수령 표시가 풀리면 안 된다.
 * @param {Record<string, unknown>} current 현재 문서
 * @param {number} tier 티어 인덱스
 * @param {number} required 요구 점수
 * @param {number} tierCount 전체 티어 수
 * @param {ClaimContext} context 요청 맥락
 * @return {object} rank 슬롯 전체 값
 */
function claimRankTier(
  current: Record<string, unknown>,
  tier: number,
  required: number,
  tierCount: number,
  context: ClaimContext,
): {points: number; claimedTiers: number[]} {
  const rank = current.rank as Record<string, unknown> | undefined;
  const points = Number(rank?.points ?? 0);
  if (!Number.isSafeInteger(points) || points < 0) {
    // NaN 은 어떤 비교도 통과시키지 않으므로 자격 검사보다 먼저 끊는다. 0 으로 되쓰면 랭크 진행도가 날아간다.
    logger.error("rank points are unreadable", {...context, points: rank?.points ?? null});
    reject("NotEligible", "Rank points are unreadable.", {...context, points: rank?.points ?? null});
  }

  const claimed = readClaimedTiers(rank, tierCount);
  if (claimed.includes(tier)) {
    reject("AlreadyClaimed", `Rank tier ${tier} is already claimed.`, {...context, tier});
  }
  if (points < required) {
    reject("NotEligible", `Rank tier ${tier} requires ${required} points.`, {...context, tier, points, required});
  }

  const claimedTiers = appendClaimedTier(claimed, tier);
  if (claimedTiers === null) {
    // "표를 늘렸는데 firestore.rules 를 안 늘렸다"는 운영 사고다. 넘긴 문서를 쓰면 그 계정의 이후 클라 저장이
    // 전부 PERMISSION_DENIED 가 되고 delete 도 룰에 막혀 복구 경로가 없다 — 수령 하나를 거부하는 편이 낫다.
    logger.error("claimedTiers would exceed the firestore.rules limit", {
      ...context, tier, claimedCount: claimed.length, limit: MAX_CLAIMED_TIERS,
    });
    reject("NotEligible", `Rank claim would exceed the claimedTiers limit of ${MAX_CLAIMED_TIERS}.`,
      {...context, tier, claimedCount: claimed.length, limit: MAX_CLAIMED_TIERS});
  }

  return {points, claimedTiers};
}

/**
 * 정점 수령 — 이 도메인은 "수령 = 클리어 확정"이라 낙인이 clearedNodeIds 하나다(별도 claimed 목록이 없다).
 * 해금 사슬은 여기서 재지 않는다 — reportAdventureWin 이 낙인을 세울 때 이미 쟀고,
 * 여기서 다시 재면 같은 판정이 두 곳에 생긴다. 이 자리가 보는 것은 그 낙인의 존재다.
 * 지급·클리어 낙인·미수령 해제가 한 트랜잭션이어야 지급됐는데 선물이 남는 상태가 저장되지 않는다.
 * @param {Record<string, unknown>} current 현재 문서
 * @param {ChapterNodeRow[]} chapterRows 챕터↔정점 대응 표
 * @param {ClaimContext} context 요청 맥락
 * @return {object} adventure 슬롯 전체 값
 */
function claimAdventureNode(
  current: Record<string, unknown>,
  chapterRows: ChapterNodeRow[],
  context: ClaimContext,
): {clearedNodeIds: string[]; claimedChapterIds: string[]; pendingRewardNodeId: string} {
  const adventure = current.adventure as Record<string, unknown> | undefined;
  const cleared = readIdList(adventure?.clearedNodeIds);
  const pending = typeof adventure?.pendingRewardNodeId === "string" ? adventure.pendingRewardNodeId : "";

  if (cleared.includes(context.ownerId)) {
    reject("AlreadyClaimed", `Adventure node '${context.ownerId}' is already cleared.`, {...context, pending});
  }
  if (pending !== context.ownerId) {
    reject("NotEligible", `Adventure node '${context.ownerId}' has no pending reward.`, {...context, pending});
  }
  // 낙인이 표 밖 정점을 가리키면 거절한다 — 해금 판정이 서버로 오기 전(reportAdventureWin 이전)
  // 클라가 스스로 찍어 둔 임의 낙인이 그대로 수령되는 창구를 막는다.
  if (!hasNode(chapterRows, context.ownerId)) {
    reject("NotEligible", `Adventure node '${context.ownerId}' is not in the chapter spec.`,
      {...context, pending, specRowCount: chapterRows.length});
  }

  return {
    clearedNodeIds: [...cleared, context.ownerId],
    // 챕터 낙인은 이 명령의 소관이 아니다 — 슬롯 전체 값을 쓰므로 그대로 실어 보내야 지워지지 않는다.
    claimedChapterIds: readIdList(adventure?.claimedChapterIds),
    pendingRewardNodeId: "",
  };
}

/**
 * 챕터 완주 수령 — 낙인은 claimedChapterIds 다. adventure 슬롯 **전체 값**을 돌려준다.
 *
 * 정점 진행(clearedNodeIds)과 미수령 정점(pendingRewardNodeId)은 이 명령의 소관이 아니지만
 * 슬롯 단위 덮어쓰기라 그대로 실어 보내야 지워지지 않는다 — 특히 pendingRewardNodeId 는
 * 다음 챕터의 미수령 정점을 가리킬 수 있어 비우면 그 정점의 보상이 사라진다.
 * @param {Record<string, unknown>} current 현재 문서
 * @param {ChapterNodeRow[]} chapterRows 챕터↔정점 대응 표
 * @param {ClaimContext} context 요청 맥락
 * @return {object} adventure 슬롯 전체 값
 */
function claimAdventureChapter(
  current: Record<string, unknown>,
  chapterRows: ChapterNodeRow[],
  context: ClaimContext,
): {clearedNodeIds: string[]; claimedChapterIds: string[]; pendingRewardNodeId: string} {
  const adventure = current.adventure as Record<string, unknown> | undefined;
  const claimedChapters = readIdList(adventure?.claimedChapterIds);

  // Claimed 검사가 완주 검사보다 먼저다 — 저작에서 정점이 늘어 완주가 풀려도 기수령은 유지된다.
  if (claimedChapters.includes(context.ownerId)) {
    reject("AlreadyClaimed", `Adventure chapter '${context.ownerId}' is already claimed.`, {...context});
  }

  const required = chapterNodeIds(chapterRows, context.ownerId);
  const cleared = readIdList(adventure?.clearedNodeIds);
  if (!isCompleted(required, new Set(cleared))) {
    reject("NotEligible", `Adventure chapter '${context.ownerId}' is not complete.`,
      {...context, requiredCount: required.length, missingCount: missingCount(required, new Set(cleared))});
  }

  return {
    clearedNodeIds: cleared,
    claimedChapterIds: [...claimedChapters, context.ownerId],
    pendingRewardNodeId: typeof adventure?.pendingRewardNodeId === "string" ? adventure.pendingRewardNodeId : "",
  };
}

/**
 * 도감 완성 수령 — 낙인은 claimedKeys 다. albumReward 슬롯 **전체 값**을 돌려준다.
 *
 * 진행도는 저장하지 않는다(클라 AlbumRewardSaveData 와 같은 축) — 자격은 소유 카드로
 * 서버가 매번 다시 잰다. 표에 그 범위 행이 하나도 없으면 완성이 아니라 미저작이다.
 * @param {Record<string, unknown>} current 현재 문서
 * @param {AlbumEntryRow[]} entryRows 도감 칸 표
 * @param {AlbumThemeRow[]} themeRows 도감 테마 표(준비 중 테마를 가린다)
 * @param {ClaimContext} context 요청 맥락
 * @return {object} albumReward 슬롯 전체 값
 */
function claimAlbumReward(
  current: Record<string, unknown>,
  entryRows: AlbumEntryRow[],
  themeRows: AlbumThemeRow[],
  context: ClaimContext,
): {claimedKeys: string[]} {
  const album = current.albumReward as Record<string, unknown> | undefined;
  const claimedKeys = readIdList(album?.claimedKeys);

  // Claimed 검사가 완성 검사보다 먼저다(클라 AlbumRewardManager.StateOf 와 같은 순서).
  if (claimedKeys.includes(context.ownerId)) {
    reject("AlreadyClaimed", `Album reward '${context.ownerId}' is already claimed.`, {...context});
  }

  const scope = parseAlbumScope(context.ownerId);
  const required = scope === null ? [] : albumScopeCardIds(entryRows, scope, lockedThemeIds(themeRows));
  const owned = new Set(readOwnedIds(current.ownership));
  if (!isCompleted(required, owned)) {
    reject("NotEligible", `Album reward '${context.ownerId}' is not complete.`,
      {...context, requiredCount: required.length, missingCount: missingCount(required, owned)});
  }

  return {claimedKeys: [...claimedKeys, context.ownerId]};
}

/**
 * 정적 보상 수령. 자격 판정·지급·낙인을 서버가 소유한다.
 *
 * 범위는 네 갈래다 — 랭크 티어 · 모험 정점 · 모험 챕터 완주 · 도감 완성.
 * 판정 근거는 전부 스펙 표에 있고(RankGrade · AdventureChapter · AlbumEntry · AlbumThemeInfo) 표가 비면
 * fail-closed 로 거절한다. 지급은 지갑 문서로 나가고 세이브에는 낙인 슬롯만 남는다.
 */
export const claimReward = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const ownerType = String(request.data?.ownerType ?? "") as ClaimOwnerType;
  const ownerId = String(request.data?.ownerId ?? "").trim();

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }
  if (ownerType !== "Rank" && ownerType !== "Adventure" && ownerType !== "Album") {
    throw new HttpsError("invalid-argument",
      `ownerType must be Rank, Adventure or Album, got '${ownerType}'.`);
  }
  if (ownerId.length === 0 || ownerId.length > MAX_OWNER_ID_LENGTH) {
    throw new HttpsError("invalid-argument", "ownerId must be a non-empty string.");
  }

  const context: ClaimContext = {uid, env, ownerType, ownerId};
  const isChapter = ownerType === "Adventure" && isChapterOwnerId(ownerId);

  // 스펙 읽기는 트랜잭션 밖이다 — 유저 문서와 무관하고, 재실행마다 다시 읽으면 비용만 는다.
  let tierIndex = -1;
  let tierCount = 0;
  let requiredPoints = 0;
  let albumEntries: AlbumEntryRow[] = [];
  let albumThemes: AlbumThemeRow[] = [];
  let chapterNodes: ChapterNodeRow[] = [];
  if (ownerType === "Rank") {
    const grades = await loadRankGrades(context);
    tierCount = rankTierCount(grades);
    tierIndex = Number(ownerId);
    const required = requiredPointsForTier(tierIndex, grades);
    if (required === null) {
      reject("RewardNotFound", `Rank tier '${ownerId}' is out of range.`, {...context, tierCount});
    }
    requiredPoints = required;
  } else if (ownerType === "Album") {
    // 낙인 키 모양이 아니면 잴 범위 자체가 없다 — 표를 읽기 전에 끊는다.
    if (parseAlbumScope(ownerId) === null) {
      reject("RewardNotFound", `Album owner '${ownerId}' is not a reward key.`, {...context});
    }
    albumEntries = await loadAlbumEntries(context);
    albumThemes = await loadAlbumThemes(context);
  } else if (ownerType === "Adventure") {
    // 챕터뿐 아니라 정점 수령도 읽는다 — 낙인이 표에 없는 정점을 가리키는지 대조하는 데 쓴다.
    chapterNodes = await loadChapterNodes(context);
  }

  // 랭크는 티어 인덱스를 정규 표기로 되돌려 조회한다 — 클라 RankConfig.FillRewards 가 쓰는 키와 같아야 한다.
  const specOwnerId = ownerType === "Rank" ? String(tierIndex) : ownerId;
  const rewardRows = parseRewardRows(await readSpecRows(env, "Reward"));
  const judgement = judgeRewardClaim(rewardRows, ownerType, specOwnerId);
  const {gains, dropped} = judgement;

  if (dropped.length > 0) {
    // 저작 실수를 조용히 삼키지 않는다 — 카드 보상이 저작되면 UnknownRewardType 으로 여기 뜬다.
    logger.warn("reward rows dropped", {...context, specOwnerId, dropped});
  }
  if (!judgement.allow) {
    if (judgement.specEmpty) {
      // 표를 통째로 못 읽은 것은 저작이 없는 것과 다르다 — 배포/업로드 사고이고 유저 잘못이 아니다.
      // 여기서 모험를 통과시키면 클리어 낙인만 남고 재수령이 AlreadyClaimed 로 막혀 보상을 영영 못 받는다.
      logger.error("Reward spec is empty — refusing every claim until it is uploaded",
        {...context, specOwnerId, specRowCount: rewardRows.length});
    }
    reject(judgement.reason,
      judgement.specEmpty ?
        "Reward spec is unreadable." :
        `No reward is authored for ${ownerType}/${specOwnerId}.`,
      {...context, specOwnerId, specEmpty: judgement.specEmpty, droppedCount: dropped.length});
  }
  if (!judgement.authored) {
    // "보상 미저작 정점은 해금만 넘긴다"가 저작 규약이다 — 여기서 막으면 그 정점이 영영 RewardPending 으로 굳는다.
    // 랭크·도감·챕터는 여기까지 오지 않는다(judgeRewardClaim 이 RewardNotFound 로 끊었다).
    logger.warn("clearing a node with no authored reward", {...context, specOwnerId, droppedCount: dropped.length});
  }

  // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
  const txId = clientReceiptId(request.data?.txId, randomUUID());

  // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
  // finalize 안에서 뒤집는다 — 트랜잭션 재실행마다 다시 돌아도 결과가 같다.
  let replayed = true;

  const result = await mutateSave(env, uid, "claimReward", {kind: "client", txId},
    (current, _transaction, wallet): SaveMutation => {
      // 지급은 자격 판정보다 먼저 계산해도 안전하다 — 거절은 아래 낙인 함수들이 던지고, 던지면 트랜잭션 전체가 없던 일이 된다.
      // 줄 것이 없으면 지갑을 아예 쓰지 않는다(claimBattleReward·claimPayout 과 같은 정책) — 보상 미저작 정점의
      // 해금 수령이 빈 지급으로 rev 만 올리면 클라가 달라진 것 없는 잔액을 채택하고 사고를 못 알아챈다.
      const paid = gains.length === 0 ?
        undefined :
        nextWallet(wallet, grant(wallet.balances, gains), "claimReward");

      if (ownerType === "Rank") {
        const rank = claimRankTier(current, tierIndex, requiredPoints, tierCount, context);
        return {slots: {rank}, wallet: paid};
      }
      if (ownerType === "Album") {
        return {
          slots: {albumReward: claimAlbumReward(current, albumEntries, albumThemes, context)},
          wallet: paid,
        };
      }
      if (isChapter) {
        return {slots: {adventure: claimAdventureChapter(current, chapterNodes, context)}, wallet: paid};
      }
      return {slots: {adventure: claimAdventureNode(current, chapterNodes, context)}, wallet: paid};
    },
    (adopted) => {
      replayed = false;
      return {...adopted, granted: gains};
    });

  if (replayed) {
    logger.info("receipt replay", {uid, env, source: "claimReward", txId, revision: result.revision});
  } else {
    logger.info("claimReward", {
      uid, env, ownerType, ownerId: specOwnerId,
      granted: gains.map((gain) => `${gain.currency}+${gain.amount}`).join(","),
      droppedCount: dropped.length,
      revision: result.revision,
      txIdSource: isClientReceiptId(request.data?.txId) ? "client" : "server",
    });
  }

  return result;
});
