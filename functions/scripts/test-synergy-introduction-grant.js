"use strict";
const assert = require("node:assert/strict");
const {test} = require("node:test");
const {canEnhanceSynergyIntroduction: allowed} = require("../lib/growth/synergyIntroductionGrant");
const catalog = [
  {id: "guide.01", enabled: true, period: "guide", sortOrder: 1, event: "Guide.EnhanceCompleted", target: 1},
  {id: "guide.05", enabled: true, period: "guide", sortOrder: 2, event: "Guide.EvolveCompleted", target: 1},
];
const state = () => ({claimed: {"guide.01": true}, progress: {}, passExp: 0});
const entry = (level, shardProgress = 0) => ({level, shardProgress, snack: 7, limitBreak: 1});

test("only the active unfinished guide and an owned card without a two-star card qualify", () => {
  assert.equal(allowed(state(), catalog, [1], {}, 1), true);
  assert.equal(allowed({...state(), claimed: {}}, catalog, [1], {}, 1), false);
  assert.equal(allowed({...state(), claimed: {"guide.01": true, "guide.05": true}}, catalog, [1], {}, 1), false);
  assert.equal(allowed({...state(), progress: {"guide.Guide.EvolveCompleted": 1}}, catalog, [1], {}, 1), false);
  assert.equal(allowed(state(), catalog, [1], {}, 2), false);
  assert.equal(allowed(state(), catalog, [1, 2], {2: entry(3)}, 1), false);
  assert.equal(allowed(state(), catalog.filter((m) => m.id !== "guide.05"), [1], {}, 1), false);
});

test("persisted grant pins one card across reconnects and rejects consumed or malformed grants", () => {
  const grant = {cardId: 1, level: 2};
  assert.equal(allowed(state(), catalog, [1, 2], {1: entry(2)}, 1, grant), true);
  assert.equal(allowed(state(), catalog, [1, 2], {1: entry(2)}, 2, grant), false);
  for (const value of [null, {}, [], true, {cardId: 1, level: 3}, {cardId: 1, level: 1}]) {
    assert.equal(allowed(state(), catalog, [1], {1: entry(2)}, 1, value), false);
  }
  assert.equal(allowed(state(), catalog, [1], {}, 1, grant), false);
});

