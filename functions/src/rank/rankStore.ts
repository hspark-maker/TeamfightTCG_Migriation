import {isDeepStrictEqual} from "node:util";
import {publicRankProfile, RankPublicProfile} from "./publicProfile";
import {
  DocumentReference,
  DocumentSnapshot,
  FieldValue,
  Firestore,
  Transaction,
} from "firebase-admin/firestore";
import {
  RankGradeRow,
  rankTierCount,
  requiredPointsForTier,
  resolveTierIndex,
} from "../payout";

export const RANK_SCHEMA_VERSION = 1;
export const RANK_RESET_TIERS = 4;

export interface RankState {
  seasonId: string;
  points: number;
  bestTierIndex: number;
  claimed: Record<string, boolean>;
}

export interface RankProgressResponse {
  seasonId: string;
  points: number;
  bestTierIndex: number;
  claimedTierIndexes: number[];
}

export function rankRef(db: Firestore, env: string, uid: string): DocumentReference {
  return db.doc(`envs/${env}/users/${uid}/rank/current`);
}

export function legacyRankPoints(payout: unknown, save: unknown): number {
  const payoutPoints = Number((payout as {currentPoints?: unknown} | undefined)?.currentPoints);
  if (Number.isSafeInteger(payoutPoints) && payoutPoints >= 0) return payoutPoints;
  const savePoints = Number(
    (save as {rank?: {points?: unknown}} | undefined)?.rank?.points,
  );
  return Number.isSafeInteger(savePoints) && savePoints >= 0 ? savePoints : 0;
}

export function legacyClaimedTiers(save: unknown, tierCount: number): number[] {
  const raw = (save as {rank?: {claimedTiers?: unknown}} | undefined)?.rank?.claimedTiers;
  if (!Array.isArray(raw)) return [];
  return normalizeTierIndexes(raw, tierCount);
}

function normalizeTierIndexes(value: unknown[], tierCount: number): number[] {
  const result = new Set<number>();
  for (const raw of value) {
    const tier = Number(raw);
    if (Number.isInteger(tier) && tier >= 0 && tier < tierCount) result.add(tier);
  }
  return [...result].sort((a, b) => a - b);
}

function claimedMap(value: unknown, tierCount: number): Record<string, boolean> {
  if (value === null || typeof value !== "object" || Array.isArray(value)) return {};
  const result: Record<string, boolean> = {};
  for (const [key, raw] of Object.entries(value as Record<string, unknown>)) {
    const tier = Number(key);
    if (raw === true && Number.isInteger(tier) && tier >= 0 && tier < tierCount) {
      result[String(tier)] = true;
    }
  }
  return result;
}

function reachedTierIndex(points: number, grades: RankGradeRow[]): number {
  if (grades.length === 0 || points < grades[0].entryPoints) return -1;
  return resolveTierIndex(points, grades);
}

export function readRank(
  snapshot: DocumentSnapshot,
  fallbackPoints: number,
  fallbackClaimed: number[],
  grades: RankGradeRow[],
): RankState {
  const data = snapshot.exists ? snapshot.data() : undefined;
  const tierCount = rankTierCount(grades);
  const rawPoints = Number(data?.points);
  const points = Number.isSafeInteger(rawPoints) && rawPoints >= 0 ? rawPoints : fallbackPoints;
  const fallbackMap = Object.fromEntries(
    normalizeTierIndexes(fallbackClaimed, tierCount).map((tier) => [String(tier), true]),
  );
  const claimed = snapshot.exists ? claimedMap(data?.claimed, tierCount) : fallbackMap;
  const reached = reachedTierIndex(points, grades);
  const claimedBest = Object.keys(claimed).reduce((best, key) => Math.max(best, Number(key)), -1);
  const rawBest = Number(data?.bestTierIndex);
  const storedBest = Number.isInteger(rawBest) && rawBest >= -1 && rawBest < tierCount ? rawBest : -1;
  return {
    seasonId: typeof data?.seasonId === "string" ? data.seasonId : "",
    points,
    bestTierIndex: Math.max(storedBest, reached, claimedBest),
    claimed,
  };
}

export function applyRankSeason(
  state: RankState, seasonId: string, grades: RankGradeRow[],
): RankState {
  if (state.seasonId === seasonId) return state;
  // First adoption migrates legacy progress into the active season without resetting it.
  if (state.seasonId.length === 0) return {...state, seasonId};

  if (grades.length === 0 || state.points < grades[0].entryPoints) {
    return {seasonId, points: 0, bestTierIndex: -1, claimed: {}};
  }
  const resetTier = Math.max(0, resolveTierIndex(state.points, grades) - RANK_RESET_TIERS);
  const resetPoints = requiredPointsForTier(resetTier, grades) ?? grades[0].entryPoints;
  return {seasonId, points: resetPoints, bestTierIndex: resetTier, claimed: {}};
}

