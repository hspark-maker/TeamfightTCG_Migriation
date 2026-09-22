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
const saves = built("save/saveDocument");
const {ensureTitles} = built("commands/ensureTitles");
const {getPlayerStatistics} = built("commands/getPlayerStatistics");
const {getAchievements} = built("commands/getAchievements");
const {claimAchievement} = built("commands/claimAchievement");
const {claimBattleExperience} = built("commands/claimBattleExperience");
const specs = built("specs/specBlobReader");
after(() => db.terminate());
const achievements = [
  {id: 1, achievementId: "win.1", groupId: "win", stage: 1, eventKey: "WinBattle", targetCount: 1},
  {id: 2, achievementId: "win.2", groupId: "win", stage: 2, eventKey: "WinBattle", targetCount: 10},
  {id: 3, achievementId: "pack.1", groupId: "pack", stage: 1, eventKey: "OpenPack", targetCount: 1},
  {id: 4, achievementId: "album.1", groupId: "album", stage: 1, eventKey: "CompleteAlbum", targetCount: 1},
].map(r => ({...r, synergyId: "", title: r.achievementId, description: "condition " + r.targetCount,
  sortOrder: r.id, enabled: 1}));
const titles = achievements.map(r => ({id: r.id, titleId: "title_" + r.achievementId}));
const rewards = achievements.flatMap(r => [
  {id: r.id * 2, ownerType: "Achievement", ownerId: r.achievementId, order: 1, rewardType: "Currency", rewardId: "Gold", amount: 1},
  {id: r.id * 2 + 1, ownerType: "Achievement", ownerId: r.achievementId, order: 2, rewardType: "Title", rewardId: "title_" + r.achievementId, amount: 1},
]);
async function setup(t, wins = 0) {
  t.mock.method(specs, "readOptionalSpecRows", async (_env, name) => {assert.equal(name, "Title"); return titles;});
  t.mock.method(specs, "readSpecRows", async (_env, name) => {
    if (name === "Reward") return rewards;
    if (name === "Achievement") return achievements;
    if (name === "AlbumEntry") return [{id: 1, themeId: "one", pageId: "p1", cardId: 1, order: 1}];
    if (name === "AlbumThemeInfo") return [{id: 1, themeId: "one", locked: 0}];
    if (name === "AccountLevel") return [{id: 1, requiredExp: 0, winExp: 10, loseExp: 10}];
    throw new Error("Unexpected table " + name);
  });
  const uid = "achievement-title-" + randomUUID();
  const root = db.doc("envs/test/users/" + uid);
  await root.collection("save").doc("current").set({schemaVersion: 8, revision: 1, ownership: {cardIds: []},
    profile: {nickname: "kept", accountExp: 0, accountRewardLevel: 1, equippedTitleId: "", ownedAvatarIds: ["base"]}});
  await root.collection("wallet").doc("current").set({rev: 1, balances: {Gold: 100}, paidBalances: {}});
  await root.collection("achievements").doc("current").set({revision: 1, progress: {WinBattle: wins}, claimed: {}});
  const request = (achievementId = "win.1", txId = randomUUID()) =>
    ({auth: {uid}, data: {env: "test", achievementId, txId}});
  const read = async () => (await root.collection("save").doc("current").get()).data();
  const state = async () => (await db.getAll(root.collection("save").doc("current"),
    root.collection("wallet").doc("current"), root.collection("achievements").doc("current"),
    root.collection("statistics").doc("current"))).map(s => s.data());
  return {uid, root, request, read, state};
}

test("boot and statistics queries never award achieved titles; boot only supplies unlock descriptions", async t => {
  const {request, read, root} = await setup(t, 10);
  const before = await read();
  const first = await ensureTitles.run(request());
  assert.equal(first.changed, false); assert.equal(first.revision, 1); assert.deepEqual(first.titles, []);
  assert.match(first.definitions[1].description, /condition 10/);
  assert.match(first.definitions[1].description, /업적 보상/);
  assert.deepEqual(await ensureTitles.run(request()), first);
  await getPlayerStatistics.run(request());
  const queried = await getAchievements.run(request());
  assert.deepEqual(queried.definitions.find(d => d.id === "win.1").reward.items,
    [{rewardType: "Title", rewardId: "title_win.1", amount: 1}]);
  assert.deepEqual(await read(), before);
  assert.deepEqual((await root.collection("achievements").doc("current").get()).data().claimed, {});
});

