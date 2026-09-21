"use strict";
const assert = require("node:assert/strict");
const {test, after} = require("node:test");
const {randomUUID} = require("node:crypto");

if (!/^(127\.0\.0\.1|localhost):\d+$/.test(process.env.FIRESTORE_EMULATOR_HOST ?? "") ||
    !String(process.env.GCLOUD_PROJECT ?? "").startsWith("demo-")) {
  throw new Error("A local Firestore emulator and demo-* project are required.");
}
const {db} = require("../lib/firebaseApp");
const specs = require("../lib/specs/specBlobReader");
const missions = require("../lib/missions/missionSpec");
const {missionPeriod} = require("../lib/missions/period");
const {getMissions} = require("../lib/commands/getMissions");
after(() => db.terminate());

const deckKey = "guide.Guide.DeckSaved6";
const rankKey = "guide.Guide.Bronze2Reached";
const catalog = [{id: "guide.deck", period: "guide", event: "Guide.DeckSaved6", target: 1,
  title: "Deck", description: "Save six cards", sortOrder: 1, enabled: true,
  guideActId: 1, guideActName: "Start", passExp: 0, accountExp: 0}];

async function setup(t, stored) {
  const uid = "mission-query-efficiency-" + randomUUID();
  const root = db.doc(`envs/test/users/${uid}`);
  const missionRef = root.collection("missions").doc("current");
  const saveRef = root.collection("save").doc("current");
  const rankRef = root.collection("rank").doc("current");
  t.mock.method(specs, "readSpecRows", async (_env, table) => {
    if (table === "Card" || table === "Reward") return [];
    throw new Error("Unexpected table " + table);
  });
  t.mock.method(missions, "readMissionCatalog", async () => catalog);
  await saveRef.set({ownership: {cardIds: []}, profile: {nickname: "unchanged"}});
  if (stored !== undefined) await missionRef.set(stored);
  return {missionRef, saveRef, rankRef,
    query: () => getMissions.run({auth: {uid}, data: {env: "test"}})};
}

test("repeated incomplete guide queries keep the persisted updateTime and response contract", async (t) => {
  const period = missionPeriod(Date.now());
  const stored = {dailyKey: period.daily, weeklyKey: period.weekly,
    progress: {"daily.OpenPack": 3, [deckKey]: 0}, claimed: {"guide.previous": true}, passExp: 7};
  const {missionRef, query} = await setup(t, stored);
  const before = await missionRef.get();
  const first = await query();
  const second = await query();
  const afterQuery = await missionRef.get();
  assert.ok(afterQuery.updateTime.isEqual(before.updateTime));
  assert.deepEqual(afterQuery.data(), stored);
  assert.deepEqual(second, first);
  assert.equal(first.missions.progress[deckKey], 0);
  assert.equal(first.missions.progress[rankKey], 0);
  assert.equal(first.missions.progress["daily.OpenPack"], 3);
  assert.deepEqual(first.missions.claimed, stored.claimed);
  assert.equal(first.missions.passExp, 7);
  assert.equal(first.definitions[0].id, "guide.deck");
  assert.deepEqual(first.definitions[0].reward, {currencies: [], items: [], passExp: 0, accountExp: 0});
});

test("an empty first query does not create a mission document", async (t) => {
  const {missionRef, query} = await setup(t);
  const first = await query();
  assert.equal(first.missions.progress[deckKey], 0);
  assert.equal((await missionRef.get()).exists, false);
  assert.deepEqual(await query(), first);
  assert.equal((await missionRef.get()).exists, false);
});

test("new deck and rank progress persist once and later queries preserve the maximum", async (t) => {
  const {missionRef, saveRef, rankRef, query} = await setup(t, {
    progress: {}, claimed: {"guide.previous": true}, passExp: 4});
  const before = await missionRef.get();
  const cardIds = [1, 2, 3, 4, 5, 6];
  await saveRef.set({ownership: {cardIds}, deck: {slots: [{cardIds}], selectedSlot: 0}});
  await rankRef.set({bestTierIndex: 1});
  const first = await query();
  const committed = await missionRef.get();
  assert.ok(!committed.updateTime.isEqual(before.updateTime));
  assert.equal(first.missions.progress[deckKey], 1);
  assert.equal(first.missions.progress[rankKey], 1);
  assert.equal(committed.data().progress[deckKey], 1);
  assert.deepEqual(committed.data().claimed, {"guide.previous": true});
  assert.equal(committed.data().passExp, 4);
  await saveRef.set({ownership: {cardIds: []}});
  await rankRef.set({bestTierIndex: 0});
  const second = await query();
  assert.equal(second.missions.progress[deckKey], 1);
  assert.equal(second.missions.progress[rankKey], 1);
  assert.ok((await missionRef.get()).updateTime.isEqual(committed.updateTime));
});

test("period reset remains response-only when guide progress does not change", async (t) => {
  const stored = {dailyKey: "2000-01-01", weeklyKey: "2000-01-01",
    progress: {"daily.OpenPack": 3, "weekly.OpenPack": 8, [deckKey]: 1, [rankKey]: 1},
    claimed: {"daily.old": true, "weekly.old": true, "guide.previous": true}, passExp: 5};
  const {missionRef, query} = await setup(t, stored);
  const before = await missionRef.get();
  const result = await query();
  assert.equal(result.missions.progress["daily.OpenPack"], undefined);
  assert.equal(result.missions.progress["weekly.OpenPack"], undefined);
  assert.equal(result.missions.progress[deckKey], 1);
  assert.deepEqual(result.missions.claimed, {"guide.previous": true});
  assert.equal(result.missions.passExp, 5);
  const afterQuery = await missionRef.get();
  assert.ok(afterQuery.updateTime.isEqual(before.updateTime));
  assert.deepEqual(afterQuery.data(), stored);
});
