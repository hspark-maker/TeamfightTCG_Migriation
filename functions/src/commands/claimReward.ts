import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {
  isKnownEnv,
  mutateSave,
  requireUid,
  SlotPatch,
} from "../save/saveDocument";
import {rejectDomain} from "../save/domainReject";
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
  ChapterNodeRow,
  chapterNodeIds,
  isCompleted,
  missingCount,
  parseAlbumEntryRows,
  parseAlbumScope,
  parseChapterNodeRows,
} from "../completionTable";
import {readOwnedIds} from "../packs/packSlots";
import {currencySlot, grant, readBalances} from "../currency/wallet";

/**
 * 도메인 거절 사유. **와이어 계약**이다 — 클라가 이 문자열을 그대로 대조한다.
 * 전부 permission-denied 로 나간다(save/domainReject): 보상 수령 실패로 세션을 끊지 않는다.
 */
type ClaimReject = "AlreadyClaimed" | "NotEligible" | "RewardNotFound";

/**
 * 수령 가능한 보상 소유자 축. 토너먼트는 **정점과 챕터 완주가 같은 축**이고 ownerId 의
 * chapter_ 접두사로만 갈린다(Reward 표의 ownerType 열을 나누지 않으려는 계약이다).
 */
type ClaimOwnerType = "Rank" | "Tournament" | "Album";

/** 소유자 키 하나의 최대 길이. 저작 키는 node_01 · p:Theme_Nature/P1 처럼 짧다. */
const MAX_OWNER_ID_LENGTH = 64;

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
function readIdList(value: unknown): string[] {
  if (!Array.isArray(value)) return [];

  const seen = new Set<string>();
  for (const entry of value) {
    if (typeof entry !== "string") continue;
    if (entry.length === 0 || entry.length > MAX_OWNER_ID_LENGTH) continue;
    seen.add(entry);
  }
  return [...seen];
}

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
 * 챕터↔정점 대응 표. 못 읽으면 완주를 잴 수 없으므로 NotEligible 로 떨어뜨린다.
 * @param {ClaimContext} context 요청 맥락
 * @return {Promise<ChapterNodeRow[]>} 대응 목록
 */