test("absent Title publication keeps boot read-only without requiring Achievement or Reward definitions", async t => {
  const {request, read} = await setup(t);
  const before = await read();
  t.mock.method(specs, "readOptionalSpecRows", async () => null);
  t.mock.method(specs, "readSpecRows", async (_env, name) => {throw new Error("Unexpected table " + name);});
  const result = await ensureTitles.run(request());
  assert.deepEqual(result.definitions, []);
  assert.deepEqual(result.titles, []);
  assert.deepEqual(await read(), before);
});

test("pack transactions retain counters and receipt replay but award no titles until each achievement is claimed", async t => {
  const {uid, request, read} = await setup(t);
  const txId = randomUUID();
  const invoke = () => saves.mutateSave("test", uid, "openPack", {kind: "client", txId}, async (current, tx, wallet, prepare) => {
    await prepare(1);
    return {slots: {ownership: {cardIds: [1]}, profile: {...current.profile, ownedAvatarIds: ["base", "new"]}}};
  }, adopted => ({...adopted, cosmetics: [{itemType: "Avatar", itemId: "new", isNew: true}]}));
  const [first, replay] = await Promise.all([invoke(), invoke()]);
  assert.deepEqual(first, replay);
  assert.equal((first.titles ?? []).length, 0);
  assert.equal(first.statistics.lifetime.packsOpened, 1);
  assert.equal(first.statistics.lifetime.albumsCompleted, 1);
  assert.equal((await read()).profile.ownedTitleIds, undefined);
  const pack = await claimAchievement.run(request("pack.1"));
  assert.deepEqual(pack.titles, [{titleId: "title_pack.1", isNew: true}]);
  assert.deepEqual((await read()).profile.ownedTitleIds, ["title_pack.1"]);
  const album = await claimAchievement.run(request("album.1"));
  assert.deepEqual(album.titles, [{titleId: "title_album.1", isNew: true}]);
  assert.deepEqual((await read()).profile.ownedAvatarIds, ["base", "new"]);
});

test("direct tutorial cards retain completed-album progress without granting titles or counting opened packs", async t => {
  const {uid, read} = await setup(t);
  const result = await saves.mutateSave("test", uid, "grantTutorialCards", {kind: "client", txId: randomUUID()},
    () => ({slots: {ownership: {cardIds: [1]}}}), adopted => adopted);
  assert.equal((result.titles ?? []).length, 0);
  assert.equal(result.statistics.lifetime.packsOpened, 0);
  assert.equal(result.statistics.lifetime.albumsCompleted, 1);
  assert.equal((await read()).profile.ownedTitleIds, undefined);
});

test("battle experience claim never grants an achieved title or mutates the other participant", async t => {
  const {uid, root, read} = await setup(t, 1);
  const matchId = randomUUID().replaceAll("-", "");
  const opponent = "opponent-" + randomUUID();
  const other = db.doc("envs/test/users/" + opponent + "/save/current");
  await other.set({schemaVersion: 8, revision: 7, profile: {nickname: "other"}});
  await db.doc("envs/test/matches/" + matchId).set({seedSource: "server", status: "confirmed", mode: "pvp",
    expectedParticipants: 2, participantUids: [uid, opponent], payouts: {[uid]: {won: true}, [opponent]: {won: false}}});
  const result = await claimBattleExperience.run({auth: {uid}, data: {env: "test", matchId, txId: randomUUID()}});
  assert.equal(result.accountExperience.grantedExp, 0);
  assert.equal((result.titles ?? []).length, 0);
  assert.equal((await read()).profile.ownedTitleIds, undefined);
  assert.equal((await other.get()).data().revision, 7);
  assert.equal((await root.collection("accountBattleClaims").doc(matchId).get()).exists, true);
});

