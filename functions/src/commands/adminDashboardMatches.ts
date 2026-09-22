import {Timestamp} from "firebase-admin/firestore";
import {CallableRequest, HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {isKnownEnv} from "../save/environments";

const DAY_MS = 86400000;
const DETAIL_WINDOW_MS = 7 * DAY_MS;
const MAX_DAYS = 90;
const SAMPLE_LIMIT = 500;
// Query projection is also a privacy boundary: do not fetch submissions, player IDs, tokens or decks.
const MATCH_FIELDS = [
  "status", "mode", "adventureNodeId", "settledAt", "reason",
  "serverSimulation.ok", "serverSimulation.draw", "serverSimulation.winnerOwner",
  "serverSimulation.firstOwner", "serverSimulation.stats.turns",
];
type Mode = "ai" | "adventure" | "pvp" | "unknown";
type Status = "confirmed" | "flagged" | "other";
type Outcome = "win" | "loss" | "draw" | "pvp" | "unknown";

function dateMs(value: unknown): number {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    throw new HttpsError("invalid-argument", "Dates must use YYYY-MM-DD.");
  }
  const parsed = Date.parse(`${value}T00:00:00.000Z`);
  if (!Number.isFinite(parsed) || new Date(parsed).toISOString().slice(0, 10) !== value) {
    throw new HttpsError("invalid-argument", "A valid calendar date is required.");
  }
  return parsed;
}

function scope(request: CallableRequest, now: number) {
  if (!request.auth?.uid) throw new HttpsError("unauthenticated", "Sign-in is required.");
  if (request.auth.token?.admin !== true) throw new HttpsError("permission-denied", "Admin claim is required.");
  const env = request.data?.env;
  if (typeof env !== "string" || !isKnownEnv(env)) throw new HttpsError("invalid-argument", "Known env is required.");
  const todayMs = Math.floor(now / DAY_MS) * DAY_MS;
  const custom = request.data?.startDate !== undefined || request.data?.endDate !== undefined;
  let fromMs: number;
  let endMs: number;
  let days: number;
  if (custom) {
    if (request.data?.days !== undefined) throw new HttpsError("invalid-argument", "Use days or a custom range, not both.");
    fromMs = dateMs(request.data?.startDate);
    endMs = dateMs(request.data?.endDate);
    days = (endMs - fromMs) / DAY_MS + 1;
    if (endMs > todayMs) throw new HttpsError("invalid-argument", "Future dates are not supported.");
  } else {
    days = request.data?.days === undefined ? 7 : request.data.days;
    fromMs = todayMs - (days - 1) * DAY_MS;
    endMs = todayMs;
  }
  if (!Number.isInteger(days) || days < 1 || days > MAX_DAYS) {
    throw new HttpsError("invalid-argument", "Select between 1 and 90 calendar days.");
  }
  return {env, days, fromMs, toMs: Math.min(endMs + DAY_MS - 1, now),
    startDate: new Date(fromMs).toISOString().slice(0, 10), endDate: new Date(endMs).toISOString().slice(0, 10)};
}

function record(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : {};
}

function nonnegativeInteger(value: unknown): number | null {
  return typeof value === "number" && Number.isSafeInteger(value) && value >= 0 ? value : null;
}

function matchMode(data: Record<string, unknown>): Mode {
  if (typeof data.adventureNodeId === "string" && data.adventureNodeId.trim() !== "") return "adventure";
  if (data.mode === "solo") return "ai";
  if (data.mode === "pvp") return "pvp";
  return "unknown";
}

