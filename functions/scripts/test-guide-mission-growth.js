"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const {parseMissionCatalog} = require("../lib/missions/catalog");
const {evaluateGuideProgress, evaluateGuideRankProgress} = require("../lib/missions/guideProgress");
const {judgeMissionClaim} = require("../lib/missions/judgeMissionClaim");
const {readMissions, applyPeriodReset, commitMissionClaim} = require("../lib/missions/missionStore");
const {parseRewardRows, resolveRewards} = require("../lib/rewardTable");
const {BASE_LEVEL, applyEnhanceLevel} = require("../lib/growth/cardGrowth");

function readSheet(name) {
  const source = fs.readFileSync(path.join(__dirname, "../../docs/SpecData", name + "_sheet.csv"), "utf8")
    .replace(/^\uFEFF/, "");
  const rows = [];
  let row = [], cell = "", quoted = false;
  for (let i = 0; i < source.length; i++) {
    const char = source[i];
    if (char === '"') {
      if (quoted && source[i + 1] === '"') { cell += '"'; i++; }
      else quoted = !quoted;
    } else if (char === "," && !quoted) { row.push(cell); cell = ""; }
    else if ((char === "\n" || char === "\r") && !quoted) {
      if (char === "\r" && source[i + 1] === "\n") i++;
      row.push(cell);
      if (row.some(Boolean)) rows.push(row);
      row = []; cell = "";
    } else cell += char;
  }
  assert.equal(quoted, false, name + " has unclosed quotes");
  if (cell || row.length) { row.push(cell); rows.push(row); }
  const headers = rows[1], types = rows[2];
  return rows.slice(3).map((values) => Object.fromEntries(headers.map((key, index) =>
    [key, /^(int|long|float|double)$/.test(types[index]) ? Number(values[index]) : values[index]])));
}

const catalog = parseMissionCatalog(readSheet("Mission"));
const rewards = parseRewardRows(readSheet("Reward"));
const guide = catalog.filter((mission) => mission.enabled && mission.period === "guide")
  .sort((a, b) => a.sortOrder - b.sortOrder);
const cards = [1, 3, 4, 99].map((id) => ({id, name: "card_" + id, synergies: "Data_Synergy_Caretaker"}));
const key = (event) => "guide.Guide." + event;
const save = (ids = [], entries = {}) => ({ownership: {cardIds: ids}, cardGrowth: {entries}});
const entry = (star = 0, extra = {}) => ({level: BASE_LEVEL + star, snack: 0, limitBreak: 0, ...extra});
const evaluate = (current, previous = {}, rank) => evaluateGuideProgress(current, cards, catalog, previous, rank);
const period = {daily: "2026-09-14", weekly: "2026-09-14", dailyResetAtMs: 1, weeklyResetAtMs: 2};
const emptyState = () => ({dailyKey: period.daily, weeklyKey: period.weekly, progress: {}, claimed: {}, passExp: 0});
const tests = [];
const test = (name, run) => tests.push({name, run});

test("CSV: stable mission IDs, challenge before growth, events, targets and rewards", () => {
  assert.deepEqual(guide.map((m) => m.id).sort(), Array.from({length: 16}, (_, i) => "guide." + String(i + 1).padStart(2, "0"))
    .filter((id) => id !== "guide.04"));
  assert.deepEqual(guide.map((m) => m.sortOrder), Array.from({length: 15}, (_, i) => i + 1));
  assert.deepEqual(guide.slice(0, 7).map((m) => [m.event, m.target]), [
    ["Guide.EnhanceCompleted", 1], ["Guide.Bronze2Reached", 1], ["Guide.AdventureNode01", 1],
    ["Guide.AdventureNode02", 1], ["Guide.EvolveCompleted", 1],
    ["Guide.StarterCardsAtStar2", 3], ["Guide.CompleteSynergyBattle", 1],
  ]);
  assert.ok(guide.find((m) => m.id === "guide.11").sortOrder <
    guide.find((m) => m.id === "guide.10").sortOrder);
  for (const mission of guide) {
    assert.equal(mission.passExp, 0);
    const reward = resolveRewards(rewards, "Guide", mission.id);
    assert.deepEqual(reward.dropped, []);
    assert.ok(reward.gains.length + reward.items.length > 0, mission.id + " needs reward");
  }
  for (const id of ["guide.01", "guide.05", "guide.02"])
    assert.deepEqual(resolveRewards(rewards, "Guide", id).gains, [{currency: "Shard", amount: 10}]);
  assert.deepEqual(resolveRewards(rewards, "Guide", "guide.04").gains, []);
  assert.deepEqual(resolveRewards(rewards, "Guide", "guide.16").gains, [{currency: "Shard", amount: 40}]);
  assert.deepEqual(resolveRewards(rewards, "Guide", "guide.06").gains, [{currency: "Gold", amount: 50}]);
});

