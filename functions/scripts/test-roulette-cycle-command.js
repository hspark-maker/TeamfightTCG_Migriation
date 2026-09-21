"use strict";
// Actual callable, cycle, save, wallet and receipt code; only external I/O and pack content are fixtures.
const assert = require("node:assert/strict");
const path = require("node:path");
const lib = process.env.FUNCTIONS_TEST_LIB || path.resolve(__dirname, "../lib");
const moduleAt = (name) => path.join(lib, name);
const clone = (value) => value === undefined ? undefined : structuredClone(value);
const documents = new Map(), versions = new Map();
let commits = 0, retries = 0, beforeCommit = null, packMode = false, failGrant = false;
let reads = [];
function reference(key) {
  return {
    path: key,
    collection: (name) => collection(`${key}/${name}`),
    get parent() { return collection(key.slice(0, key.lastIndexOf("/"))); },
    get: async () => snapshot(key),
  };
}
function collection(key) {
  return {
    path: key, doc: (id) => reference(`${key}/${id}`),
    get parent() { return key.includes("/") ? reference(key.slice(0, key.lastIndexOf("/"))) : null; },
  };
}
function snapshot(key) {
  const value = clone(documents.get(key));
  return {exists: value !== undefined, data: () => clone(value)};
}
const db = {
  doc: reference, collection,
  async runTransaction(callback) {
    for (let attempt = 0; attempt < 30; attempt++) {
      const readVersions = new Map(), writes = [];
      const transaction = {
        async get(ref) {
          assert.equal(writes.length, 0, "Firestore reads must precede every write");
          reads.push(ref.path);
          readVersions.set(ref.path, versions.get(ref.path) || 0);
          return snapshot(ref.path);
        },
        async getAll(...refs) { return Promise.all(refs.map((ref) => this.get(ref))); },
        set(ref, value, options) { writes.push({ref, value, merge: options?.merge}); return this; },
        update(ref, value) { writes.push({ref, value, merge: true, update: true}); return this; },
        create(ref, value) { writes.push({ref, value, create: true}); return this; },
      };
      const result = await callback(transaction);
      if (beforeCommit) { const hook = beforeCommit; beforeCommit = null; await hook(writes); }
      if ([...readVersions].some(([key, version]) => version !== (versions.get(key) || 0))) {
        retries++;
        continue;
      }
      for (const write of writes) {
        if (write.create) assert.equal(documents.has(write.ref.path), false, "create requires absent document");
        if (write.update) assert.equal(documents.has(write.ref.path), true, "update requires existing document");
      }
      for (const {ref, value, merge} of writes) {
        documents.set(ref.path, clone(merge ? {...documents.get(ref.path), ...value} : value));
        versions.set(ref.path, (versions.get(ref.path) || 0) + 1);
      }
      if (writes.length) commits++;
      return result;
    }
    throw new Error("Too much contention in test transaction driver");
  },
};
function stub(filename, exports) {
  const resolved = require.resolve(filename);
  require.cache[resolved] = {id: resolved, filename: resolved, loaded: true, exports};
}
const quotas = [25, 20, 25, 15, 8, 4, 2, 1];
const header = (rouletteId) => ({id: 1, rouletteId, displayName: rouletteId,
  priceType: "RouletteTicket", price: 1, sortOrder: 0});
