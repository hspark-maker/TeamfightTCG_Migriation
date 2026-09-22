"use strict";
const assert = require("node:assert/strict");
const {test, after} = require("node:test");
const {randomUUID} = require("node:crypto");
const {resolve} = require("node:path");
if (!/^(127\.0\.0\.1|localhost):\d+$/.test(process.env.FIRESTORE_EMULATOR_HOST ?? "") ||
    !String(process.env.GCLOUD_PROJECT ?? "").startsWith("demo-")) {
  throw new Error("Local emulator + demo project required");
}
const output = process.env.TITLE_TEST_BUILD || "../lib";
const built = name => require(resolve(__dirname, output, name));
const {db} = built("firebaseApp");
const saves = built("save/saveDocument");
const specs = built("specs/specBlobReader");
const {readStatistics} = built("statistics/playerStatistics");
const {devCompleteAchievements} = built("commands/devCompleteAchievements");
const {getAchievements} = built("commands/getAchievements");
const {claimAchievement} = built("commands/claimAchievement");
after(() => db.terminate());

const achievementRows = [
  ["wins.1", "wins", 1, "WinBattle", 1, ""],
  ["wins.2", "wins", 2, "WinBattle", 10, ""],
  ["destroy.1", "destroy", 1, "DestroyCards", 30, ""],
  ["pack.1", "pack", 1, "OpenPack", 8, ""],
  ["album.1", "album", 1, "CompleteAlbum", 5, ""],
  ["streak.1", "streak", 1, "WinStreak", 7, ""],
  ["brand.1", "brand", 1, "PlaySynergy", 1, "Brand"],
  ["brand.2", "brand", 2, "PlaySynergy", 10, "Brand"],
  ["trace.1", "trace", 1, "PlaySynergy", 5, "Trace"],
].map(([achievementId, groupId, stage, eventKey, targetCount, synergyId], i) => ({id: i + 1,
  achievementId, groupId, stage, eventKey, targetCount, synergyId, title: achievementId,
  description: achievementId, sortOrder: i + 1, enabled: 1}));
const titleRows = [1, 10].map((targetCount, i) => ({id: i + 1, titleId: "title_win_" + targetCount}));
const rewards = [...achievementRows.map(r => ({id: r.id, ownerType: "Achievement", ownerId: r.achievementId,
  order: 1, rewardType: "Currency", rewardId: "Gold", amount: 10})),
  ...titleRows.map((r, i) => ({id: 100 + i, ownerType: "Achievement", ownerId: "wins." + (i + 1),
    order: 2, rewardType: "Title", rewardId: r.titleId, amount: 1}))];
const desiredProgress = {WinBattle: 10, DestroyCards: 30, OpenPack: 8, CompleteAlbum: 5,
  WinStreak: 7, "PlaySynergy:Brand": 10, "PlaySynergy:Trace": 5};

async function setup(t, {progress = {}, claimed = {}} = {}) {
  t.mock.method(specs, "readOptionalSpecRows", async (_env, name) => {
    assert.equal(name, "Title"); return titleRows;
  });
  t.mock.method(specs, "readSpecRows", async (_env, name) => {
    if (name === "Achievement") return [...achievementRows,
      {...achievementRows[0], id: 99, achievementId: "disabled.1", groupId: "disabled", targetCount: 99999, enabled: 0}];
    if (name === "AlbumEntry" || name === "AlbumThemeInfo") return [];
    if (name === "Reward") return rewards;
    throw new Error("Unexpected table " + name);
  });
  const uid = "dev-achievements-" + randomUUID();
  const root = db.doc("envs/test/users/" + uid);
  const legacy = {revision: 3, progress, claimed, currentWinStreak: 3};
  const statistics = readStatistics(undefined, legacy, 12345);
  statistics.legacyProgress = {...progress};
  statistics.revision = 2;
  statistics.battle.all = {...statistics.battle.all, battles: 4, wins: 3, losses: 1, cardsDestroyed: 8,
    attacks: 21, damageDealt: 30, healed: 2, synergyTriggers: 5, currentWinStreak: 3, bestWinStreak: 3};
  statistics.battle.ranked = {...statistics.battle.all};
  await Promise.all([
    root.collection("save").doc("current").set({schemaVersion: 8, revision: 7,
      ownership: {cardIds: [1]}, profile: {nickname: "kept", ownedTitleIds: ["legacy"], equippedTitleId: "legacy",
        ownedAvatarIds: ["base"], accountExp: 500}}),
    root.collection("wallet").doc("current").set({rev: 5, balances: {Gold: 100, Diamond: 3}, paidBalances: {Diamond: 2}}),
    root.collection("achievements").doc("current").set(legacy),
    root.collection("statistics").doc("current").set({...statistics, schemaVersion: 1}),
  ]);
  const request = (txId = randomUUID()) => ({auth: {uid}, data: {env: "test", txId}});
  const read = async () => (await db.getAll(root.collection("save").doc("current"),
    root.collection("wallet").doc("current"), root.collection("achievements").doc("current"),
    root.collection("statistics").doc("current"))).map(s => s.data());
  return {uid, root, request, read};
}

test("debug completion requires authentication and refuses live or unknown environments before writes", async t => {
  const {request, read} = await setup(t);
  const before = await read();
  await assert.rejects(devCompleteAchievements.run({data: {env: "test"}}), error => error.code === "unauthenticated");
  for (const env of ["live", "unknown", ""]) {
    await assert.rejects(devCompleteAchievements.run({...request(), data: {env, txId: randomUUID()}}),
      error => error.code === "permission-denied");
  }
  assert.deepEqual(await read(), before);
});

