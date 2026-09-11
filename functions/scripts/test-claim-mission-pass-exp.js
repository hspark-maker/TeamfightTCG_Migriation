// Real claim/get callables, reward judgement and mission/pass stores; in-memory I/O only.
const assert = require("node:assert/strict");
const Module = require("node:module");
const {HttpsError} = require("firebase-functions/v2/https");
const {missionPeriod} = require("../lib/missions/period");
const period = missionPeriod(Date.now());
const mission = {id: "daily.triggerKeyword2", period: "daily", event: "TriggerKeyword",
  target: 2, passExp: 15, enabled: true, sortOrder: 1, title: "Keyword", description: ""};
const otherReward = {id: 1, ownerType: "Mission", ownerId: "daily.other", order: 1,
  rewardType: "Currency", rewardId: "Gold", amount: 1};
let rewardRows, writes, lastMutation, passSeasonMode;
const documents = new Map();
const receipts = new Map();
const missionPath = "envs/test/users/alice/missions/current";
const passPath = "envs/test/users/alice/pass/current";
const snapshot = ref => ({exists: documents.has(ref.path), data: () => structuredClone(documents.get(ref.path))});
const db = {doc: path => ({path}), runTransaction: async action => {
  const pending = [];
  const read = ref => {
    assert.equal(pending.length, 0, "all reads precede writes");
    return snapshot(ref);
  };
  const result = await action({get: async ref => read(ref), getAll: async (...refs) => refs.map(read),
    set: (ref, value, options) => pending.push({ref, value, options})});
  for (const {ref, value, options} of pending) {
    documents.set(ref.path, options?.merge ? {...documents.get(ref.path), ...value} : value);
  }
  writes += pending.length;
  return result;
}};
const saveMock = {
  isKnownEnv: env => env === "test", requireUid: auth => auth.uid,
  saveDocument: () => db.doc("save"),
  mutateSave: async (env, uid, source, receipt, mutate, finalize) => {
    if (receipts.has(receipt.txId)) return receipts.get(receipt.txId);
    const result = await db.runTransaction(async transaction => {
      lastMutation = await mutate({}, transaction, {rev: 1, balances: {Gold: 100}});
      return finalize({revision: 1, updatedSlots: lastMutation.slots});
    });
    receipts.set(receipt.txId, result);
    return result;
  },
};
const originalLoad = Module._load;
Module._load = function(request, parent, isMain) {
  if (/[/\\](claimMission|getMissions)\.js$/.test(parent?.filename ?? "")) {
    if (request === "firebase-functions/v2/https") return {HttpsError, onCall: handler => handler};
    if (request === "firebase-functions/logger") return {info() {}, warn() {}, error() {}};
    if (request === "../firebaseApp") return {db};
    if (request === "../save/saveDocument") return saveMock;
    if (request === "../observability/analyticsEvent") return {recordEvent() {}};
    if (request === "../missions/missionSpec") return {readMissionCatalog: async () => [mission]};
    if (request === "../packs/packSpecReader") return {readSpecRows: async (env, table) => {
      if (table === "Reward") return rewardRows;
      if (table === "PassSeason") {
        if (passSeasonMode === "read-failure") throw new Error("Injected PassSeason read failure");
        if (passSeasonMode === "empty") return [];
        return [{id: 1, seasonId: "S1", displayName: "Season", startAtMs: 0,
          endAtMs: passSeasonMode === "inactive" ? 1 : 4102444800000, maxLevel: 10}];
      }
      return [];
    }};
  }
  return originalLoad.call(this, request, parent, isMain);
};
let claimMission, getMissions;
try {
  ({claimMission} = require("../lib/commands/claimMission"));
  ({getMissions} = require("../lib/commands/getMissions"));
} finally {
  Module._load = originalLoad;
}
const request = txId => ({auth: {uid: "alice"},
  data: {env: "test", missionId: mission.id, txId: `mission-pass-exp-${txId}`}});
