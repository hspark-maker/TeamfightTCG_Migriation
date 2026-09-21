"use strict";

// Run with FIRESTORE_EMULATOR_HOST=127.0.0.1:8089. Never connects to production.
const {readFileSync} = require("node:fs");
const {resolve} = require("node:path");
const {test, before, after, beforeEach} = require("node:test");
const {initializeTestEnvironment, assertFails, assertSucceeds} = require("@firebase/rules-unit-testing");
const {doc, getDoc, setDoc, updateDoc, deleteDoc, deleteField, serverTimestamp} = require("firebase/firestore");
const {buildFreshAccountSlots} = require("../lib/save/freshAccount");

let env;
const path = "envs/test/users/player/save/current";
const original = (profile = {nickname: "test", accountExp: 350, accountRewardLevel: 2}) => ({
  ...buildFreshAccountSlots([], "test"), profile,
  schemaVersion: 8, revision: 1, updatedAt: serverTimestamp(),
  deviceId: "0123456789abcdef0123456789abcdef", appVersion: "test",
});
const db = (uid = "player") => env.authenticatedContext(uid).firestore();
const update = (patch, uid = "player") => updateDoc(doc(db(uid), path), {
  ...patch, revision: 2, updatedAt: serverTimestamp(),
});
const seed = (value) => env.withSecurityRulesDisabled((context) => setDoc(doc(context.firestore(), path), value));

before(async () => {
  const endpoint = process.env.FIRESTORE_EMULATOR_HOST;
  if (!endpoint || !/^(127\.0\.0\.1|localhost):\d+$/.test(endpoint)) {
    throw new Error("A local FIRESTORE_EMULATOR_HOST is required.");
  }
  const [host, port] = endpoint.split(":");
  env = await initializeTestEnvironment({projectId: "demo-account-experience", firestore: {
    host, port: Number(port), rules: readFileSync(resolve(__dirname, "../../firestore.rules"), "utf8"),
  }});
});
after(async () => { if (env) await env.cleanup(); });
beforeEach(async () => { await env.clearFirestore(); await seed(original()); });

test("profile cosmetics and deck saves preserve server XP", async () => {
  await assertSucceeds(update({"profile.nickname": "renamed"}));
  await seed(original());
  await assertSucceeds(update({deck: {slots: []}}));
});
test("retired keyword growth cannot be restored to a migrated save", async () => {
  await assertFails(update({keywordGrowth: {levels: {1: 10}}}));
  await assertSucceeds(update({"profile.nickname": "clean-growth-save"}));
});
test("server card growth stays immutable while migrated saves can change profile and deck", async () => {
  const migrated = original();
  migrated.cardGrowth = {entries: {1: {level: 3, shardProgress: 4}}};
  await seed(migrated);
  for (const patch of [
    {"cardGrowth.entries.1.snack": 20},
    {"cardGrowth.entries.1.limitBreak": 3},
    {"cardGrowth.entries.1.level": 4},
    {"cardGrowth.entries.1.shardProgress": 8},
    {cardGrowth: {entries: {}}},
    {cardGrowth: deleteField()},
  ]) await assertFails(update(patch));
  await assertSucceeds(update({"profile.nickname": "migrated"}));
  await seed(migrated);
  await assertSucceeds(update({deck: {slots: []}, cardGrowth: migrated.cardGrowth}));
});
test("XP increment, decrement, deletion and replacement are denied", async () => {
  for (const value of [351, 0, -1, 350.5, "350", null, deleteField()]) {
    await assertFails(update({"profile.accountExp": value}));
  }
  await assertFails(update({profile: {nickname: "lost server fields"}}));
});
test("reward watermark cannot be forged, rewound or removed", async () => {
  for (const value of [0, 1, 3, "2", null, deleteField()]) {
    await assertFails(update({"profile.accountRewardLevel": value}));
  }
});
test("legacy profile can serialize zero defaults while retaining legacy XP", async () => {
  await seed(original({nickname: "legacy"}));
  await assertSucceeds(update({profile: {nickname: "new", accountExp: 0, accountRewardLevel: 0}}));
  await seed(original({nickname: "legacy", accountExp: 900}));
  await assertSucceeds(update({profile: {nickname: "new", accountExp: 900, accountRewardLevel: 0}}));
  await seed(original({nickname: "legacy", accountExp: 900}));
  await assertFails(update({"profile.accountExp": 901}));
  await assertFails(update({"profile.accountRewardLevel": 3}));
});
test("fresh server account permits first client profile save", async () => {
  const fresh = original(buildFreshAccountSlots([], "test").profile);
  await seed(fresh);
  await assertSucceeds(update({profile: {...fresh.profile, emoteIds: [], contentUnlocks: {}, nickname: "new"}}));
});
test("other users and anonymous clients cannot update profile", async () => {
  await assertFails(update({"profile.nickname": "attack"}, "stranger"));
  await assertFails(updateDoc(doc(env.unauthenticatedContext().firestore(), path), {
    revision: 2, updatedAt: serverTimestamp(), "profile.nickname": "attack",
  }));
});
test("clients cannot create saves, delete saves or forge permanent battle XP claims", async () => {
  await assertFails(deleteDoc(doc(db(), path)));
  await assertFails(setDoc(doc(db(), "envs/test/users/player/accountBattleClaims/match-1"), {claimed: true}));
  await env.clearFirestore();
  await assertFails(setDoc(doc(db(), path), original()));
});

test("player statistics cannot be read or forged directly, even by their owner", async () => {
  const statsPath = "envs/test/users/player/statistics/current";
  await env.withSecurityRulesDisabled((context) => setDoc(doc(context.firestore(), statsPath),
    {revision: 1, lifetime: {wins: 2}, legacyProgress: {WinBattle: 2}}));
  for (const firestore of [db(), db("stranger"), env.unauthenticatedContext().firestore()]) {
    await assertFails(getDoc(doc(firestore, statsPath)));
    await assertFails(setDoc(doc(firestore, statsPath), {lifetime: {wins: 999}}));
    await assertFails(updateDoc(doc(firestore, statsPath), {"lifetime.wins": 999}));
    await assertFails(deleteDoc(doc(firestore, statsPath)));
  }
});
