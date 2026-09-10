const assert = require("node:assert/strict");
const Module = require("node:module");
const {createHash} = require("node:crypto");
const {Timestamp} = require("firebase-admin/firestore");
const {missionPeriod} = require("../lib/missions/period");

// Exercise the real command control flow and mission hydration. External specs,
// replay, payout arithmetic and mutateSave have their own focused test suites.
const documents = new Map();
let reads = [];
let writes = [];
let replayPack = false;
let resolvedGrade;
let replayEnabled = false;
let replayVerdict;
let replayCalls = 0;
let transactionActive = false;
let abortNextCommit = false;
let retryNextCommit = false;
let attemptedWrites = [];
let commits = [];
const originalNow = Date.now;
Date.now = () => Date.UTC(2026, 8, 10, 12);
const period = missionPeriod(Date.now());
const userPath = (uid, slot) => `envs/test/users/${uid}/${slot}/current`;
const db = {doc: path => {
  assert.ok(!path.includes("/telemetry/replayDaily/"), "callable must not access the daily counter");
  return {path};
}};
function snapshot(ref) {
  const value = documents.get(ref.path);
  return {exists: value !== undefined, data: () => value};
}
async function transaction(run) {
  const retry = retryNextCommit;
  retryNextCommit = false;
  if (retry) await transactionAttempt(run, false);
  return transactionAttempt(run, true);
}
async function transactionAttempt(run, commit) {
  const pending = [];
  const read = refs => {
    assert.equal(pending.length, 0, "all reads must precede writes in each transaction");
    reads.push(refs.map(ref => ref.path));
    return refs.map(snapshot);
  };
  let value;
  transactionActive = true;
  try {
    value = await run({
      get: async ref => read([ref])[0],
      getAll: async (...refs) => read(refs),
      set: (ref, data, options) => pending.push({path: ref.path, data, options}),
      create: (ref, data) => {
        assert.equal(documents.has(ref.path), false, "event create requires a new document");
        pending.push({path: ref.path, data, create: true});
      },
    });
  } finally {
    transactionActive = false;
  }
  attemptedWrites.push(pending);
  if (!commit) return value;
  if (abortNextCommit) {
    abortNextCommit = false;
    throw Error("injected commit failure");
  }
  for (const write of pending) {
    documents.set(write.path, write.options?.merge ?
      {...documents.get(write.path), ...write.data} : write.data);
  }
  writes.push(...pending);
  commits.push(pending);
  return value;
}
const rankStore = {
  rankRef: (_, env, uid) => db.doc(`envs/${env}/users/${uid}/rank/current`),
  legacyRankPoints: (payout, save) => payout?.currentPoints ?? save?.rank?.points ?? 0,
  legacyClaimedTiers: save => save?.rank?.claimedTiers ?? [],
  readRank: (snap, fallback, claimed) => ({points: snap.data()?.points ?? fallback,
    bestTierIndex: 0, claimedTierIndices: claimed}),
  applyRankSeason: state => state,
  adoptLegacyEntry: state => state,
  rankProgressResponse: state => ({...state}),
  writeRank: (tx, ref, state, now, profile) => tx.set(ref, {...state, profile}),
};
const logger = {info() {}, warn() {}, error() {}};
class HttpsError extends Error {
  constructor(code, message) {super(message); this.code = code;}
}
const packMocks = {
  "../save/saveDocument": {
    isKnownEnv: env => env === "test", requireUid: auth => auth.uid,
    mutateSave: async (env, uid, source, receipt, mutate, finalize) => {
      if (replayPack) return {revision: 10, replay: true};
      return transaction(async tx => {
        const outcome = await mutate({ownership: {cardIds: []}, rank: {points: 1}}, tx,
          {rev: 1, balances: {Gold: 100}});
        return finalize({revision: 11, updatedSlots: outcome.slots});
      });
    },
  },
  "../packs/cardCatalog": {loadCatalogIds: async () => new Set([7])},
  "../missions/missionSpec": {readMissionCatalog: async () => []},
  "../packs/packSpecReader": {
    readCardPackRow: async () => ({price: 0, priceType: "Gold", drawCount: 1,
      uniqueDraw: false, minRankGrade: "", refundAmount: 0}),
    readDropRows: async () => [], readRankGradeRows: async () => [], readSpecRows: async (_, table) =>
      table === "CardEnhanceRule" ? [{maxLevel: 4, maxLimitBreak: 1}] :
        table === "CardLimitBreak" ? [{stage: 1, hpGain: 10, snackCost: 100}] : [],
  },
  "../packs/rankGrade": {
    entryPointsFromRows: () => [0], gradeOf: (_, points) => points,
    parseRequiredGrade: () => null, isRanked: () => true,
  },
  "../packs/packDraw": {
    resolveDropPool: (_, grade) => {resolvedGrade = grade; return [{cardId: 7, weight: 1}];},
    drawPack: () => [{cardId: 7, isNew: true, snack: 0}],
  },
  "../currency/walletStore": {nextWallet: (state, balances) => ({next: {...state, balances}})},
};
const matchMocks = {
  "../observability/countedTransaction": {withCountedTransaction: (_, run) => transaction(run)},
  "../specs/specBlobReader": {readSpecRows: async () => []},
  "../rank/rankSeason": {currentRankSeason: () => ({seasonId: "S1"})},
  "../battleReplayConfig": {isBattleReplayEnabled: async () => replayEnabled},
  "../battleReplayService": {
    parseSpecPins: (_, pins) => pins ?? null,
    callBattleReplay: async () => {
      assert.equal(transactionActive, false, "external replay runs outside transactions");
      assert.ok(replayVerdict, "unexpected replay/network");
      replayCalls++;
      return replayVerdict;
    },
  },
  "../deckValidation": {
    buildAiDeckSnapshots: () => [{id: 7}], computeDeckHash: () => "b".repeat(64),
  },
  "../payout": {
    parseRankGradeRows: rows => rows, rankTierCount: () => 3,
    computeCurrencyPayout: (won, remaining) => ({won, remaining}),
    computeRankPayout: (before, won) => ({before, after: before + (won ? 10 : -1), afterTierIndex: 1}),
    computeDrawRankPayout: before => ({before, after: before, afterTierIndex: 0}),
  },
};
const originalLoad = Module._load;
Module._load = function(request, parent, isMain) {
  if (parent?.filename.endsWith("battleReplayTelemetry.js") && request === "./firebaseApp") return {db};
  const command = parent?.filename.endsWith("openPack.js") ? packMocks :
    parent?.filename.endsWith("submitMatchResult.js") ? matchMocks : null;
  if (command) {
    if (request in command) return command[request];
    if (request === "firebase-functions/v2/https") return {
      HttpsError, onCall: (...args) => args.at(-1),
    };
    if (request === "firebase-functions/logger") return logger;
    if (request === "../firebaseApp") return {db};
    if (request === "../rank/rankStore") return rankStore;
    if (request === "../observability/analyticsEvent") return {recordEvent() {}};
  }
  return originalLoad.call(this, request, parent, isMain);
};
let openPack;
let submitMatchResult;
try {
  ({openPack} = require("../lib/commands/openPack"));
  ({submitMatchResult} = require("../lib/commands/submitMatchResult"));
} finally {
  Module._load = originalLoad;
}

