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
  parseRewardRows,
  RankGradeRow,
  rankTierCount,
  requiredPointsForTier,
  resolveRewards,
} from "../payout";
import {currencySlot, grant, readBalances} from "../currency/wallet";

/**
 * 도메인 거절 사유. **와이어 계약**이다 — 클라가 이 문자열을 그대로 대조한다.
 * 전부 permission-denied 로 나간다(save/domainReject): 보상 수령 실패로 세션을 끊지 않는다.
 */
type ClaimReject = "AlreadyClaimed" | "NotEligible" | "RewardNotFound";

/** 수령 가능한 보상 소유자 축. 앨범 완성과 토너먼트 **챕터** 완주는 서버가 잴 근거가 없어 여기 없다. */
type ClaimOwnerType = "Rank" | "Tournament";

/** 소유자 키 하나의 최대 길이. 저작 키는 node_01 처럼 짧다. */
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
 * 수령한 티어 목록. 룰이 claimedTiers 를 size() <= 20 으로 막으므로 티어 범위 밖 값을 걸러
 * 길이를 티어 수 이하로 묶는다 — 넘긴 문서를 쓰면 그 계정의 이후 클라 저장이 전부 거부된다.
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

  return {points, claimedTiers: [...claimed, tier].sort((a, b) => a - b)};
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
 * 정적 보상 수령. 자격 판정·지급·낙인을 서버가 소유한다.
 *
 * 범위는 랭크 티어와 토너먼트 **정점** 둘뿐이다. 앨범 완성과 챕터 완주는 판정 근거
 * (도감 완성 조건 · 챕터↔정점 대응)가 스펙 표에 없어 서버가 잴 수 없다.
 * 재화는 지갑 문서가 아니라 세이브의 currency 슬롯에 쓴다.
 */
export const claimReward = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const ownerType = String(request.data?.ownerType ?? "") as ClaimOwnerType;
  const ownerId = String(request.data?.ownerId ?? "").trim();

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }
  if (ownerType !== "Rank" && ownerType !== "Tournament") {
    throw new HttpsError("invalid-argument", `ownerType must be Rank or Tournament, got '${ownerType}'.`);
  }
  if (ownerId.length === 0 || ownerId.length > MAX_OWNER_ID_LENGTH) {
    throw new HttpsError("invalid-argument", "ownerId must be a non-empty string.");
  }

  const context: ClaimContext = {uid, env, ownerType, ownerId};

  // 챕터 완주는 챕터↔정점 대응이 TournamentConfig SO 에만 있어 서버가 완주를 못 잰다.
  // 자격 근거가 없는 요청은 지급 경로에 들이지 않는다.
  if (ownerType === "Tournament" && ownerId.startsWith("chapter_")) {
    reject("RewardNotFound", "Chapter completion rewards are not claimable on the server.", {...context});
  }

  // 스펙 읽기는 트랜잭션 밖이다 — 유저 문서와 무관하고, 재실행마다 다시 읽으면 비용만 는다.
  let tierIndex = -1;
  let tierCount = 0;
  let requiredPoints = 0;
  if (ownerType === "Rank") {
    const grades = await loadRankGrades(context);
    tierCount = rankTierCount(grades);
    tierIndex = Number(ownerId);
    const required = requiredPointsForTier(tierIndex, grades);
    if (required === null) {
      reject("RewardNotFound", `Rank tier '${ownerId}' is out of range.`, {...context, tierCount});
    }
    requiredPoints = required;
  }

  // 랭크는 티어 인덱스를 정규 표기로 되돌려 조회한다 — 클라 RankConfig.FillRewards 가 쓰는 키와 같아야 한다.
  const specOwnerId = ownerType === "Rank" ? String(tierIndex) : ownerId;
  const rewardRows = parseRewardRows(await readSpecRows(env, "Reward"));
  const {gains, dropped} = resolveRewards(rewardRows, ownerType, specOwnerId);

  if (dropped.length > 0) {
    // 저작 실수를 조용히 삼키지 않는다 — 카드 보상이 저작되면 UnknownRewardType 으로 여기 뜬다.
    logger.warn("reward rows dropped", {...context, specOwnerId, dropped});
  }
  if (gains.length === 0) {
    // 토너먼트는 지급이 0건이어도 거절하지 않는다. "보상 미저작 정점은 해금만 넘긴다"가 저작 규약인데
    // 여기서 막으면 그 정점이 영영 RewardPending 으로 굳어 진행이 끊긴다 — 클리어 낙인은 남기고 지급만 비운다.
    // 랭크는 다르다: 미저작 티어를 수령해도 넘길 진행이 없으므로 거절이 맞다.
    if (ownerType === "Rank") {
      reject("RewardNotFound", `No reward is authored for Rank/${specOwnerId}.`,
        {...context, specOwnerId, droppedCount: dropped.length});
    }
    logger.warn("clearing a node with no authored reward", {...context, specOwnerId, droppedCount: dropped.length});
  }

  const result = await mutateSave(env, uid, (current): SlotPatch => {
    const currency = currencySlot(grant(readBalances(current.currency), gains));

    if (ownerType === "Rank") {
      return {currency, rank: claimRankTier(current, tierIndex, requiredPoints, tierCount, context)};
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
