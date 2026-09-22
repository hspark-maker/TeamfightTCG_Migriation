"use strict";
const assert = require("node:assert/strict");
const {test, before, after} = require("node:test");
const {randomUUID} = require("node:crypto");
const {resolve} = require("node:path");
const {readFileSync} = require("node:fs");
const {initializeTestEnvironment, assertFails, assertSucceeds} = require("@firebase/rules-unit-testing");
const {doc, setDoc, updateDoc, deleteField, serverTimestamp} = require("firebase/firestore");
if (!/^(127\.0\.0\.1|localhost):\d+$/.test(process.env.FIRESTORE_EMULATOR_HOST ?? "") ||
    !String(process.env.GCLOUD_PROJECT ?? "").startsWith("demo-")) throw new Error("Local emulator + demo project required");
const output = process.env.TITLE_TEST_BUILD || "../lib";
const built = name => require(resolve(__dirname, output, name));
const {db} = built("firebaseApp");
const {mutateSave} = built("save/saveDocument");
const {grantTitle} = built("titles/titleOwnership");
const {buildFreshAccountSlots} = built("save/freshAccount");
const context = {titles: [{id: 1, titleId: "first"}, {id: 2, titleId: "second"}],
  catalog: new Set(), grades: new Map(), thresholds: [], packs: new Map(), choices: [], cards: []};
let environment;
before(async () => {
  const [host, port] = process.env.FIRESTORE_EMULATOR_HOST.split(":");
  environment = await initializeTestEnvironment({projectId: process.env.GCLOUD_PROJECT, firestore: {
    host, port: Number(port), rules: readFileSync(resolve(__dirname, "../../firestore.rules"), "utf8"),
  }});
});
after(async () => {if (environment) await environment.cleanup(); await db.terminate();});
async function setup() {
  const uid = "titles-" + randomUUID();
  const root = db.doc("envs/test/users/" + uid);
  await root.collection("save").doc("current").set({schemaVersion: 8, revision: 1,
    profile: {nickname: "kept", accountExp: 9, equippedTitleId: ""}});
  await root.collection("wallet").doc("current").set({rev: 1, balances: {Gold: 100}, paidBalances: {}});
  return {uid, read: async () => (await root.collection("save").doc("current").get()).data()};
}
async function award(uid, titleId, txId, fail = false) {
  let granted;
  return mutateSave("test", uid, "titleTestGrant", {kind: "client", txId}, current => {
    const result = grantTitle(current.profile, titleId, context.titles);
    granted = {slots: {profile: result.profile}, titles: [result.title]};
    if (fail) throw new Error("simulated failure");
    return {slots: granted.slots};
  }, adopted => ({...adopted, titles: granted.titles}));
}
test("same receipt is replayed once; separate duplicate is harmless", async () => {
  const {uid, read} = await setup();
  const txId = randomUUID();
  const both = await Promise.all([award(uid, "first", txId), award(uid, "first", txId)]);
  assert.deepEqual(both[0].titles, both[1].titles);
  assert.equal((await read()).revision, 2);
  assert.deepEqual((await award(uid, "first", randomUUID())).titles, [{titleId: "first", isNew: false}]);
  assert.deepEqual((await read()).profile.ownedTitleIds, ["first"]);
});
test("concurrent grants retain both titles and failure writes nothing", async () => {
  const {uid, read} = await setup();
  await assert.rejects(award(uid, "first", randomUUID(), true));
  assert.equal((await read()).revision, 1);
  await Promise.all([award(uid, "first", randomUUID()), award(uid, "second", randomUUID())]);
  const saved = await read();
  assert.deepEqual(new Set(saved.profile.ownedTitleIds), new Set(["first", "second"]));
  assert.equal(saved.profile.nickname, "kept");
  assert.equal(saved.profile.accountExp, 9);
  assert.equal(saved.profile.equippedTitleId, "");
});
async function clientCase(profile, patch, success) {
  const uid = "title-rules-" + randomUUID();
  const path = "envs/test/users/" + uid + "/save/current";
  await environment.withSecurityRulesDisabled(ctx => setDoc(doc(ctx.firestore(), path), {
    ...buildFreshAccountSlots([], "test"), profile,
    schemaVersion: 8, revision: 1, updatedAt: serverTimestamp(),
    deviceId: "0123456789abcdef0123456789abcdef", appVersion: "test",
  }));
  const change = updateDoc(doc(environment.authenticatedContext(uid).firestore(), path), {
    ...patch, revision: 2, updatedAt: serverTimestamp(),
  });
  await (success ? assertSucceeds(change) : assertFails(change));
}
test("rules reject title ownership changes, fabrication and unowned equipment", async () => {
  const profile = {ownedTitleIds: ["first"], equippedTitleId: "first"};
  await clientCase(profile, {"profile.ownedTitleIds": ["first", "second"]}, false);
  await clientCase(profile, {"profile.ownedTitleIds": []}, false);
  await clientCase(profile, {"profile.ownedTitleIds": deleteField()}, false);
  await clientCase(profile, {"profile.equippedTitleId": "second"}, false);
  await clientCase(profile, {"profile.equippedTitleId": 2}, false);
  await clientCase(profile, {"profile.equippedTitleId": null}, false);
  await clientCase({}, {"profile.ownedTitleIds": ["first"], "profile.equippedTitleId": "first"}, false);
});
test("rules allow owned equip, unequip and legacy absent fields", async () => {
  await clientCase({ownedTitleIds: ["first"], equippedTitleId: ""}, {"profile.equippedTitleId": "first"}, true);
  await clientCase({ownedTitleIds: ["first"], equippedTitleId: "first"}, {"profile.equippedTitleId": ""}, true);
  await clientCase({}, {"profile.nickname": "renamed", "profile.ownedTitleIds": [], "profile.equippedTitleId": ""}, true);
  await clientCase({equippedTitleId: null}, {"profile.nickname": "renamed"}, true);
  await clientCase({equippedTitleId: "legacy"}, {"profile.nickname": "renamed"}, true);
});