test("each stage grants only its own title despite excess progress; same receipt credits once and replays title result", async t => {
  const {request, read, state} = await setup(t, 10);
  await assert.rejects(claimAchievement.run(request("win.2")), error => error.details?.reason === "NotEligible");
  const input = request();
  const [first, replay] = await Promise.all([claimAchievement.run(input), claimAchievement.run(input)]);
  assert.deepEqual(first, replay);
  assert.deepEqual(first.titles, [{titleId: "title_win.1", isNew: true}]);
  assert.deepEqual((await read()).profile.ownedTitleIds, ["title_win.1"]);
  assert.equal((await state())[1].balances.Gold, 101);
  await assert.rejects(claimAchievement.run(request("win.2", input.data.txId)), error => error.details?.reason === "TxIdReused");
  const second = await claimAchievement.run(request("win.2"));
  assert.deepEqual(second.titles, [{titleId: "title_win.2", isNew: true}]);
  assert.equal((await state())[1].balances.Gold, 102);
  assert.equal(second.achievements.progress.WinBattle, 10);
});

test("different receipt races grant a stage once and permanent marker survives receipt expiry", async t => {
  const {request, root, state} = await setup(t, 10);
  const results = await Promise.allSettled([claimAchievement.run(request()), claimAchievement.run(request())]);
  assert.equal(results.filter(r => r.status === "fulfilled").length, 1);
  assert.equal(results.find(r => r.status === "rejected").reason.details.reason, "AlreadyClaimed");
  const receipts = await root.collection("wallet").doc("current").collection("receipts").get();
  await Promise.all(receipts.docs.map(d => d.ref.delete()));
  await assert.rejects(claimAchievement.run(request()), error => error.details?.reason === "AlreadyClaimed");
  const [save, wallet] = await state();
  assert.deepEqual(save.profile.ownedTitleIds, ["title_win.1"]); assert.equal(wallet.balances.Gold, 101);
});

test("existing automatic ownership is preserved and claiming the achievement still grants currency once", async t => {
  const {request, root, state} = await setup(t, 10);
  await root.collection("save").doc("current").update({"profile.ownedTitleIds": ["legacy", "title_win.1"],
    "profile.equippedTitleId": "legacy"});
  const result = await claimAchievement.run(request());
  assert.deepEqual(result.titles, [{titleId: "title_win.1", isNew: false}]);
  const [save, wallet, achievements] = await state();
  assert.deepEqual(save.profile.ownedTitleIds, ["legacy", "title_win.1"]);
  assert.equal(save.profile.equippedTitleId, "legacy"); assert.equal(wallet.balances.Gold, 101);
  assert.equal(achievements.claimed["win.1"], true);
});

test("failure after queued claim writes rolls back titles, currency, statistics and claim marker together", async t => {
  const {request, state} = await setup(t, 10);
  const before = await state();
  const real = saves.mutateSave;
  t.mock.method(saves, "mutateSave", (env, uid, source, receipt, mutate, finalize) =>
    real(env, uid, source, receipt, async (...args) => {await mutate(...args); throw new Error("injected abort");}, finalize));
  await assert.rejects(claimAchievement.run(request()), /injected abort/);
  assert.deepEqual(await state(), before);
});

test("crafting preserves completed-album statistics but its title waits for achievement reward claim", async t => {
  const {request, root, read} = await setup(t);
  const crafting = built("crafting/spec");
  const missions = built("missions/missionSpec");
  const {craftCard} = built("commands/craftCard");
  t.mock.method(crafting, "readCraftingCatalog", async () => ({recipes: [{cardId: 1, grade: "Common", cost: 1}],
    cards: [{id: 1, grade: "Common", channel: "Live", synergies: ""}]}));
  t.mock.method(missions, "readMissionCatalog", async () => []);
  await root.collection("wallet").doc("current").update({balances: {CardDust: 10}});
  const input = request(); input.data.cardId = 1;
  const result = await craftCard.run(input);
  assert.equal((result.titles ?? []).length, 0);
  assert.equal(result.statistics.lifetime.albumsCompleted, 1);
  assert.equal((await read()).profile.ownedTitleIds, undefined);
  const claimed = await claimAchievement.run(request("album.1"));
  assert.deepEqual(claimed.titles, [{titleId: "title_album.1", isNew: true}]);
});
