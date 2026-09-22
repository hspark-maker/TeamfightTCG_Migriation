"use strict";
const assert = require("node:assert/strict");
const {test, after} = require("node:test");
const {randomUUID} = require("node:crypto");
const {resolve} = require("node:path");
if (!/^(127\.0\.0\.1|localhost):\d+$/.test(process.env.FIRESTORE_EMULATOR_HOST ?? "") ||
    !String(process.env.GCLOUD_PROJECT ?? "").startsWith("demo-")) throw new Error("Local emulator + demo project required");
const output = process.env.TITLE_TEST_BUILD || "../lib";
const built = name => require(resolve(__dirname, output, name));
const {db} = built("firebaseApp");
const {mutateSave} = built("save/saveDocument");
const {ensureTitles} = built("commands/ensureTitles");
const {claimBattleExperience} = built("commands/claimBattleExperience");
const specs = built("specs/specBlobReader");
after(() => db.terminate());
const achievements = [
  {id: 1, achievementId: "win.1", groupId: "win", stage: 1, eventKey: "WinBattle", targetCount: 1},
  {id: 2, achievementId: "win.2", groupId: "win", stage: 2, eventKey: "WinBattle", targetCount: 10},
  {id: 3, achievementId: "pack.1", groupId: "pack", stage: 1, eventKey: "OpenPack", targetCount: 1},
  {id: 4, achievementId: "album.1", groupId: "album", stage: 1, eventKey: "CompleteAlbum", targetCount: 1},
].map(r => ({...r, synergyId: "", title: r.achievementId, description: "condition " + r.targetCount,
  rewardCurrency: "Gold", rewardAmount: 1, sortOrder: r.id, enabled: 1}));
const titles = achievements.map(r => ({id: r.id, titleId: "title_" + r.achievementId,
  eventKey: r.eventKey, synergyId: r.synergyId, targetCount: r.targetCount, description: r.description}));
async function setup(t, wins = 0) {
  t.mock.method(specs, "readOptionalSpecRows", async (_env, name) => {
    assert.equal(name, "Title"); return titles;
  });
  t.mock.method(specs, "readSpecRows", async (_env, name) => {
    if (name === "Reward") return [];
    if (name === "Achievement") return achievements;
    if (name === "Title") return titles;
    if (name === "AlbumEntry") return [{id: 1, themeId: "one", pageId: "p1", cardId: 1, order: 1}];
    if (name === "AlbumThemeInfo") return [{id: 1, themeId: "one", locked: 0}];
    if (name === "AccountLevel") return [{id: 1, requiredExp: 0, winExp: 10, loseExp: 10}];
    throw new Error("Unexpected table " + name);
  });
  const uid = "auto-title-" + randomUUID();
  const root = db.doc("envs/test/users/" + uid);
  await root.collection("save").doc("current").set({schemaVersion: 8, revision: 1, ownership: {cardIds: []},
    profile: {nickname: "kept", accountExp: 0, accountRewardLevel: 1, equippedTitleId: "", ownedAvatarIds: ["base"]}});
  await root.collection("wallet").doc("current").set({rev: 1, balances: {Gold: 100}, paidBalances: {}});
  await root.collection("achievements").doc("current").set({revision: 1, progress: {WinBattle: wins}, claimed: {}});
  return {uid, root, read: async () => (await root.collection("save").doc("current").get()).data()};
}

test("boot reconciliation grants achieved stages without a claim and is idempotent", async t => {
  const {uid, root, read} = await setup(t, 10);
  const first = await ensureTitles.run({auth: {uid}, data: {env: "test"}});
  assert.equal(first.changed, true);
  assert.equal(first.revision, 2);
  assert.deepEqual(first.titles.map(x => x.titleId), ["title_win.1", "title_win.2"]);
  assert.equal(first.definitions[1].description, "condition 10");
  const next = await ensureTitles.run({auth: {uid}, data: {env: "test"}});
  assert.equal(next.changed, false); assert.equal(next.revision, 2); assert.deepEqual(next.titles, []);
  assert.deepEqual((await read()).profile.ownedAvatarIds, ["base"]);
  assert.deepEqual((await root.collection("achievements").doc("current").get()).data().claimed, {});
});

