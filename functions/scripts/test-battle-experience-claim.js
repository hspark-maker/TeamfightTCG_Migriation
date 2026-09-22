"use strict";
// Real callable, account calculation, save/wallet and receipt code with a conflict/retry driver.
const assert = require("node:assert/strict");
const clone = (value) => value === undefined ? undefined : structuredClone(value);
const documents = new Map(), versions = new Map();
let conflictHook = null, failCommit = false, cardReward = false;
function ref(path) {
  return {
    path,
    get parent() { return ref(path.slice(0, path.lastIndexOf("/"))); },
    collection: (name) => ({doc: (id) => ref(`${path}/${name}/${id}`)}),
  };
}
const db = {
  doc: ref, collection: (name) => ({doc: (id) => ref(`${name}/${id}`)}),
  async runTransaction(callback) {
    for (let attempt = 0; attempt < 20; attempt++) {
      const reads = new Map(), writes = [];
      const tx = {
        async get(reference) {
          assert.equal(writes.length, 0, "read after write");
          reads.set(reference.path, versions.get(reference.path) || 0);
          const value = clone(documents.get(reference.path));
          return {ref: reference, exists: value !== undefined, data: () => clone(value)};
        },
        async getAll(...refs) { return Promise.all(refs.map((r) => this.get(r))); },
        set(reference, value, options) { writes.push({reference, value, merge: options?.merge}); },
        update(reference, value) { writes.push({reference, value, merge: true}); },
        create(reference, value) { writes.push({reference, value, create: true}); },
      };
      const result = await callback(tx);
      if (conflictHook) { const hook = conflictHook; conflictHook = null; await hook(); }
      if ([...reads].some(([key, version]) => version !== (versions.get(key) || 0))) continue;
      if (failCommit) throw new Error("commit unavailable");
      for (const write of writes) {
        if (write.create) assert.equal(documents.has(write.reference.path), false, "duplicate create");
      }
      for (const {reference, value, merge} of writes) {
        documents.set(reference.path, clone(merge ? {...documents.get(reference.path), ...value} : value));
        versions.set(reference.path, (versions.get(reference.path) || 0) + 1);
      }
      return result;
    }
    throw new Error("contention");
  },
};
function stub(modulePath, exports) {
  const filename = require.resolve(modulePath);
  require.cache[filename] = {id: filename, filename, loaded: true, exports};
}
stub("../lib/firebaseApp", {db});
stub("../lib/specs/specBlobReader", {readSpecRows: async (_env, table) => {
  assert.ok(["AlbumEntry", "AlbumThemeInfo"].includes(table));
  return [];
}});
stub("../lib/missions/missionSpec", {readMissionCatalog: async () => []});
stub("../lib/rewards/itemGrant", {
  loadItemGrantContext: async () => ({cards: []}),
  grantRewardItems: () => ({slots: {ownership: {cardIds: [1]}}, currencies: [],
    cards: [{cardId: 1, isNew: false}]}),
});
stub("../lib/packs/packSpecReader", {readSpecRows: async (_env, table) => table === "AccountLevel" ? [
  {id: 1, requiredExp: 0, winExp: 100, loseExp: 50},
  {id: 2, requiredExp: 100, winExp: 100, loseExp: 50},
  {id: 3, requiredExp: 300, winExp: 100, loseExp: 50},
] : [{id: 1, ownerType: "AccountLevel", ownerId: "2", order: 1,
  rewardType: cardReward ? "Card" : "Currency", rewardId: cardReward ? "1" : "Gold", amount: cardReward ? 1 : 200},
{id: 2, ownerType: "AccountLevel", ownerId: "3", order: 1,
  rewardType: "Currency", rewardId: "Gold", amount: 300}]});
