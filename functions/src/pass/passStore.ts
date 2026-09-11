import type {
  DocumentReference,
  DocumentSnapshot,
  Firestore,
  Transaction,
} from "firebase-admin/firestore";

export const PASS_SCHEMA_VERSION = 1;
const PASS_EXP_MAX = 100000000;

export interface PassState {
  seasonId: string;
  exp: number;
  claimed: Record<string, boolean>;
  repeatClaimed: number;
  premiumUnlocked: boolean;
  premiumClaimed: Record<string, boolean>;
}

export interface PassProgressResponse {
  seasonId: string;
  exp: number;
  claimed: Record<string, boolean>;
  repeatClaimed: number;
  premiumUnlocked: boolean;
  premiumClaimed: Record<string, boolean>;
}

export interface PassMutation {
  ref: DocumentReference;
  state: PassState;
}

/**
 * Returns the dedicated seasonal pass document.
 * @param {Firestore} db Firestore instance.
 * @param {string} env Environment id.
 * @param {string} uid User id.
 * @return {DocumentReference} Pass document reference.
 */
export function passRef(db: Firestore, env: string, uid: string): DocumentReference {
  return db.doc(`envs/${env}/users/${uid}/pass/current`);
}

function claimedMap(value: unknown): Record<string, boolean> {
  if (value === null || typeof value !== "object" || Array.isArray(value)) return {};
  const result: Record<string, boolean> = {};
  for (const [key, raw] of Object.entries(value as Record<string, unknown>)) {
    if (/^[1-9][0-9]{0,2}$/.test(key) && raw === true) result[key] = true;
  }
  return result;
}

/**
 * Reads a pass document defensively.
 * @param {DocumentSnapshot} snapshot Pass document snapshot.
 * @return {PassState} Normalized state.
 */
export function readPass(snapshot: DocumentSnapshot): PassState {
  const data = snapshot.exists ? snapshot.data() : undefined;
  const rawExp = Number(data?.exp ?? 0);
  const rawRepeatClaimed = Number(data?.repeatClaimed ?? 0);
  return {
    seasonId: typeof data?.seasonId === "string" ? data.seasonId : "",
    exp: Number.isSafeInteger(rawExp) && rawExp > 0 ? Math.min(rawExp, PASS_EXP_MAX) : 0,
    claimed: claimedMap(data?.claimed),
    premiumUnlocked: data?.premiumUnlocked === true,
    premiumClaimed: claimedMap(data?.premiumClaimed),
    repeatClaimed: Number.isSafeInteger(rawRepeatClaimed) && rawRepeatClaimed > 0 ?
      Math.min(rawRepeatClaimed, PASS_EXP_MAX) : 0,
  };
}

/**
 * Applies a season change in memory; the next successful mutation persists it.
 * @param {PassState} state Stored state.
 * @param {string} seasonId Active season id.
 * @return {PassState} Active-season state.
 */
export function applyPassSeason(state: PassState, seasonId: string): PassState {
  if (state.seasonId === seasonId) return state;
  return {seasonId, exp: 0, claimed: {}, repeatClaimed: 0, premiumUnlocked: false, premiumClaimed: {}};
}

/**
 * Reads pass state inside an existing transaction. Call before any transaction write.
 * @param {Transaction} transaction Active transaction.
 * @param {Firestore} db Firestore instance.
 * @param {string} env Environment id.
 * @param {string} uid User id.
 * @param {string} seasonId Active season id.
 * @return {Promise<PassMutation>} Mutable transaction snapshot.
 */
export async function beginPassMutation(
  transaction: Transaction,
  db: Firestore,
  env: string,
  uid: string,
  seasonId: string,
): Promise<PassMutation> {
  const ref = passRef(db, env, uid);
  const state = applyPassSeason(readPass(await transaction.get(ref)), seasonId);
  return {ref, state};
}

function write(transaction: Transaction, pass: PassMutation, now: unknown): void {
  transaction.set(pass.ref, {
    schemaVersion: PASS_SCHEMA_VERSION,
    seasonId: pass.state.seasonId,
    exp: pass.state.exp,
    claimed: pass.state.claimed,
    repeatClaimed: pass.state.repeatClaimed,
    premiumUnlocked: pass.state.premiumUnlocked,
    premiumClaimed: pass.state.premiumClaimed,
    updatedAt: now,
  });
}

/**
 * Adds earned experience and persists the active season state.
 * @param {Transaction} transaction Active transaction.
 * @param {PassMutation} pass Mutable pass snapshot.
 * @param {number} amount Positive experience gain.
 * @param {unknown} now Server timestamp value.
 */
export function commitPassExp(
  transaction: Transaction, pass: PassMutation, amount: number, now: unknown,
): void {
  const gain = Number.isSafeInteger(amount) && amount > 0 ? amount : 0;
  pass.state.exp = Math.min(pass.state.exp + gain, PASS_EXP_MAX);
  write(transaction, pass, now);
}

/**
 * Marks one track's level claimed and persists the pass state.
 * @param {Transaction} transaction Active transaction.
 * @param {PassMutation} pass Mutable pass snapshot.
 * @param {number} level Claimed level.
 * @param {unknown} now Server timestamp value.
 * @param {string} track Validated reward track, default free for older callers.
 */
export function commitPassClaim(
  transaction: Transaction, pass: PassMutation, level: number, now: unknown, track: "free" | "premium" = "free",
): void {
  (track === "premium" ? pass.state.premiumClaimed : pass.state.claimed)[String(level)] = true;
  write(transaction, pass, now);
}

/**
 * Returns a detached response snapshot.
 * @param {PassState} state Current pass state.
 * @return {PassProgressResponse} Wire-safe snapshot.
 */
export function passProgressResponse(state: PassState): PassProgressResponse {
  return {seasonId: state.seasonId, exp: state.exp, claimed: {...state.claimed}, repeatClaimed: state.repeatClaimed,
    premiumUnlocked: state.premiumUnlocked, premiumClaimed: {...state.premiumClaimed}};
}

/**
 * Persists the cumulative repeat reward claim count in the wallet transaction.
 * @param {Transaction} transaction Active wallet transaction.
 * @param {PassMutation} pass Active season state.
 * @param {number} toClaimCount Validated cumulative claim count.
 * @param {unknown} now Server timestamp.
 */
export function commitPassRepeatClaim(
  transaction: Transaction, pass: PassMutation, toClaimCount: number, now: unknown,
): void {
  pass.state.repeatClaimed = toClaimCount;
  write(transaction, pass, now);
}