test("challenge clears can be claimed without completing the following growth mission", () => {
  for (const [challengeId, growthId, nodeId] of [
    ["guide.07", "guide.05", "node_02"], ["guide.11", "guide.10", "node_04"],
  ]) {
    const state = emptyState();
    const challenge = guide.find((mission) => mission.id === challengeId);
    for (const prior of guide.filter((mission) => mission.sortOrder < challenge.sortOrder))
      state.claimed[prior.id] = true;
    state.progress = evaluate({adventure: {clearedNodeIds: [nodeId]}});
    assert.equal(judgeMissionClaim(challengeId, state, catalog).allow, true);
    assert.equal(judgeMissionClaim(growthId, state, catalog).reason, "NotEligible");
    commitMissionClaim({set() {}}, {ref: {}, state, period}, challengeId, 0, 0);
    assert.equal(judgeMissionClaim(challengeId, state, catalog).reason, "AlreadyClaimed");
    assert.equal(judgeMissionClaim(growthId, state, catalog).reason, "NotEligible");
  }
});

test("rank: missing/bronze 1 fail; bronze 2/higher pass; maximum persists after downgrade", () => {
  for (const rank of [undefined, {}, {bestTierIndex: 0}, {points: 99999}, {bestTierIndex: "1"}])
    assert.equal(evaluate({}, {}, rank)[key("Bronze2Reached")], 0);
  for (const bestTierIndex of [1, 2, 20])
    assert.equal(evaluate({}, {}, {bestTierIndex})[key("Bronze2Reached")], 1);
  const reached = evaluateGuideRankProgress({bestTierIndex: 1});
  assert.equal(evaluate({}, reached, {bestTierIndex: 0})[key("Bronze2Reached")], 1);
  assert.equal(evaluate({}, reached)[key("Bronze2Reached")], 1);
});

test("starter cards: count 0..3 owned fixed IDs; no deck; greater stars count", () => {
  for (const target of [1, 2]) for (let count = 0; count <= 3; count++) {
    const entries = Object.fromEntries([3, 4, 1].map((id, i) => [id, entry(i < count ? target + 1 : target - 1)]));
    assert.equal(evaluate(save([3, 4, 1], entries))[key("StarterCardsAtStar" + target)], count);
  }
  const entries = {3: entry(3), 4: entry(3), 1: entry(3), 99: entry(3)};
  assert.equal(evaluate(save([3, 4, 99], entries))[key("StarterCardsAtStar2")], 2);
  assert.equal(evaluate(save([3, 3, 4, 1], entries))[key("StarterCardsAtStar2")], 3);
});

test("growth: partial shards and 1 star enhance only; 2+ stars count both", () => {
  const partial = evaluate(save([99], {99: entry(0, {shardProgress: 1})}));
  assert.equal(partial[key("EnhanceCompleted")], 1);
  assert.equal(partial[key("EvolveCompleted")], 0);
  const oneStar = evaluate(save([99], {99: entry(1, {shardProgress: 1})}));
  assert.equal(oneStar[key("EnhanceCompleted")], 1);
  assert.equal(oneStar[key("EvolveCompleted")], 0);
  for (const entries of [{99: {level: BASE_LEVEL + 2}}, applyEnhanceLevel({}, 99, BASE_LEVEL + 2),
    {99: entry(3)}]) {
    const progress = evaluate(save([99], entries));
    assert.equal(progress[key("EnhanceCompleted")], 1);
    assert.equal(progress[key("EvolveCompleted")], 1);
  }
});

test("guide 04 completion does not complete guide 05 until one owned card reaches 2 stars", () => {
  const current = save([1, 3, 4], {1: entry(1), 3: entry(1), 4: entry(1)});
  const before = evaluate(current);
  assert.equal(before[key("StarterCardsAtStar1")], 3);
  assert.equal(before[key("EvolveCompleted")], 0);
  current.cardGrowth.entries[1] = entry(2);
  const after = evaluate(current, before);
  assert.equal(after[key("EvolveCompleted")], 1);
  assert.equal(after[key("StarterCardsAtStar2")], 1);
});

test("growth: snack/limit break only and unowned growth do not count", () => {
  for (const current of [save([99], {99: entry(0, {snack: 1000, limitBreak: 5})}),
    save([], {99: entry(4, {shardProgress: 10})}), save([99]), {}]) {
    const progress = evaluate(current);
    assert.equal(progress[key("EnhanceCompleted")], 0);
    assert.equal(progress[key("EvolveCompleted")], 0);
  }
});