test("all six event kinds and each synergy reach their highest enabled target without altering battle details", async t => {
  const {request, read} = await setup(t);
  const before = await read();
  const result = await devCompleteAchievements.run(request());
  const [save, wallet, achievements, statistics] = await read();
  assert.equal(result.completedAchievements, achievementRows.length);
  assert.deepEqual(result.achievements.progress, desiredProgress);
  assert.deepEqual(achievements.progress, desiredProgress);
  assert.equal(statistics.lifetime.currentWinStreak, 3);
  assert.equal(statistics.lifetime.bestWinStreak, 7);
  assert.deepEqual(statistics.battle, before[3].battle);
  assert.equal(statistics.trackedSinceMs, before[3].trackedSinceMs);
  assert.deepEqual(achievements.claimed, {});
  assert.deepEqual(save.profile, before[0].profile);
  assert.deepEqual(save.ownership, before[0].ownership);
  assert.deepEqual(wallet, before[1]);
  assert.equal((result.titles ?? []).length, 0);
});

test("existing greater progress, unrelated synergy counters and permanent claim markers remain intact", async t => {
  const {request, read} = await setup(t, {progress: {WinBattle: 100, DestroyCards: 90, OpenPack: 20,
    CompleteAlbum: 7, WinStreak: 15, "PlaySynergy:Brand": 40, "PlaySynergy:Trace": 30,
    "PlaySynergy:Bulk": 200}, claimed: {"wins.1": true, "legacy.1": true}});
  const before = await read();
  await devCompleteAchievements.run(request());
  const after = await read();
  assert.deepEqual(after[2].progress, before[2].progress);
  assert.deepEqual(after[2].claimed, before[2].claimed);
  assert.deepEqual(after[0].profile, before[0].profile);
  assert.deepEqual(after[1], before[1]);
  assert.deepEqual(after[3].battle, before[3].battle);
});

test("concurrent same receipt is applied once and later replay does not change revisions", async t => {
  const {request, read} = await setup(t);
  const before = await read();
  const input = request();
  const [first, duplicate] = await Promise.all([devCompleteAchievements.run(input), devCompleteAchievements.run(input)]);
  assert.deepEqual(first, duplicate);
  const after = await read();
  assert.equal(after[0].revision, before[0].revision + 1);
  assert.equal(after[2].revision, before[2].revision + 1);
  assert.equal(after[3].revision, before[3].revision + 1);
  assert.deepEqual(await devCompleteAchievements.run(input), first);
  assert.deepEqual(await read(), after);
});

test("getAchievements preserves debug completion including synthetic album progress without backfill inflation", async t => {
  const {request, read} = await setup(t);
  const completed = await devCompleteAchievements.run(request());
  const before = await read();
  const first = await getAchievements.run(request());
  const second = await getAchievements.run(request());
  assert.deepEqual(first, second);
  assert.deepEqual(first.achievements.progress, completed.achievements.progress);
  assert.deepEqual(await read(), before);
});

test("debug completion grants no rewards; existing sequential claims grant each title and currency normally", async t => {
  const {request, read} = await setup(t);
  await devCompleteAchievements.run(request());
  assert.deepEqual((await read())[0].profile.ownedTitleIds, ["legacy"]);
  const claim = achievementId => claimAchievement.run({...request(), data: {...request().data, achievementId}});
  await assert.rejects(claim("wins.2"), error => error.details?.reason === "NotEligible");
  const first = await claim("wins.1");
  assert.deepEqual(first.titles, [{titleId: "title_win_1", isNew: true}]);
  assert.equal((await read())[1].balances.Gold, 110);
  const second = await claim("wins.2");
  assert.deepEqual(second.titles, [{titleId: "title_win_10", isNew: true}]);
  const [save, wallet, achievements] = await read();
  assert.deepEqual(save.profile.ownedTitleIds, ["legacy", "title_win_1", "title_win_10"]);
  assert.equal(wallet.balances.Gold, 120);
  assert.deepEqual(achievements.claimed, {"wins.1": true, "wins.2": true});
  assert.deepEqual(achievements.progress, desiredProgress);
});

test("failure after queued statistics writes rolls back save, wallet, claim markers and all counters", async t => {
  const {request, read, root} = await setup(t);
  const before = await read();
  const real = saves.mutateSave;
  t.mock.method(saves, "mutateSave", (env, uid, source, receipt, mutate, finalize) =>
    real(env, uid, source, receipt, async (...args) => {await mutate(...args); throw new Error("injected abort");}, finalize));
  await assert.rejects(devCompleteAchievements.run(request()), /injected abort/);
  assert.deepEqual(await read(), before);
  assert.equal((await root.collection("wallet").doc("current").collection("receipts").get()).size, 0);
});

test("caller cannot target a different account through payload uid", async t => {
  const caller = await setup(t);
  const otherRoot = db.doc("envs/test/users/other-" + randomUUID());
  await otherRoot.collection("save").doc("current").set({schemaVersion: 8, revision: 1, profile: {nickname: "other"}});
  const request = caller.request(); request.data.uid = otherRoot.id;
  await devCompleteAchievements.run(request);
  assert.equal((await caller.read())[2].progress.WinBattle, 10);
  assert.equal((await otherRoot.collection("save").doc("current").get()).data().revision, 1);
  assert.equal((await otherRoot.collection("statistics").doc("current").get()).exists, false);
});