const {claimBattleExperience} = require("../lib/commands/claimBattleExperience");
const root = "envs/test/users/alice";
const savePath = root + "/save/current", walletPath = root + "/wallet/current";
const matchId = "a".repeat(32), matchPath = "envs/test/matches/" + matchId;
const markerPath = root + "/accountBattleClaims/" + matchId;
const request = (txId, extra = {}) => claimBattleExperience.run({auth: {uid: "alice"}, data: {
  env: "test", matchId, txId, ...extra,
}});
function reset() {
  documents.clear(); versions.clear(); conflictHook = null; failCommit = false; cardReward = false;
  documents.set(savePath, {schemaVersion: 8, revision: 0, profile: {accountExp: 0, nickname: "kept"}});
  documents.set(walletPath, {rev: 1, balances: {Gold: 0}});
  documents.set(matchPath, {seedSource: "server", mode: "pvp", expectedParticipants: 2, status: "confirmed",
    participantUids: ["alice", "bob"], payouts: {alice: {won: true}}});
}
async function test(name, run) { reset(); await run(); console.log("PASS " + name); }
(async () => {
  await test("claim atomically credits XP, reached level bonus, profile and marker", async () => {
    const result = await request("battle-test-first", {won: false, accountExp: 999999});
    assert.deepEqual(result.accountExperience, {grantedExp: 100, previousLevel: 1, level: 2});
    assert.equal(documents.get(savePath).profile.accountExp, 100);
    assert.equal(documents.get(savePath).profile.nickname, "kept");
    assert.equal(documents.get(walletPath).balances.Gold, 200);
    assert.equal(documents.has(markerPath), true);
  });
  await test("same receipt and new receipt after receipt deletion never double grant", async () => {
    await request("battle-test-repeat");
    await request("battle-test-repeat");
    for (const key of documents.keys()) if (key.includes("/receipts/")) documents.delete(key);
    const result = await request("battle-test-new-receipt");
    assert.equal(result.alreadyClaimed, true);
    assert.equal(result.accountExperience, undefined);
    assert.equal(documents.get(savePath).profile.accountExp, 100);
    assert.equal(documents.get(walletPath).balances.Gold, 200);
  });
  await test("competing claims retry against permanent marker", async () => {
    conflictHook = () => request("battle-test-competing");
    const result = await request("battle-test-original");
    assert.equal(result.alreadyClaimed, true);
    assert.equal(documents.get(savePath).profile.accountExp, 100);
    assert.equal(documents.get(walletPath).balances.Gold, 200);
  });
  await test("commit failure leaves neither progression nor marker", async () => {
    failCommit = true;
    await assert.rejects(request("battle-test-failure"), /commit unavailable/);
    assert.equal(documents.get(savePath).profile.accountExp, 0);
    assert.equal(documents.get(walletPath).balances.Gold, 0);
    assert.equal(documents.has(markerPath), false);
  });
  await test("unowned and unconfirmed matches never write rewards", async () => {
    documents.get(matchPath).participantUids = ["bob"];
    await assert.rejects(request("battle-test-unowned"), /MatchNotOwned/);
    documents.get(matchPath).participantUids = ["alice"];
    documents.get(matchPath).status = "pending";
    await assert.rejects(request("battle-test-pending"), /MatchNotConfirmed/);
    assert.equal(documents.get(savePath).profile.accountExp, 0);
    assert.equal(documents.has(markerPath), false);
  });
  await test("card level bonus preserves ownership without retired growth mission progress", async () => {
    cardReward = true;
    documents.get(savePath).profile.contentUnlocks = {unlocked: ["Mission"]};
    const result = await request("battle-test-card-bonus");
    assert.equal(result.missions.progress["daily.LimitBreakCard"], undefined);
    assert.equal(documents.get(root + "/missions/current").progress["daily.LimitBreakCard"], undefined);
    assert.deepEqual(documents.get(savePath).ownership.cardIds, [1]);
    assert.equal(documents.has(markerPath), true);
  });
})().catch((error) => { console.error(error); process.exitCode = 1; });