test("callable charges zero through two stars, preserves the old grant, and rejects without paid fallback", async () => {
  const Module = require("node:module");
  const originalLoad = Module._load;
  let save = {ownership: {cardIds: [1, 2]}, cardGrowth: {entries: {1: entry(1, 7)}}};
  let grants = {enhanceCard: true, enhanceKeyword: true, packs: {starter: true}};
  let wallet = {rev: 0, balances: {Shard: 0}, paidBalances: {}};
  let mission = state(), writes = [], sources = [], maxLevel = 4;
  let overrides = [];
  const shardIncrements = [];
  const transaction = {
    async get() { assert.equal(writes.length, 0); return {exists: true, data: () => grants}; },
    set(ref, value) { writes.push(value); },
  };
  class HttpsError extends Error {
    constructor(code, message, details) { super(message); this.details = details; }
  }
  const stubs = {
    "firebase-functions/v2/https": {onCall: (run) => run, HttpsError},
    "firebase-functions/logger": {info() {}, warn() {}, error() {}},
    "firebase-admin/firestore": {FieldValue: {serverTimestamp: () => 1}},
    "../firebaseApp": {db: {doc: (path) => ({path})}},
    "../observability/requestMetrics": {measuredCallable: (name, run) => run},
    "../observability/analyticsEvent": {recordEvent() {}},
    "../missions/missionSpec": {readMissionCatalog: async () => catalog},
    "../missions/missionStore": {
      beginMissionBump: async () => ({state: mission}),
      applyMissionIncrement(bump, event, amount) {
        assert.equal(event, "SpendShard");
        shardIncrements.push(amount);
      },
      commitMissionBump() {}, missionResponse: (value) => value,
    },
    "../missions/guideMutation": {readGuideCards: async () => [], applyGuideProgress() {}},
    "../packs/packSpecReader": {readSpecRows: async (env, table) => table === "CardEnhanceRule" ?
      [{maxLevel, maxLimitBreak: 3, baseEnhanceCost: 25, costGrowthPerLevel: 50}] : overrides},
    "../save/saveDocument": {
      requireUid: () => "player", isKnownEnv: () => true,
      async mutateSave(env, uid, command, receipt, mutate, respond) {
        writes = [];
        const result = await mutate(save, transaction, wallet);
        save = {...save, ...result.slots};
        wallet = result.wallet.next;
        for (const value of writes) grants = {...grants, ...value};
        sources.push(command);
        return respond({revision: wallet.rev, wallet});
      },
    },
  };
  Module._load = function(request, parent, isMain) {
    return Object.hasOwn(stubs, request) ? stubs[request] : originalLoad.call(this, request, parent, isMain);
  };
  const file = require.resolve("../lib/commands/enhanceCard");
  try {
    delete require.cache[file];
    const {enhanceSynergyIntroduction, enhanceCard} = require(file);
    const request = (cardId = 1) => ({auth: {uid: "player"}, data: {env: "test", cardId, freeShot: true}});
    const first = await enhanceSynergyIntroduction(request());
    assert.equal(first.cost, 0); assert.equal(first.freeShotUsed, true);
    assert.equal(first.level, 3); assert.equal(first.appliedShards, 93);
    assert.equal(first.evolved, true); assert.equal(first.shardProgress, 0);
    assert.deepEqual(shardIncrements, []);
    assert.deepEqual(grants.synergyIntroduction, {cardId: 1, level: 3});
    await assert.rejects(enhanceSynergyIntroduction(request(2)), (e) => e.details.reason === "NotReady");
    assert.equal(writes.length, 0);
    assert.equal(wallet.balances.Shard, 0);
    assert.equal(save.cardGrowth.entries[1].snack, 7);
    assert.equal(save.cardGrowth.entries[1].limitBreak, 1);
    assert.equal(grants.enhanceCard, true); assert.equal(grants.enhanceKeyword, true);
    assert.deepEqual(grants.packs, {starter: true});
    wallet.balances.Shard = 100;
    await assert.rejects(enhanceSynergyIntroduction(request()), (e) => e.details.reason === "NotReady");
    assert.equal(wallet.balances.Shard, 100); assert.equal(writes.length, 0);
    assert.deepEqual(sources, ["enhanceSynergyIntroduction"]);
    const paid = await enhanceCard(request());
    assert.equal(paid.cost, 1); assert.equal(paid.freeShotUsed, false);
    assert.deepEqual(shardIncrements, [1]);

    // Legacy sessions that already received their first evolution still finish in one request.
    save.cardGrowth.entries = {1: entry(2, 12), 2: entry(1, 4)};
    grants.synergyIntroduction = {cardId: 1, level: 2};
    const resumed = await enhanceSynergyIntroduction(request());
    assert.equal(resumed.level, 3); assert.equal(resumed.appliedShards, 63);
    assert.equal(resumed.cost, 0); assert.equal(wallet.balances.Shard, 99);
    assert.deepEqual(save.cardGrowth.entries[2], entry(1, 4));

    for (const [fromLevel, progress, expected] of [[1, 0, 100], [1, 24, 76], [2, 0, 75], [2, 74, 1]]) {
      save.cardGrowth.entries = {1: entry(fromLevel, progress)};
      delete grants.synergyIntroduction;
      const result = await enhanceSynergyIntroduction(request());
      assert.equal(result.level, 3); assert.equal(result.appliedShards, expected);
      assert.equal(result.cost, 0); assert.equal(result.evolved, true);
    }

    save.cardGrowth.entries = {1: entry(1, 2)};
    delete grants.synergyIntroduction;
    overrides = [{level: 2, cost: 40}, {level: 3, cost: 90}];
    const authored = await enhanceSynergyIntroduction(request());
    assert.equal(authored.appliedShards, 128);
    save.cardGrowth.entries = {1: entry(1, 2)};
    delete grants.synergyIntroduction;
    maxLevel = 2;
    const before = structuredClone({save, grants, wallet});
    await assert.rejects(enhanceSynergyIntroduction(request()), (e) => e.details.reason === "RuleUnavailable");
    assert.deepEqual({save, grants, wallet}, before);
    assert.deepEqual(shardIncrements, [1]); // Free grants and rejected requests consume no shards.

    maxLevel = 4;
    overrides = [];
    save.cardGrowth.entries = {1: entry(1, 24)};
    const capped = await enhanceCard({auth: {uid: "player"},
      data: {env: "test", cardId: 1, amount: 10, freeShot: false}});
    assert.equal(capped.cost, 1);
    assert.deepEqual(shardIncrements, [1, 1]); // Count actual consumption, not requested amount.
    save.cardGrowth.entries = {1: entry(1)};
    const batch = await enhanceCard({auth: {uid: "player"},
      data: {env: "test", cardId: 1, amount: 10, freeShot: false}});
    assert.equal(batch.cost, 10);
    assert.deepEqual(shardIncrements, [1, 1, 10]);
    wallet.balances.Shard = 0;
    await assert.rejects(enhanceCard(request()), (e) => e.details.reason === "NotAffordable");
    assert.deepEqual(shardIncrements, [1, 1, 10]);
  } finally {
    Module._load = originalLoad;
    delete require.cache[file];
  }
});