function missionSeed(count, staleDaily = false, staleWeekly = false) {
  return {dailyKey: staleDaily ? "old-day" : period.daily,
    weeklyKey: staleWeekly ? "old-week" : period.weekly,
    progress: {"daily.CompleteBattle": count, "weekly.CompleteBattle": count + 20,
      "daily.OpenPack": count, "weekly.OpenPack": count + 20, "guide.keep": count},
    claimed: {"daily.keep": true, "weekly.keep": true, "guide.keep": true}, passExp: count + 100};
}
function reset() {reads = []; writes = []; attemptedWrites = []; commits = [];}
const events = entries => entries.filter(entry => entry.path.startsWith("envs/test/replayTelemetryEvents/"));
const settledDelta = {settled: 1, replayOk: 0, replayFailed: 0, unavailable: 0,
  divergent: 0, outcomeMismatch: 0, hashMismatch: 0};
function assertEvent(delta) {
  const emitted = events(writes);
  assert.equal(emitted.length, 1, "one telemetry event per completed settlement attempt");
  assert.equal(emitted[0].create, true);
  assert.equal(emitted[0].data.day, "2026-09-10");
  assert.ok(emitted[0].data.createdAt instanceof Timestamp);
  assert.deepEqual(emitted[0].data.delta, delta);
  assert.ok(commits.some(batch => batch.includes(emitted[0]) &&
    batch.some(write => write.path === matchPath)), "event and match share a commit");
}
const hash = "a".repeat(64);
const otherHash = "b".repeat(64);
const matchId = "c".repeat(32);
const matchPath = `envs/test/matches/${matchId}`;
function payload(won = true) {
  return {env: "test", matchId, seedSource: "server", myDeckHash: won ? hash : otherHash,
    opponentDeckHash: won ? otherHash : hash, contentFingerprint: hash,
    finalStateHash: "a".repeat(16), stateHashChain: "b".repeat(16),
    stateHashChainPrev: "c".repeat(16), stateHashChainLength: 1,
    won, myRemaining: won ? 3 : 0, opponentRemaining: won ? 0 : 3, rankPointsBefore: 999};
}
const request = (uid, data) => ({auth: {uid}, data});
function seedUser(uid, count) {
  documents.set(userPath(uid, "payoutState"), {sequence: count, currentPoints: count * 10});
  documents.set(userPath(uid, "save"), {profile: {nickname: uid}, rank: {claimedTiers: [count]}});
  documents.set(userPath(uid, "missions"), missionSeed(count, uid === "alice"));
}
function assertSettled(uid, before, sequence, daily, weekly) {
  const payout = documents.get(`envs/test/users/${uid}/payouts/${matchId}`);
  assert.equal(payout.rank.before, before, `${uid} uses its own rank/fallback`);
  assert.equal(payout.rankSequence, sequence, `${uid} uses its own payout sequence`);
  assert.equal(documents.get(userPath(uid, "rank")).profile.nickname, uid,
    `${uid} uses its own save profile`);
  const mission = documents.get(userPath(uid, "missions"));
  assert.equal(mission.progress["daily.CompleteBattle"], daily);
  assert.equal(mission.progress["weekly.CompleteBattle"], weekly);
  assert.equal(writes.filter(write => write.path === userPath(uid, "missions")).length, 1);
}

