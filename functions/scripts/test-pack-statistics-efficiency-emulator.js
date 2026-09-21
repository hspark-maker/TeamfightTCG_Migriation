"use strict";
const assert = require("node:assert/strict");
const {test, after} = require("node:test");
const {randomUUID} = require("node:crypto");
if (!/^(127\.0\.0\.1|localhost):\d+$/.test(process.env.FIRESTORE_EMULATOR_HOST ?? "") ||
    !String(process.env.GCLOUD_PROJECT ?? "").startsWith("demo-")) {
  throw new Error("A local Firestore emulator and demo-* project are required.");
}
const {db} = require("../lib/firebaseApp");
const {mutateSave} = require("../lib/save/saveDocument");
after(() => db.terminate());
const commands = ["claimAttendance", "claimMission", "claimReward", "claimPassReward", "claimBattleExperience", "spinRoulette"];

async function setup(t) {
  const uid = "pack-efficiency-" + randomUUID();
  const root = db.doc(`envs/test/users/${uid}`);
  await Promise.all([
    root.collection("save").doc("current").set({schemaVersion: 8, revision: 1}),
    root.collection("wallet").doc("current").set({rev: 1, balances: {Gold: 100}}),
  ]);
  const reads = [];
  const run = db.runTransaction;
  t.mock.method(db, "runTransaction", (callback, ...options) => run.call(db, (tx) => callback(new Proxy(tx, {
    get(target, key) {
      if (key === "get" || key === "getAll") return (...args) => {
        reads.push(...args.filter((ref) => typeof ref.path === "string").map((ref) => ref.path));
        return target[key](...args);
      };
      const value = target[key];
      return typeof value === "function" ? value.bind(target) : value;
    },
  })), ...options));
  const statsReads = () => reads.filter((path) => /\/(statistics|achievements)\/current$/.test(path));
  return {uid, root, reads, statsReads};
}

test("all six reward producers can grant currency/direct cards with zero statistics reads", async (t) => {
  const {uid, root, reads, statsReads} = await setup(t);
  for (const source of commands) {
    reads.length = 0;
    const result = await mutateSave("test", uid, source, {kind: "client", txId: randomUUID()},
      async (_current, _tx, _wallet, prepare) => { await prepare(0); return {slots: {ownership: {cardIds: [1]}}}; },
      (adopted) => ({...adopted, cards: [{cardId: 1}], packs: []}));
    assert.equal(result.statistics, undefined);
    assert.deepEqual(statsReads(), []);
    assert.equal(reads.length, 4, "only save, wallet, receipt and permanent operation are read");
  }
  assert.equal((await root.collection("statistics").doc("current").get()).exists, false);
});

test("real packs prepare two documents before producer writes and replay without counting again", async (t) => {
  const {uid, root, reads, statsReads} = await setup(t);
  const receipt = {kind: "client", txId: randomUUID()};
  const execute = () => mutateSave("test", uid, "claimReward", receipt,
    async (_current, tx, _wallet, prepare) => {
      await prepare(2);
      tx.set(root.collection("testMarkers").doc("reward"), {claimed: true});
      return {slots: {ownership: {cardIds: [1, 2]}}};
    }, (adopted) => ({...adopted, packs: [{packId: "a"}, {packId: "b"}]}));
  const result = await execute();
  assert.equal(statsReads().length, 2);
  assert.equal(result.statistics.lifetime.packsOpened, 2);
  reads.length = 0;
  assert.deepEqual(await execute(), result);
  assert.deepEqual(statsReads(), []);
  assert.equal((await root.collection("statistics").doc("current").get()).data().lifetime.packsOpened, 2);
});

test("missing or mismatched preparation aborts all producer writes instead of dropping statistics", async (t) => {
  const {uid, root} = await setup(t);
  for (const prepared of [0, 1]) {
    const marker = root.collection("testMarkers").doc(String(prepared));
    await assert.rejects(mutateSave("test", uid, "claimReward", {kind: "client", txId: randomUUID()},
      async (_current, tx, _wallet, prepare) => {
        if (prepared) await prepare(prepared);
        tx.set(marker, {claimed: true});
        return {slots: {}};
      }, (adopted) => ({...adopted, packs: [{packId: "a"}, {packId: "b"}]})), /not prepared before writes/);
    assert.equal((await marker.get()).exists, false);
  }
  assert.equal((await root.collection("save").doc("current").get()).data().revision, 1);
  assert.equal((await root.collection("statistics").doc("current").get()).exists, false);
});

test("finalization failure rolls back prepared statistics and producer writes", async (t) => {
  const {uid, root} = await setup(t);
  const marker = root.collection("testMarkers").doc("failed");
  await assert.rejects(mutateSave("test", uid, "openPack", {kind: "client", txId: randomUUID()},
    async (_current, tx, _wallet, prepare) => {
      await prepare(1);
      tx.set(marker, {claimed: true});
      return {slots: {}};
    }, () => { throw new Error("injected finalize failure"); }), /injected finalize failure/);
  assert.equal((await marker.get()).exists, false);
  assert.equal((await root.collection("statistics").doc("current").get()).exists, false);
});