const slots = (rouletteId) => quotas.map((weight, slotIndex) => ({
  id: slotIndex + 1, rouletteId, slotIndex, weight,
  rewardType: packMode ? "Pack" : "Currency", rewardId: packMode ? "TestPack" : "Gold",
  amount: packMode ? 1 : slotIndex + 1,
}));
stub(moduleAt("firebaseApp"), {db});
stub(moduleAt("roulette/rouletteSpecReader"), {
  MAX_ROULETTE_ID_LENGTH: 64,
  readRouletteHeaderRow: async (_env, id) => header(id),
  readRouletteSlotRows: async (_env, id) => slots(id),
});
stub(moduleAt("specs/specBlobReader"), {readSpecRows: async () => []});
stub(moduleAt("missions/missionSpec"), {readMissionCatalog: async () => []});
stub(moduleAt("observability/analyticsEvent"), {recordEvent: () => {}});
stub("firebase-functions/logger", {info: () => {}, warn: () => {}, error: () => {}, debug: () => {}});
stub(moduleAt("rewards/itemGrant"), {
  loadItemGrantContext: async () => ({cards: []}),
  grantRewardItems: () => {
    if (failGrant) throw new Error("pack grant failed");
    const cards = [{cardId: 1, isNew: true, snack: 0}];
    return {slots: {ownership: {cardIds: [1]}}, cards, currencies: [], packs: [{packId: "TestPack", cards}]};
  },
});
const {spinRoulette} = require(moduleAt("commands/spinRoulette"));
const {rouletteCycleKey} = require(moduleAt("roulette/rouletteCycle"));
const root = (uid = "roulette-test", env = "test") => `envs/${env}/users/${uid}`;
const cyclePath = (id = "main", uid, env) => `${root(uid, env)}/rouletteCycles/${rouletteCycleKey(id)}`;
const walletPath = (uid, env) => `${root(uid, env)}/wallet/current`;
function seed(uid, env, tickets = 200) {
  documents.set(`${root(uid, env)}/save/current`, {schemaVersion: 8, revision: 1, tutorial: {}});
  documents.set(walletPath(uid, env), {rev: 1, balances: {Gold: 0, RouletteTicket: tickets}});
}
function spin(txId, {uid = "roulette-test", env = "test", rouletteId = "main"} = {}) {
  txId = `roulette-${txId}`;
  return spinRoulette.run({auth: {uid}, data: {env, rouletteId, txId}});
}
const remaining = (id, uid, env) => documents.get(cyclePath(id, uid, env)).remaining.reduce((a, b) => a + b, 0);
const state = () => clone([...documents]);
let passed = 0;
async function test(name, run) {
  documents.clear(); versions.clear(); reads = [];
  commits = 0; retries = 0; beforeCommit = null; packMode = false; failGrant = false;
  seed();
  await run();
  passed++;
  console.log("PASS " + name);
}
(async () => {
  await test("100 spins enforce exact quotas and 101 starts another cycle", async () => {
    const counts = Array(8).fill(0);
    for (let i = 0; i < 100; i++) counts[(await spin(`cycle-${i}`)).slotIndex]++;
    assert.deepEqual(counts, quotas);
    assert.equal(remaining(), 0);
    assert.equal(documents.get(walletPath()).balances.Gold, quotas.reduce((sum, n, i) => sum + n * (i + 1), 0));
    assert.equal(documents.get(walletPath()).balances.RouletteTicket, 100);
    await spin("next-cycle");
    assert.equal(remaining(), 99);
    assert.equal(commits, 101);
  });
  await test("receipt replay preserves response, cycle, wallet and save", async () => {
    const first = await spin("replay-one"), before = state();
    reads = [];
    assert.deepEqual(await spin("replay-one"), first);
    assert.deepEqual(state(), before);
    assert.equal(reads.includes(cyclePath()), false, "receipt must bypass draw callback");
    assert.equal(commits, 1);
  });
  await test("concurrent identical txIds consume one ticket and one quota", async () => {
    const results = await Promise.all([spin("same-tx"), spin("same-tx")]);
    assert.deepEqual(results[0], results[1]);
    assert.equal(remaining(), 99);
    assert.equal(documents.get(walletPath()).balances.RouletteTicket, 199);
    assert.equal(commits, 1);
    assert.ok(retries > 0);
  });
  await test("concurrent different txIds retry and each consume exactly once", async () => {
    const results = await Promise.all([spin("different-one"), spin("different-two")]);
    assert.equal(remaining(), 98);
    assert.equal(documents.get(walletPath()).balances.RouletteTicket, 198);
    assert.equal(documents.get(walletPath()).balances.Gold, results.reduce((sum, result) => sum + result.amount, 0));
    assert.equal(commits, 2);
    assert.ok(retries > 0);
  });
  await test("insufficient tickets preserve an existing cycle and create no receipt", async () => {
    await spin("before-empty");
    documents.get(walletPath()).balances.RouletteTicket = 0;
    const before = state();
    await assert.rejects(spin("no-ticket"), /InsufficientTicket:/);
    assert.deepEqual(state(), before);
  });
  await test("pack grant failure consumes neither ticket nor quota", async () => {
    packMode = true;
    await spin("pack-before-failure");
    const before = state();
    failGrant = true;
    await assert.rejects(spin("pack-failure"), /pack grant failed/);
    assert.deepEqual(state(), before);
  });
  await test("pack missions read before writes and commit atomically with cycle", async () => {
    packMode = true;
    const result = await spin("pack-missions");
    assert.equal(result.cards.length, 1);
    assert.ok(reads.includes(`${root()}/missions/current`));
    assert.ok(documents.has(`${root()}/missions/current`));
    assert.deepEqual(documents.get(`${root()}/save/current`).ownership, {cardIds: [1]});
    assert.equal(remaining(), 99);
    const before = state();
    beforeCommit = (writes) => {
      assert.ok(writes.some((write) => write.ref.path === cyclePath()));
      assert.ok(writes.some((write) => write.ref.path === `${root()}/missions/current`));
      throw new Error("commit failed after queued writes");
    };
    await assert.rejects(spin("commit-failure"), /commit failed/);
    assert.deepEqual(state(), before);
  });
  await test("player, environment and roulette identities have independent cycles", async () => {
    seed("another-user", "test");
    seed("roulette-test", "live");
    await spin("isolated-main");
    await spin("isolated-main-2");
    await spin("isolated-user", {uid: "another-user"});
    await spin("isolated-env", {env: "live"});
    await spin("isolated-board", {rouletteId: "other/board"});
    assert.equal(remaining(), 98);
    assert.equal(remaining("main", "another-user", "test"), 99);
    assert.equal(remaining("main", "roulette-test", "live"), 99);
    assert.equal(remaining("other/board"), 99);
    assert.equal([...documents.keys()].filter((key) => key.includes("/rouletteCycles/")).length, 4);
  });
  console.log(`Roulette cycle command: ${passed} focused regression scenarios passed.`);
})().catch((error) => { console.error(error); process.exitCode = 1; });