function seed(rows = [otherReward], passExp = 15) {
  rewardRows = rows; mission.passExp = passExp; writes = 0; lastMutation = undefined;
  passSeasonMode = "active";
  documents.clear(); receipts.clear();
  documents.set(missionPath, {dailyKey: period.daily, weeklyKey: period.weekly,
    progress: {"daily.TriggerKeyword": 2}, claimed: {}, passExp: 40});
  documents.set(passPath, {seasonId: "S1", exp: 20, claimed: {1: true}});
}
async function main() {
  seed();
  const listing = await getMissions(request("list"));
  assert.deepEqual(listing.definitions[0].reward, {currencies: [], items: [], passExp: 15},
    "getMissions retains XP-only definitions without a Reward row");
  assert.equal(listing.missions.progress["daily.TriggerKeyword"], 2);
  const result = await claimMission(request("xp-only"));
  assert.equal(result.grantedPassExp, 15);
  assert.deepEqual(result.granted, []);
  assert.deepEqual(result.cards, []);
  assert.deepEqual(result.packs, []);
  assert.equal(result.missions.claimed[mission.id], true);
  assert.equal(result.missions.passExp, 55);
  assert.deepEqual(result.pass, {seasonId: "S1", exp: 35, claimed: {1: true}});
  assert.deepEqual(lastMutation, {slots: {}, wallet: undefined}, "XP-only claim never changes wallet or save slots");
  assert.equal(writes, 2, "mission claim and season XP each written once");
  assert.deepEqual(await claimMission(request("xp-only")), result);
  assert.equal(writes, 2, "receipt replay does not grant XP twice");
  await assert.rejects(claimMission(request("second-claim")), error => error.details?.reason === "AlreadyClaimed");
  assert.equal(writes, 2);

  for (const [rows, passExp, reason] of [
    [[], 15, "NotEligible"],
    [[otherReward], 0, "RewardNotFound"],
    [[otherReward, {...otherReward, id: 2, ownerId: mission.id, rewardId: "UnknownCurrency"}], 15, "RewardNotFound"],
    [[otherReward, {...otherReward, id: 2, ownerId: mission.id, amount: 0}], 15, "RewardNotFound"],
    [[otherReward, {...otherReward, id: 2, ownerId: mission.id, rewardType: "Invalid"}], 15, "RewardNotFound"],
  ]) {
    seed(rows, passExp);
    await assert.rejects(claimMission(request("invalid")), error => error.details?.reason === reason);
    assert.equal(writes, 0, "invalid/missing spec never commits a claim or XP");
    assert.equal(documents.get(passPath).exp, 20);
    assert.deepEqual(documents.get(missionPath).claimed, {});
  }
  for (const mode of ["inactive", "empty", "read-failure"]) {
    seed();
    passSeasonMode = mode;
    const before = structuredClone([...documents]);
    await assert.rejects(claimMission(request(mode)), error => {
      assert.equal(error.details?.reason, "NotEligible");
      assert.match(error.message, /active battle pass.*XP-only mission/);
      return true;
    });
    assert.equal(writes, 0, `${mode}: XP-only rejection must not write`);
    assert.deepEqual([...documents], before, `${mode}: no claim mark or XP change`);
    assert.equal(receipts.size, 0, `${mode}: rejected claim creates no receipt`);
    assert.equal(lastMutation, undefined);
    passSeasonMode = "active";
    const recovered = await claimMission(request(mode));
    assert.equal(recovered.pass.exp, 35, "same request can claim after pass availability recovers");
    assert.equal(recovered.missions.claimed[mission.id], true);

    seed([{...otherReward, ownerId: mission.id}]);
    passSeasonMode = mode;
    const ordinary = await claimMission(request(`currency-${mode}`));
    assert.deepEqual(ordinary.granted, [{currency: "Gold", amount: 1}],
      `${mode}: currency+XP missions retain currency fallback`);
    assert.equal(ordinary.missions.claimed[mission.id], true);
    assert.equal(ordinary.missions.passExp, 55);
    assert.equal(ordinary.pass, undefined);
    assert.equal(documents.get(passPath).exp, 20);
    assert.equal(writes, 1, "fallback only writes mission state");
  }
  console.log("claimMission XP-only and getMissions: ok (claim, replay, duplicate, 5 rejection cases, " +
    "3 unavailable-pass modes with recovery and currency fallback)");
}
main().catch(error => {console.error(error); process.exitCode = 1;});