test("old counters never migrate to replacement conditions; previous maximum and claims survive resets", () => {
  const previous = {[key("DeckSaved6")]: 1, [key("CaretakerCardsAtStar1")]: 3,
    [key("CaretakerDeckAtStar2")]: 3, "daily.EnhanceCard": 10};
  const progress = evaluate({}, previous);
  for (const event of ["Bronze2Reached", "EnhanceCompleted", "EvolveCompleted", "StarterCardsAtStar1", "StarterCardsAtStar2"])
    assert.equal(progress[key(event)], 0);
  assert.equal(progress[key("CaretakerDeckAtStar2")], 3);
  assert.deepEqual(previous["daily.EnhanceCard"], 10);
  const state = readMissions({exists: true, data: () => ({...emptyState(), dailyKey: "old", weeklyKey: "old",
    progress: {...progress, [key("StarterCardsAtStar2")]: 3}, claimed: {"guide.02": true, "daily.old": true}})});
  const reset = applyPeriodReset(state, period);
  assert.deepEqual(reset.claimed, {"guide.02": true});
  assert.equal(evaluate({}, reset.progress)[key("StarterCardsAtStar2")], 3);
  assert.equal(judgeMissionClaim("guide.02", reset, catalog).reason, "AlreadyClaimed");
});

test("locked completion is retained; new rewards once; existing claimed missions do not repay", () => {
  const state = emptyState();
  state.progress = evaluate({...save([1, 3, 4], {1: entry(2), 3: entry(2), 4: entry(2)}),
    adventure: {clearedNodeIds: ["node_01", "node_02"]}}, {}, {bestTierIndex: 1});
  state.claimed["guide.02"] = true;
  assert.equal(judgeMissionClaim("guide.06", state, catalog).reason, "NotEligible");
  const paid = [];
  const transaction = {set: () => {}};
  const bump = {ref: {}, state, period};
  for (const mission of guide.slice(0, 6)) {
    const verdict = judgeMissionClaim(mission.id, state, catalog);
    if (mission.id === "guide.02") { assert.equal(verdict.reason, "AlreadyClaimed"); continue; }
    assert.equal(verdict.allow, true, mission.id);
    paid.push({id: mission.id, reward: resolveRewards(rewards, "Guide", mission.id)});
    commitMissionClaim(transaction, bump, mission.id, mission.passExp, 0);
    assert.equal(judgeMissionClaim(mission.id, state, catalog).reason, "AlreadyClaimed");
  }
  assert.equal(state.passExp, 0);
  assert.equal(paid.filter((p) => p.id === "guide.02").length, 0);
  for (const id of ["guide.01", "guide.05"])
    assert.equal(paid.filter((p) => p.id === id).length, 1);
});