test("one pack transaction grants pack and completed-album titles with one save revision and replays them", async t => {
  const {uid, read} = await setup(t);
  const txId = randomUUID();
  const invoke = () => mutateSave("test", uid, "openPack", {kind: "client", txId}, async (current, tx, wallet, prepare) => {
    await prepare(1);
    return {slots: {ownership: {cardIds: [1]}, profile: {...current.profile, ownedAvatarIds: ["base", "new"]}}};
  }, adopted => ({...adopted, cosmetics: [{itemType: "Avatar", itemId: "new", isNew: true}]}));
  const [first, replay] = await Promise.all([invoke(), invoke()]);
  assert.deepEqual(first.titles, replay.titles);
  assert.deepEqual(first.titles.map(x => x.titleId), ["title_pack.1", "title_album.1"]);
  assert.equal(first.statistics.lifetime.packsOpened, 1);
  assert.equal(first.statistics.lifetime.albumsCompleted, 1);
  const saved = await read();
  assert.equal(saved.revision, 2);
  assert.deepEqual(saved.profile.ownedAvatarIds, ["base", "new"]);
  assert.equal(saved.profile.nickname, "kept");
});

test("direct tutorial cards trigger album title immediately without counting as an opened pack", async t => {
  const {uid, read} = await setup(t);
  const result = await mutateSave("test", uid, "grantTutorialCards", {kind: "client", txId: randomUUID()},
    () => ({slots: {ownership: {cardIds: [1]}}}), adopted => adopted);
  assert.deepEqual(result.titles.map(x => x.titleId), ["title_album.1"]);
  assert.equal(result.statistics.lifetime.packsOpened, 0);
  assert.equal((await read()).revision, 2);
});

test("battle claim at experience cap still grants the title and only changes its own save", async t => {
  const {uid, root, read} = await setup(t, 1);
  const matchId = randomUUID().replaceAll("-", "");
  const opponent = "opponent-" + randomUUID();
  const other = db.doc("envs/test/users/" + opponent + "/save/current");
  await other.set({schemaVersion: 8, revision: 7, profile: {nickname: "other"}});
  await db.doc("envs/test/matches/" + matchId).set({seedSource: "server", status: "confirmed", mode: "pvp",
    expectedParticipants: 2, participantUids: [uid, opponent], payouts: {[uid]: {won: true}, [opponent]: {won: false}}});
  const result = await claimBattleExperience.run({auth: {uid}, data: {env: "test", matchId, txId: randomUUID()}});
  assert.equal(result.accountExperience.grantedExp, 0);
  assert.deepEqual(result.titles, [{titleId: "title_win.1", isNew: true}]);
  assert.equal((await read()).revision, 2);
  assert.equal((await other.get()).data().revision, 7);
  assert.equal((await root.collection("accountBattleClaims").doc(matchId).get()).exists, true);
});

test("failed underlying mutation leaves titles, statistics and save untouched", async t => {
  const {uid, root, read} = await setup(t, 10);
  await assert.rejects(mutateSave("test", uid, "grantTutorialCards", {kind: "client", txId: randomUUID()},
    () => {throw new Error("injected failure");}, adopted => adopted));
  assert.equal((await read()).revision, 1);
  assert.equal((await root.collection("statistics").doc("current").get()).exists, false);
});

test("achievement claim keeps its claim marker and uses the same final statistics for automatic titles", async t => {
  const {uid, root, read} = await setup(t, 10);
  const {claimAchievement} = built("commands/claimAchievement");
  const result = await claimAchievement.run({auth: {uid}, data: {env: "test", achievementId: "win.1", txId: randomUUID()}});
  assert.deepEqual(result.titles.map(x => x.titleId), ["title_win.1", "title_win.2"]);
  assert.equal(result.achievements.claimed["win.1"], true);
  assert.equal((await root.collection("achievements").doc("current").get()).data().claimed["win.1"], true);
  assert.equal((await read()).revision, 2);
});

test("crafting uses its completed-album statistics without overwriting them in the common title hook", async t => {
  const {uid, root, read} = await setup(t);
  const crafting = built("crafting/spec");
  const missions = built("missions/missionSpec");
  const {craftCard} = built("commands/craftCard");
  t.mock.method(crafting, "readCraftingCatalog", async () => ({recipes: [{cardId: 1, grade: "Common", cost: 1}],
    cards: [{id: 1, grade: "Common", channel: "Live", synergies: ""}]}));
  t.mock.method(missions, "readMissionCatalog", async () => []);
  await root.collection("wallet").doc("current").update({balances: {CardDust: 10}});
  const result = await craftCard.run({auth: {uid}, data: {env: "test", cardId: 1, txId: randomUUID()}});
  assert.deepEqual(result.titles, [{titleId: "title_album.1", isNew: true}]);
  assert.equal(result.statistics.lifetime.albumsCompleted, 1);
  const stats = (await root.collection("statistics").doc("current").get()).data();
  assert.equal(stats.revision, result.statistics.revision);
  assert.equal(stats.lifetime.albumsCompleted, 1);
  assert.equal((await read()).revision, 2);
});
