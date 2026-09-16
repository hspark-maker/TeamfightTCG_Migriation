"use strict";

// FIRESTORE_EMULATOR_HOST=127.0.0.1:8089 GCLOUD_PROJECT=demo-account-experience
// node --test scripts/test-account-experience-emulator.js
const assert = require("node:assert/strict");
const {test, after} = require("node:test");
const {randomUUID} = require("node:crypto");
if (!/^(127\.0\.0\.1|localhost):\d+$/.test(process.env.FIRESTORE_EMULATOR_HOST ?? "") ||
    !String(process.env.GCLOUD_PROJECT ?? "").startsWith("demo-")) {
  throw new Error("This test requires a local Firestore emulator and a demo-* project.");
}

const {db, DATABASE_ID} = require("../lib/firebaseApp");
const specs = require("../lib/specs/specBlobReader");
const catalog = require("../lib/missions/missionSpec");
const saves = require("../lib/save/saveDocument");
const {missionPeriod} = require("../lib/missions/period");
const {claimMission} = require("../lib/commands/claimMission");
after(() => db.terminate());

const levels = [0, 100, 300, 600].map((requiredExp, index) =>
  ({id: index + 1, requiredExp, winExp: 100, loseExp: 50}));
const rewards = [2, 3, 4].map((level) => ({id: level, ownerType: "AccountLevel", ownerId: String(level),
  order: 1, rewardType: "Currency", rewardId: "Gold", amount: level * 10}));
const missions = ["daily.first", "daily.second"].map((id, index) => ({id, enabled: true,
  period: "daily", event: "PlayBattle", target: 1, title: id, description: id,
  passExp: 0, accountExp: 120, sortOrder: index + 1, guideActId: 0, guideActName: ""}));

async function setup(t) {
  assert.equal(DATABASE_ID, "cardbattle");
  const uid = "account-xp-test-" + randomUUID();
  const root = db.doc(`envs/test/users/${uid}`);
  const period = missionPeriod(Date.now());
  t.mock.method(specs, "readSpecRows", async (_env, name) => {
    if (name === "Reward") return rewards;
    if (name === "AccountLevel") return levels;
    if (name === "PassSeason") return [];
    throw new Error("Unexpected spec: " + name);
  });
  t.mock.method(catalog, "readMissionCatalog", async () => missions);
  await Promise.all([
    root.collection("save").doc("current").set({schemaVersion: 8, revision: 1,
      profile: {accountExp: 90, nickname: "kept", contentUnlocks: {version: 1, unlocked: ["Mission"]}}}),
    root.collection("wallet").doc("current").set({rev: 1, balances: {Gold: 5}, paidBalances: {}}),
    root.collection("missions").doc("current").set({schemaVersion: 1,
      dailyKey: period.daily, weeklyKey: period.weekly, progress: {"daily.PlayBattle": 1}, claimed: {}, passExp: 0}),
  ]);
  const request = (missionId, txId = randomUUID()) => ({auth: {uid}, data: {env: "test", missionId, txId}});
  const read = async () => {
    const snapshots = await db.getAll(root.collection("save").doc("current"),
      root.collection("wallet").doc("current"), root.collection("missions").doc("current"));
    return snapshots.map((snapshot) => snapshot.data());
  };
  return {uid, root, request, read};
}

test("real transaction: concurrent identical receipts grant once and replay the same profile", async (t) => {
  const {request, read, root} = await setup(t);
  const input = request("daily.first");
  const [first, replay] = await Promise.all([claimMission.run(input), claimMission.run(input)]);
  assert.deepEqual(JSON.parse(JSON.stringify(replay)), JSON.parse(JSON.stringify(first)));
  const [save, wallet, mission] = await read();
  assert.equal(save.profile.accountExp, 210);
  assert.equal(save.profile.accountRewardLevel, 2);
  assert.equal(save.profile.nickname, "kept");
  assert.equal(save.revision, 2);
  assert.equal(wallet.balances.Gold, 25);
  assert.equal(mission.claimed["daily.first"], true);
  assert.equal(first.grantedAccountExp, 120);
  assert.equal((await root.collection("wallet").doc("current").collection("receipts").get()).size, 1);
});

test("real transaction: simultaneous distinct claims retain both XP gains and crossed-level rewards", async (t) => {
  const {request, read} = await setup(t);
  await Promise.all(missions.map((mission) => claimMission.run(request(mission.id))));
  const [save, wallet, mission] = await read();
  assert.equal(save.profile.accountExp, 330);
  assert.equal(save.profile.accountRewardLevel, 3);
  assert.equal(save.revision, 3);
  assert.equal(wallet.balances.Gold, 55);
  assert.deepEqual(mission.claimed, {"daily.first": true, "daily.second": true});
  await assert.rejects(() => claimMission.run(request("daily.first")),
    (error) => error.details?.reason === "AlreadyClaimed");
  assert.deepEqual(await read(), [save, wallet, mission]);
});

test("real transaction: failure after queued mission writes leaves XP, wallet and claim untouched", async (t) => {
  const {request, read} = await setup(t);
  const before = await read();
  const realMutate = saves.mutateSave;
  const fault = t.mock.method(saves, "mutateSave", (env, uid, source, receipt, mutate, finalize) =>
    realMutate(env, uid, source, receipt, async (...args) => {
      await mutate(...args);
      throw new Error("injected failure before commit");
    }, finalize));
  await assert.rejects(() => claimMission.run(request("daily.first")), /injected failure/);
  assert.deepEqual(await read(), before);
  fault.mock.restore();
  await claimMission.run(request("daily.first"));
  assert.equal((await read())[0].profile.accountExp, 210);
});

test("partial reward publication is retryable and does not consume XP or mission claims", async (t) => {
  const {request, read} = await setup(t);
  const before = await read();
  const readSpec = specs.readSpecRows;
  const incomplete = t.mock.method(specs, "readSpecRows", (env, name) =>
    name === "Reward" ? Promise.resolve(rewards.slice(1)) : readSpec(env, name));
  await assert.rejects(() => claimMission.run(request("daily.first")),
    (error) => error.code === "unavailable" && error.details?.reason === "AccountProgressionUnavailable");
  assert.deepEqual(await read(), before);
  incomplete.mock.restore();
  await claimMission.run(request("daily.first"));
  assert.equal((await read())[0].profile.accountExp, 210);
});