async function loadChapterNodes(context: ClaimContext): Promise<ChapterNodeRow[]> {
  const rows = await readSpecRows(context.env, "TournamentChapter");
  const entries = parseChapterNodeRows(rows);
  if (entries.length === 0) {
    logger.error("TournamentChapter spec is empty or unreadable", {...context, rowCount: rows.length});
    reject("NotEligible", "Tournament chapter spec is unreadable.", {...context, specRowCount: rows.length});
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
 * 지급·클리어 낙인·미수령 해제가 한 트랜잭션이어야 지급됐는데 선물이 남는 상태가 저장되지 않는다.
 * @param {Record<string, unknown>} current 현재 문서
 * @param {ClaimContext} context 요청 맥락
 * @return {object} tournament 슬롯 전체 값
 */
function clearTournamentNode(
  current: Record<string, unknown>,
  context: ClaimContext,
): {clearedNodeIds: string[]; claimedChapterIds: string[]; pendingRewardNodeId: string} {
  const tournament = current.tournament as Record<string, unknown> | undefined;
  const cleared = readIdList(tournament?.clearedNodeIds);
  const pending = typeof tournament?.pendingRewardNodeId === "string" ? tournament.pendingRewardNodeId : "";

  if (cleared.includes(context.ownerId)) {
    reject("AlreadyClaimed", `Tournament node '${context.ownerId}' is already cleared.`, {...context, pending});
  }
  if (pending !== context.ownerId) {
    reject("NotEligible", `Tournament node '${context.ownerId}' has no pending reward.`, {...context, pending});
  }

  return {
    clearedNodeIds: [...cleared, context.ownerId],
    // 챕터 낙인은 이 명령의 소관이 아니다 — 슬롯 전체 값을 쓰므로 그대로 실어 보내야 지워지지 않는다.
    claimedChapterIds: readIdList(tournament?.claimedChapterIds),
    pendingRewardNodeId: "",
  };
}

/**
 * 챕터 완주 수령 — 낙인은 claimedChapterIds 다. tournament 슬롯 **전체 값**을 돌려준다.
 *
 * 정점 진행(clearedNodeIds)과 미수령 정점(pendingRewardNodeId)은 이 명령의 소관이 아니지만
 * 슬롯 단위 덮어쓰기라 그대로 실어 보내야 지워지지 않는다 — 특히 pendingRewardNodeId 는
 * 다음 챕터의 미수령 정점을 가리킬 수 있어 비우면 그 정점의 보상이 사라진다.
 * @param {Record<string, unknown>} current 현재 문서
 * @param {ChapterNodeRow[]} chapterRows 챕터↔정점 대응 표
 * @param {ClaimContext} context 요청 맥락
 * @return {object} tournament 슬롯 전체 값
 */
function claimTournamentChapter(
  current: Record<string, unknown>,
  chapterRows: ChapterNodeRow[],
  context: ClaimContext,
): {clearedNodeIds: string[]; claimedChapterIds: string[]; pendingRewardNodeId: string} {
  const tournament = current.tournament as Record<string, unknown> | undefined;
  const claimedChapters = readIdList(tournament?.claimedChapterIds);

  // Claimed 검사가 완주 검사보다 먼저다 — 저작에서 정점이 늘어 완주가 풀려도 기수령은 유지된다.
  if (claimedChapters.includes(context.ownerId)) {
    reject("AlreadyClaimed", `Tournament chapter '${context.ownerId}' is already claimed.`, {...context});
  }

  const required = chapterNodeIds(chapterRows, context.ownerId);
  const cleared = readIdList(tournament?.clearedNodeIds);
  if (!isCompleted(required, new Set(cleared))) {
    reject("NotEligible", `Tournament chapter '${context.ownerId}' is not complete.`,
      {...context, requiredCount: required.length, missingCount: missingCount(required, new Set(cleared))});
  }

  return {
    clearedNodeIds: cleared,
    claimedChapterIds: [...claimedChapters, context.ownerId],
    pendingRewardNodeId: typeof tournament?.pendingRewardNodeId === "string" ? tournament.pendingRewardNodeId : "",
  };
}

/**
 * 도감 완성 수령 — 낙인은 claimedKeys 다. albumReward 슬롯 **전체 값**을 돌려준다.
 *
 * 진행도는 저장하지 않는다(클라 AlbumRewardSaveData 와 같은 축) — 자격은 소유 카드로
 * 서버가 매번 다시 잰다. 표에 그 범위 행이 하나도 없으면 완성이 아니라 미저작이다.
 * @param {Record<string, unknown>} current 현재 문서
 * @param {AlbumEntryRow[]} entryRows 도감 칸 표
 * @param {ClaimContext} context 요청 맥락
 * @return {object} albumReward 슬롯 전체 값
 */
function claimAlbumReward(
  current: Record<string, unknown>,
  entryRows: AlbumEntryRow[],
  context: ClaimContext,
): {claimedKeys: string[]} {
  const album = current.albumReward as Record<string, unknown> | undefined;
  const claimedKeys = readIdList(album?.claimedKeys);

  // Claimed 검사가 완성 검사보다 먼저다(클라 AlbumRewardManager.StateOf 와 같은 순서).
  if (claimedKeys.includes(context.ownerId)) {
    reject("AlreadyClaimed", `Album reward '${context.ownerId}' is already claimed.`, {...context});
  }

  const scope = parseAlbumScope(context.ownerId);
  const required = scope === null ? [] : albumScopeCardIds(entryRows, scope);
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
 * 범위는 네 갈래다 — 랭크 티어 · 토너먼트 정점 · 토너먼트 챕터 완주 · 도감 완성.
 * 판정 근거는 전부 스펙 표에 있고(RankGrade · TournamentChapter · AlbumEntry) 표가 비면
 * fail-closed 로 거절한다. 재화는 지갑 문서가 아니라 세이브의 currency 슬롯에 쓴다.
 */
export const claimReward = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const ownerType = String(request.data?.ownerType ?? "") as ClaimOwnerType;
  const ownerId = String(request.data?.ownerId ?? "").trim();

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }
  if (ownerType !== "Rank" && ownerType !== "Tournament" && ownerType !== "Album") {
    throw new HttpsError("invalid-argument",
      `ownerType must be Rank, Tournament or Album, got '${ownerType}'.`);
  }
  if (ownerId.length === 0 || ownerId.length > MAX_OWNER_ID_LENGTH) {
    throw new HttpsError("invalid-argument", "ownerId must be a non-empty string.");
  }

  const context: ClaimContext = {uid, env, ownerType, ownerId};
  const isChapter = ownerType === "Tournament" && isChapterOwnerId(ownerId);

  // 스펙 읽기는 트랜잭션 밖이다 — 유저 문서와 무관하고, 재실행마다 다시 읽으면 비용만 는다.
  let tierIndex = -1;
  let tierCount = 0;
  let requiredPoints = 0;
  let albumEntries: AlbumEntryRow[] = [];
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
  } else if (isChapter) {
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
      // 여기서 토너먼트를 통과시키면 클리어 낙인만 남고 재수령이 AlreadyClaimed 로 막혀 보상을 영영 못 받는다.
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

  const result = await mutateSave(env, uid, (current): SlotPatch => {
    const currency = currencySlot(grant(readBalances(current.currency), gains));

    if (ownerType === "Rank") {
      return {currency, rank: claimRankTier(current, tierIndex, requiredPoints, tierCount, context)};
    }
    if (ownerType === "Album") {
      return {currency, albumReward: claimAlbumReward(current, albumEntries, context)};
    }
    if (isChapter) {
      return {currency, tournament: claimTournamentChapter(current, chapterNodes, context)};
    }
    return {currency, tournament: clearTournamentNode(current, context)};
  });

  logger.info("claimReward", {
    uid, env, ownerType, ownerId: specOwnerId,
    granted: gains.map((gain) => `${gain.currency}+${gain.amount}`).join(","),
    droppedCount: dropped.length,
    revision: result.revision,
  });

  return {...result, granted: gains};
});