/**
 * Adopts a completed tutorial's legacy Bronze entry before the first ranked settlement.
 * @param {RankState} state Current rank state.
 * @param {number} fallbackPoints Legacy points from save/payoutState.
 * @param {RankGradeRow[]} grades Rank grade spec rows.
 * @return {RankState} State with the legacy entry adopted, or the input untouched.
 */
export function adoptLegacyEntry(
  state: RankState, fallbackPoints: number, grades: RankGradeRow[],
): RankState {
  if (grades.length === 0 || state.points >= grades[0].entryPoints ||
      fallbackPoints < grades[0].entryPoints) return state;
  const points = fallbackPoints;
  return {...state, points, bestTierIndex: Math.max(state.bestTierIndex, resolveTierIndex(points, grades))};
}

export function writeRank(
  transaction: Transaction, ref: DocumentReference, state: RankState, now: unknown, profile: unknown,
): void {
  transaction.set(ref, {
    schemaVersion: RANK_SCHEMA_VERSION,
    seasonId: state.seasonId,
    points: state.points,
    bestTierIndex: state.bestTierIndex,
    claimed: state.claimed,
    updatedAt: now,
  });
  // 조회용 색인은 진실원과 같은 트랜잭션에 갱신한다. 환경별 컬렉션으로 격리한다.
  const user = ref.parent.parent!;
  transaction.set(user.parent.parent!.collection("rankings").doc(user.id), {
    seasonId: state.seasonId,
    points: state.points,
    profile: publicRankProfile(profile),
    updatedAt: now,
  });
}

export function rankProgressResponse(state: RankState): RankProgressResponse {
  return {
    seasonId: state.seasonId,
    points: state.points,
    bestTierIndex: state.bestTierIndex,
    claimedTierIndexes: Object.keys(state.claimed).map(Number).sort((a, b) => a - b),
  };
}

export async function ensureRankState(
  db: Firestore,
  env: string,
  uid: string,
  seasonId: string,
  grades: RankGradeRow[],
): Promise<RankState | null> {
  const snapshot = await ensureRankSnapshot(db, env, uid, seasonId, grades);
  return snapshot?.state ?? null;
}

// 이미 읽는 세이브에서 내 공개 프로필도 반환해 랭킹 조회의 추가 읽기를 없앤다.
export async function ensureRankSnapshot(
  db: Firestore, env: string, uid: string, seasonId: string, grades: RankGradeRow[],
): Promise<{state: RankState; profile: RankPublicProfile} | null> {
  return db.runTransaction(async (transaction) => {
    const currentRankRef = rankRef(db, env, uid);
    const payoutRef = db.doc(`envs/${env}/users/${uid}/payoutState/current`);
    const saveRef = db.doc(`envs/${env}/users/${uid}/save/current`);
    const boardRef = db.doc(`envs/${env}/rankings/${uid}`);
    const [rankSnapshot, payoutSnapshot, saveSnapshot, boardSnapshot] =
      await transaction.getAll(currentRankRef, payoutRef, saveRef, boardRef);
    if (!saveSnapshot.exists) return null;

    const fallbackPoints = legacyRankPoints(payoutSnapshot.data(), saveSnapshot.data());
    const fallbackClaimed = legacyClaimedTiers(saveSnapshot.data(), rankTierCount(grades));
    let state = applyRankSeason(
      readRank(rankSnapshot, fallbackPoints, fallbackClaimed, grades),
      seasonId,
      grades,
    );
    state = adoptLegacyEntry(state, fallbackPoints, grades);
    const profile = publicRankProfile(saveSnapshot.data()?.profile);

    // 원본과 두 사본이 모두 맞으면 timestamp만 갱신하는 3회 쓰기를 생략한다.
    // 정규화 전 저장값과 비교해야 손상된 값도 복구된다. 색인도 같은 트랜잭션에서
    // 읽어야 원본이 정상인 계정의 색인 누락·불일치 복구를 건너뛰지 않는다.
    const stored = rankSnapshot.data();
    const board = boardSnapshot.data();
    if (stored?.schemaVersion === RANK_SCHEMA_VERSION &&
        stored.seasonId === state.seasonId && stored.points === state.points &&
        stored.bestTierIndex === state.bestTierIndex && isDeepStrictEqual(stored.claimed, state.claimed) &&
        board?.seasonId === state.seasonId && board.points === state.points &&
        payoutSnapshot.data()?.currentPoints === state.points) {
      if (!isDeepStrictEqual(board.profile, profile)) {
        transaction.set(boardRef, {profile}, {mergeFields: ["profile"]});
      }
      return {state, profile};
    }

    const now = FieldValue.serverTimestamp();
    writeRank(transaction, currentRankRef, state, now, profile);
    // Keep rollback compatibility while payoutState remains deployed.
    transaction.set(payoutRef, {currentPoints: state.points, updatedAt: now}, {merge: true});
    return {state, profile};
  });
}