async function main() {
  documents.set(userPath("alice", "missions"), missionSeed(2, true, true));
  documents.set(userPath("alice", "rank"), {points: 77});
  const packRequest = request("alice", {env: "test", packId: "free", txId: "pack-test"});
  const pack = await openPack(packRequest);
  assert.deepEqual(reads, [[userPath("alice", "missions"), userPath("alice", "rank")]],
    "openPack reads both independent callback documents in one stage");
  assert.equal(resolvedGrade, 77, "rank snapshot wins over legacy save rank");
  assert.equal(pack.missions.progress["daily.OpenPack"], 1);
  assert.equal(pack.missions.progress["weekly.OpenPack"], 1);
  assert.deepEqual(pack.missions.claimed, {"guide.keep": true});
  assert.equal(pack.missions.passExp, 102);
  reset();
  replayPack = true;
  assert.equal((await openPack(packRequest)).replay, true);
  assert.deepEqual(reads, [], "receipt replay does not prefetch callback documents");
  assert.deepEqual(writes, []);

  documents.clear();
  seedUser("alice", 2);
  seedUser("bob", 8);
  documents.set(userPath("alice", "rank"), {points: 77});
  documents.set(matchPath, {seedSource: "server", status: "pending",
    participantUids: ["alice", "bob"], createdAt: Timestamp.now()});
  reset();
  // Bob submits first: entries ordering deliberately differs from participantUids.
  assert.equal((await submitMatchResult(request("bob", payload(false)))).status, "pending");
  assert.deepEqual(reads, [[matchPath]], "pending match does not fetch participant documents");
  assert.deepEqual(events(writes), [], "ordinary pending submission creates no telemetry event");
  reset();
  assert.equal((await submitMatchResult(request("alice", payload()))).status, "confirmed");
  assert.deepEqual(reads, [[matchPath],
    ["rank", "payoutState", "save", "missions"].flatMap(slot =>
      ["bob", "alice"].map(uid => userPath(uid, slot)))], "PvP participant reads use one batch");
  assertSettled("alice", 77, 3, 1, 23);
  assertSettled("bob", 80, 9, 9, 29);
  assert.deepEqual(documents.get(userPath("alice", "missions")).claimed,
    {"weekly.keep": true, "guide.keep": true}, "daily reset keeps weekly and guide claims");
  assert.equal(documents.get(userPath("bob", "missions")).passExp, 108);
  assertEvent(settledDelta);
  reset();
  assert.equal((await submitMatchResult(request("alice", payload()))).status, "confirmed");
  assert.deepEqual(reads, [[matchPath]], "confirmed replay bypasses participant batch");
  assert.deepEqual(writes, []);

  documents.clear();
  seedUser("alice", 2);
  const soloData = {...payload(), commandLogVersion: 1, commandLog: "", commandCount: 0,
    commandLogHash: createHash("sha256").update("").digest("hex"), boardOrder: [[7], [7]]};
  const soloMatch = {seedSource: "server", status: "pending", participantUids: ["alice"],
    mode: "solo", resultProtocol: 1, expectedParticipants: 1, phase: "locked", lockStatus: "approved",
    approvals: {alice: {ownerIndex: 0, cardSnapshots: [{id: 7}], deckHash: hash}},
    aiDeck: {cardIds: [7], cardLevel: 1}, cardDataVersion: hash,
    serverBoardOrders: {owner0: [7], owner1: [7]}, createdAt: Timestamp.now()};
  documents.set(matchPath, soloMatch);
  reset();
  assert.equal((await submitMatchResult(request("alice", soloData))).status, "confirmed");
  assert.deepEqual(reads, [[matchPath],
    ["rank", "payoutState", "save", "missions"].map(slot => userPath("alice", slot))]);
  assertSettled("alice", 20, 3, 1, 23);
  assertEvent(settledDelta);

  documents.clear();
  documents.set(matchPath, {...soloMatch, phase: "invalid"});
  reset();
  const flagged = await submitMatchResult(request("alice", soloData));
  assert.equal(flagged.status, "flagged");
  assert.equal(flagged.reason, "solo_match_contract_missing");
  assert.deepEqual(reads, [[matchPath]]);
  assertEvent(settledDelta);
  reset();
  assert.equal((await submitMatchResult(request("alice", soloData))).status, "flagged");
  assert.deepEqual(writes, [], "flagged replay creates no duplicate event");

  documents.clear();
  seedUser("alice", 2);
  documents.set(matchPath, soloMatch);
  const beforeAbort = new Map(documents);
  reset();
  abortNextCommit = true;
  await assert.rejects(submitMatchResult(request("alice", soloData)), /injected commit failure/);
  assert.equal(events(attemptedWrites.flat()).length, 1, "failure occurs after event was staged");
  assert.deepEqual(documents, beforeAbort, "failed commit preserves neither payout nor event nor match changes");
  assert.deepEqual(writes, []);
  reset();
  retryNextCommit = true;
  assert.equal((await submitMatchResult(request("alice", soloData))).status, "confirmed");
  const retriedEvents = events(attemptedWrites.flat());
  assert.equal(retriedEvents.length, 2);
  assert.equal(retriedEvents[0].path, retriedEvents[1].path, "transaction retries retain the callable event ID");
  assertEvent(settledDelta);
  assert.equal(documents.get(userPath("alice", "payoutState")).sequence, 3,
    "aborted and retried callbacks do not duplicate payouts");

  documents.clear();
  documents.set(matchPath, {...soloMatch, rulesetVersion: 2, seedHex: hash, specPins: {}});
  replayEnabled = true;
  replayVerdict = {kind: "unavailable", reason: "injected_replay_outage"};
  reset();
  assert.equal((await submitMatchResult(request("alice", soloData))).status, "pending");
  assert.equal(replayCalls, 1);
  assert.deepEqual(reads, [[matchPath], [matchPath]], "replay request and outage response only read the match");
  assert.deepEqual(commits[0], [], "need_replay commits no event before the external verdict");
  assertEvent({...settledDelta, settled: 0, unavailable: 1});
  assert.equal(documents.get(matchPath).replayUnavailable.reason, "injected_replay_outage");
  const firstOutageEvent = events(writes)[0].path;
  reset();
  assert.equal((await submitMatchResult(request("alice", soloData))).status, "pending");
  assertEvent({...settledDelta, settled: 0, unavailable: 1});
  assert.notEqual(events(writes)[0].path, firstOutageEvent,
    "separate unavailable submissions retain separate attempt counters");
  console.log("PASS batched commands: pack, PvP/solo snapshot mapping, period reset, pending/replay gates, atomic telemetry events, commit abort/retry, authoritative outage, reads before writes");
}

main().catch(error => {console.error(error); process.exitCode = 1;})
  .finally(() => {Date.now = originalNow;});