test("callables: rank read before writes, live-state claims, rejection does not pay twice", async () => {
  const Module = require("node:module");
  const originalLoad = Module._load;
  const docs = new Map();
  const root = "envs/test/users/player/";
  const reads = [];
  let writes = [], wallet = {rev: 0, balances: {}, paidBalances: {}};
  const snapshot = (ref) => ({exists: docs.has(ref.path), data: () => docs.get(ref.path)});
  const transaction = {
    async get(ref) { assert.equal(writes.length, 0, "read after write"); reads.push(ref.path); return snapshot(ref); },
    async getAll(...refs) { return Promise.all(refs.map((ref) => this.get(ref))); },
    set(ref, value, options) { writes.push({ref, value: structuredClone(value), options}); },
  };
  const db = {
    doc: (documentPath) => ({path: documentPath}),
    async runTransaction(run) {
      reads.length = 0; writes = [];
      const result = await run(transaction);
      for (const {ref, value, options} of writes)
        docs.set(ref.path, options?.merge ? {...docs.get(ref.path), ...value} : value);
      return result;
    },
  };
  const saveDocument = {
    requireUid: () => "player",
    isKnownEnv: (env) => env === "test",
    saveDocument: () => db.doc(root + "save/current"),
    async mutateSave(env, uid, command, receipt, mutate, respond) {
      return db.runTransaction(async (tx) => {
        const mutation = await mutate(docs.get(root + "save/current"), tx, wallet);
        if (mutation.wallet) wallet = mutation.wallet.next;
        return respond({revision: wallet.rev, wallet});
      });
    },
  };
  class HttpsError extends Error {
    constructor(code, message, details) { super(message); this.code = code; this.details = details; }
  }
  const stubs = {
    "firebase-functions/v2/https": {onCall: (run) => run, HttpsError},
    "firebase-functions/logger": {info() {}, warn() {}, error() {}},
    "firebase-admin/firestore": {FieldValue: {serverTimestamp: () => 1}},
    "../firebaseApp": {db},
    "../save/saveDocument": saveDocument,
    "../observability/analyticsEvent": {recordEvent() {}},
    "../rewards/itemGrant": {loadItemGrantContext: () => { throw new Error("Unexpected item reward"); }},
    "../missions/missionSpec": {readMissionCatalog: async () => catalog},
    "../packs/packSpecReader": {readSpecRows: async (env, table) => {
      if (table === "Card") return cards;
      if (table === "Reward") return readSheet("Reward");
      if (table === "PassSeason") return [];
      throw new Error("Unexpected table " + table);
    }},
  };
  const commandFiles = ["../lib/commands/getMissions", "../lib/commands/claimMission"];
  Module._load = function(request, parent, isMain) {
    if (Object.hasOwn(stubs, request)) return stubs[request];
    return originalLoad.call(this, request, parent, isMain);
  };
  try {
    for (const file of commandFiles) delete require.cache[require.resolve(file)];
    const {getMissions} = require(commandFiles[0]);
    const {claimMission} = require(commandFiles[1]);
    docs.set(root + "save/current", save([3], {3: entry(0, {shardProgress: 1})}));
    docs.set(root + "rank/current", {bestTierIndex: 1, points: 0});
    const request = (missionId) => ({auth: {uid: "player"}, data: {env: "test", missionId}});
    const listed = await getMissions(request());
    assert.equal(listed.missions.progress[key("Bronze2Reached")], 1);
    assert.ok(reads.includes(root + "rank/current"));
    assert.equal(listed.definitions.filter((m) => m.period === "guide").length, 15);
    await claimMission(request("guide.01"));
    assert.ok(reads.includes(root + "rank/current"));
    assert.equal(wallet.balances.Shard, 10);
    await assert.rejects(claimMission(request("guide.01")), (error) => error.details?.reason === "AlreadyClaimed");
    assert.equal(writes.length, 0);
    assert.equal(wallet.balances.Shard, 10);
    docs.set(root + "rank/current", {bestTierIndex: 0, points: 0});
    await claimMission(request("guide.02"));
    assert.equal(wallet.balances.Shard, 20);
    assert.equal(docs.get(root + "missions/current").progress[key("Bronze2Reached")], 1);
    docs.set(root + "save/current", {...save([1, 3, 4], {1: entry(2), 3: entry(2), 4: entry(2)}),
      adventure: {clearedNodeIds: ["node_01"]}});
    await assert.rejects(claimMission(request("guide.05")), (error) => error.details?.reason === "NotEligible");
    assert.equal(writes.length, 0);
    await claimMission(request("guide.03"));
    await assert.rejects(claimMission(request("guide.04")), (error) => error.details?.reason === "MissionDisabled");
    // Existing player already received the node 2 item reward before the order changed.
    const missionDoc = docs.get(root + "missions/current");
    missionDoc.claimed["guide.07"] = true;
    await claimMission(request("guide.05"));
    assert.equal(wallet.balances.Shard, 40);
    await assert.rejects(claimMission(request("guide.05")), (error) => error.details?.reason === "AlreadyClaimed");
    assert.equal(wallet.balances.Shard, 40);
    const beforeBattleClaim = docs.get(root + "missions/current");
    beforeBattleClaim.claimed["guide.04"] = true;
    beforeBattleClaim.claimed["guide.06"] = true;
    beforeBattleClaim.claimed["guide.15"] = true;
    await assert.rejects(claimMission(request("guide.16")), (error) => error.details?.reason === "NotEligible");
    beforeBattleClaim.progress[key("CompleteSynergyBattle")] = 1;
    await claimMission(request("guide.16"));
    assert.equal(wallet.balances.Shard, 80);
    assert.equal(docs.get(root + "missions/current").claimed["guide.04"], true);
    assert.equal(docs.get(root + "missions/current").claimed["guide.15"], true);
    await assert.rejects(claimMission(request("guide.16")), (error) => error.details?.reason === "AlreadyClaimed");
    assert.equal(writes.length, 0);
    assert.equal(wallet.balances.Shard, 80);
  } finally {
    Module._load = originalLoad;
    for (const file of commandFiles) delete require.cache[require.resolve(file)];
  }
});

async function main() {
  for (const {name, run} of tests) {
    await run();
    console.log("PASS " + name);
  }
  console.log(`${tests.length} guide mission growth tests passed.`);
}

main().catch((error) => { console.error(error); process.exitCode = 1; });