/** Read retained settled matches only. Daily counters are the independent, asynchronously aggregated total. */
export const adminDashboardMatches = onCall({enforceAppCheck: false}, async (request) => {
  const now = Date.now();
  const {env, days, fromMs, toMs, startDate, endDate} = scope(request, now);
  const detailStart = Math.max(fromMs, now - DETAIL_WINDOW_MS);
  const detailAvailable = detailStart <= toMs;
  const detailFromMs = detailAvailable ? detailStart : null;
  const detailToMs = detailAvailable ? toMs : null;
  const detailLimited = fromMs < now - DETAIL_WINDOW_MS;
  const dayIds = Array.from({length: days}, (_, offset) =>
    new Date(fromMs + offset * DAY_MS).toISOString().slice(0, 10));
  const matchesPromise = detailAvailable ? db.collection(`envs/${env}/matches`)
    .where("settledAt", ">=", Timestamp.fromMillis(detailStart))
    .where("settledAt", "<=", Timestamp.fromMillis(toMs))
    .orderBy("settledAt", "desc").limit(SAMPLE_LIMIT + 1).select(...MATCH_FIELDS).get() :
    Promise.resolve({docs: [], size: 0});
  const [matches, dailyDocs] = await Promise.all([
    matchesPromise,
    db.getAll(...dayIds.map((day) => db.doc(`envs/${env}/telemetry/replayDaily/days/${day}`)), {fieldMask: ["settled"]}),
  ]);
  const modes = (["ai", "adventure", "pvp", "unknown"] as const).map((mode) => ({
    mode, total: 0, confirmed: 0, flagged: 0, wins: 0, losses: 0, draws: 0, unknown: 0,
  }));
  const summary = {
    confirmed: 0, flagged: 0, other: 0, replayed: 0,
    averageTurns: null as number | null, turnSamples: 0,
    aiWins: 0, aiLosses: 0, aiDraws: 0, aiUnknown: 0,
  };
  const recent: {id: string; mode: Mode; status: Status; settledAtMs: number;
    reason: string | null; turns: number | null; outcome: Outcome}[] = [];
  // Running mean avoids overflowing an otherwise valid collection of safe integers.
  let averageTurns = 0;
  const sample = matches.docs.slice(0, SAMPLE_LIMIT);
  for (const snapshot of sample) {
    const data = snapshot.data();
    const mode = matchMode(data);
    const status: Status = data.status === "confirmed" || data.status === "flagged" ? data.status : "other";
    const simulation = record(data.serverSimulation);
    const replayed = simulation.ok === true;
    const confirmedReplay = status === "confirmed" && replayed;
    const validOutcome = confirmedReplay && typeof simulation.draw === "boolean" &&
      (simulation.draw || simulation.winnerOwner === 0 || simulation.winnerOwner === 1);
    const turns = confirmedReplay ? nonnegativeInteger(record(simulation.stats).turns) : null;
    const row = modes.find((entry) => entry.mode === mode)!;
    row.total++;
    summary[status]++;
    if (status !== "other") row[status]++;
    if (replayed) summary.replayed++;
    if (turns !== null) {
      summary.turnSamples++;
      averageTurns += (turns - averageTurns) / summary.turnSamples;
    }
    let outcome: Outcome = "unknown";
    if (validOutcome && (mode === "ai" || mode === "adventure")) {
      outcome = simulation.draw ? "draw" : simulation.winnerOwner === 0 ? "win" : "loss";
      if (outcome === "win") {
        row.wins++; summary.aiWins++;
      } else if (outcome === "loss") {
        row.losses++; summary.aiLosses++;
      } else {
        row.draws++; summary.aiDraws++;
      }
    } else if (validOutcome && mode === "pvp") {
      // Both owners are players. Never invent a player-relative win rate for this mode.
      outcome = "pvp";
    } else {
      row.unknown++;
      if (mode === "ai" || mode === "adventure") summary.aiUnknown++;
    }
    if (recent.length < 50) {
      recent.push({
        id: snapshot.id, mode, status, settledAtMs: (data.settledAt as Timestamp).toMillis(),
        reason: typeof data.reason === "string" ? data.reason.slice(0, 200) : null,
        turns, outcome,
      });
    }
  }
  summary.averageTurns = summary.turnSamples === 0 ? null : averageTurns;
  return {
    env, days, startDate, endDate, fromMs, toMs, fetchedAtMs: Date.now(), limit: SAMPLE_LIMIT,
    detailFromMs, detailToMs, detailLimited, detailAvailable,
    hasMore: matches.size > SAMPLE_LIMIT, sampleSize: sample.length,
    summary, modes,
    daily: dailyDocs.map((snapshot, index) => ({
      day: dayIds[index], exists: snapshot.exists, settled: nonnegativeInteger(snapshot.data()?.settled),
    })),
    recent,
  };
});
