"use strict";
// Only a local emulator and disposable demo project are allowed.
const assert = require("node:assert/strict");
const {test, after} = require("node:test");
const {randomUUID, createHash} = require("node:crypto");
if (!/^(127\.0\.0\.1|localhost):\d+$/.test(process.env.FIRESTORE_EMULATOR_HOST ?? "") ||
    !String(process.env.GCLOUD_PROJECT ?? "").startsWith("demo-")) {
  throw new Error("A local Firestore emulator and demo-* project are required.");
}
const {db} = require("../lib/firebaseApp");
const specs = require("../lib/specs/specBlobReader");
const saves = require("../lib/save/saveDocument");
const missions = require("../lib/missions/missionSpec");
const replay = require("../lib/battleReplayService");
const replayConfig = require("../lib/battleReplayConfig");
const {computeDeckHash} = require("../lib/deckValidation");
const {claimAchievement} = require("../lib/commands/claimAchievement");
const {getAchievements} = require("../lib/commands/getAchievements");
const {openPack} = require("../lib/commands/openPack");
const {submitMatchResult} = require("../lib/commands/submitMatchResult");
after(() => db.terminate());

const definitions = [1, 2].map((stage) => ({id: stage, achievementId: `wins.${stage}`, groupId: "wins", stage,
  eventKey: "WinBattle", synergyId: "", targetCount: stage, title: "Wins", description: "Total wins",
  rewardCurrency: stage === 1 ? "Gold" : "Diamond", rewardAmount: 10, sortOrder: 1, enabled: 1}));
const albumDef = {id: 3, achievementId: "album.1", groupId: "album", stage: 1, eventKey: "CompleteAlbum",
  synergyId: "", targetCount: 1, title: "Albums", description: "Complete themes", rewardCurrency: "Shard", rewardAmount: 3,
  sortOrder: 2, enabled: 1};
const cards = Array.from({length: 6}, (_, i) => ({id: i + 1, channel: "Live", grade: "Common", maxHp: 5,
  keywords: 0, keywordUnlockLevel: 1, hp2: 6, hp3: 7, hp4: 8, synergies: "Data_Synergy_Bulk"}));
const table = (name) => {
  if (name === "Achievement") return [...definitions, albumDef];
  if (name === "AlbumEntry") return [{id: 1, themeId: "forest", pageId: "one", cardId: 1}];
  if (name === "AlbumThemeInfo") return [{id: 1, themeId: "forest", locked: 0}];
  if (name === "Card") return cards;
  if (name === "CardPack") return [{id: 1, packId: "pack", price: 10, priceType: "Gold", drawCount: 1}];
  if (name === "CardPackDrop") return [{id: 1, packId: "pack", cardId: 2, weight: 1}];
  if (name === "RankGrade") return [];
  if (name === "Reward" || name === "PassSeason") return [];
  if (name === "CardEnhanceRule") return [{id: 1, maxLevel: 4, maxLimitBreak: 1}];
  if (name === "CardLimitBreak") return [{id: 1, stage: 1, hpGain: 1, snackCost: 3}];
  if (name === "SynergyTierDef") return [{id: 1, synergyId: "Bulk", requiredCount: 2}];
  throw new Error("Unexpected table: " + name);
};

async function setup(t, progress = {WinBattle: 3}) {
  const uid = "achievement-test-" + randomUUID();
  const root = db.doc(`envs/test/users/${uid}`);
  t.mock.method(specs, "readSpecRows", async (_env, name) => table(name));
  t.mock.method(specs, "readPinnedSpecRows", async (_env, name) => table(name));
  t.mock.method(missions, "readMissionCatalog", async () => []);
  await Promise.all([
    root.collection("save").doc("current").set({schemaVersion: 8, revision: 1,
      ownership: {cardIds: [1]}, profile: {nickname: "kept"}}),
    root.collection("wallet").doc("current").set({rev: 1, balances: {Gold: 100}}),
    root.collection("achievements").doc("current").set({schemaVersion: 1, revision: 1, progress,
      currentWinStreak: 0, claimed: {}}),
  ]);
  const request = (achievementId = "wins.1", txId = randomUUID()) =>
    ({auth: {uid}, data: {env: "test", achievementId, txId}});
  const read = async () => (await db.getAll(root.collection("save").doc("current"),
    root.collection("wallet").doc("current"), root.collection("achievements").doc("current")))
    .map((snapshot) => snapshot.data());
  return {uid, root, request, read};
}

test("concurrent identical claim retries credit once and replay the same revision", async (t) => {
  const {request, read} = await setup(t);
  const input = request();
  const [a, b] = await Promise.all([claimAchievement.run(input), claimAchievement.run(input)]);
  assert.deepEqual(a, b);
  const [save, wallet, state] = await read();
  assert.equal(save.revision, 2); assert.equal(wallet.balances.Gold, 110);
  assert.equal(state.claimed["wins.1"], true); assert.equal(state.revision, 2);
  assert.equal(a.achievements.revision, 2); assert.equal(state.progress.WinBattle, 3);
});

test("different txIds racing on one stage grant once; permanent claim survives receipt expiry", async (t) => {
  const {request, read, root} = await setup(t);
  const results = await Promise.allSettled([claimAchievement.run(request()), claimAchievement.run(request())]);
  assert.equal(results.filter((result) => result.status === "fulfilled").length, 1);
  assert.equal(results.find((result) => result.status === "rejected").reason.details.reason, "AlreadyClaimed");
  const receipts = await root.collection("wallet").doc("current").collection("receipts").get();
  await Promise.all(receipts.docs.map((doc) => doc.ref.delete()));
  await assert.rejects(() => claimAchievement.run(request()), (error) => error.details?.reason === "AlreadyClaimed");
  assert.equal((await read())[1].balances.Gold, 110);
});

test("tiers claim in order, keep cumulative progress and reject reused txId arguments", async (t) => {
  const {request, read} = await setup(t);
  await assert.rejects(() => claimAchievement.run(request("wins.2")), (error) => error.details?.reason === "NotEligible");
  const first = request();
  await claimAchievement.run(first);
  await assert.rejects(() => claimAchievement.run(request("wins.2", first.data.txId)),
    (error) => error.details?.reason === "TxIdReused");
  const next = await claimAchievement.run(request("wins.2"));
  assert.equal(next.achievements.revision, 3);
  assert.equal((await read())[1].balances.Diamond, 10);
  assert.equal((await read())[2].progress.WinBattle, 3);
});

test("failure after queued claim writes rolls back wallet, revision and permanent marker together", async (t) => {
  const {request, read} = await setup(t);
  const before = await read();
  const real = saves.mutateSave;
  t.mock.method(saves, "mutateSave", (env, uid, source, receipt, mutate, finalize) =>
    real(env, uid, source, receipt, async (...args) => { await mutate(...args); throw new Error("injected abort"); }, finalize));
  await assert.rejects(() => claimAchievement.run(request()), /injected abort/);
  assert.deepEqual(await read(), before);
});

test("existing album ownership backfills once without inventing historical battle progress", async (t) => {
  const {request, read} = await setup(t, {});
  const a = await getAchievements.run(request());
  const b = await getAchievements.run(request());
  assert.deepEqual(a, b); assert.equal(a.achievements.progress.CompleteAlbum, 1);
  assert.equal(a.achievements.progress.WinBattle, undefined); assert.equal(a.achievements.revision, 2);
  await claimAchievement.run(request("album.1"));
  assert.equal((await read())[1].balances.Shard, 3);
});

test("auth/env isolation and unavailable definitions cannot award rewards", async (t) => {
  const {request, read} = await setup(t);
  await assert.rejects(() => claimAchievement.run({data: request().data}), (error) => error.code === "unauthenticated");
  await assert.rejects(() => getAchievements.run({auth: request().auth, data: {env: "other"}}),
    (error) => error.code === "invalid-argument");
  await assert.rejects(() => claimAchievement.run({auth: {uid: "other"}, data: request().data}),
    (error) => error.code === "failed-precondition");
  t.mock.method(specs, "readSpecRows", async (_env, name) => {
    if (name === "Achievement") throw new Error("not published");
    return table(name);
  });
  await assert.rejects(() => getAchievements.run(request()), (error) => error.code === "unavailable");
  assert.equal((await read())[1].balances.Gold, 100);
});

test("successful pack commits its counter once; rejected packs change no achievements", async (t) => {
  const {request, root, read} = await setup(t, {});
  const input = request(); input.data.packId = "pack";
  const first = await openPack.run(input);
  await openPack.run(input);
  assert.equal(first.achievements.progress.OpenPack, 1);
  assert.equal((await read())[2].progress.OpenPack, 1);
  await root.collection("wallet").doc("current").update({"balances.Gold": 0});
  await assert.rejects(() => openPack.run({...input, data: {...input.data, txId: randomUUID()}}),
    (error) => error.details?.reason === "InsufficientGold");
  assert.equal((await read())[2].progress.OpenPack, 1);
});

test("all reward-pack commands count each opened pack once and direct card grants count zero", async (t) => {
  const {uid, read} = await setup(t, {});
  for (const source of ["claimAttendance", "claimMission", "claimReward", "claimPassReward", "claimBattleExperience", "spinRoulette"]) {
    const receipt = {kind: "client", txId: randomUUID()};
    const execute = (packs) => saves.mutateSave("test", uid, source, receipt, () => ({slots: {}}),
      (adopted) => ({...adopted, cards: [{cardId: 1}], packs}));
    const result = await execute([{packId: "pack", cards: [{cardId: 1}]}, {packId: "pack", cards: [{cardId: 2}]}]);
    await execute([{packId: "pack", cards: [{cardId: 1}]}, {packId: "pack", cards: [{cardId: 2}]}]);
    const before = (await read())[2];
    assert.equal(result.achievements.progress.OpenPack, before.progress.OpenPack);
    const direct = await saves.mutateSave("test", uid, source, {kind: "client", txId: randomUUID()},
      () => ({slots: {}}), (adopted) => ({...adopted, cards: [{cardId: 3}], packs: []}));
    assert.equal(direct.achievements, undefined);
    assert.deepEqual((await read())[2], before);
  }
  assert.equal((await read())[2].progress.OpenPack, 12);
});

test("reward-pack finalization failure aborts the save and achievement counter together", async (t) => {
  const {uid, read} = await setup(t, {});
  const before = await read();
  await assert.rejects(() => saves.mutateSave("test", uid, "claimReward", {kind: "client", txId: randomUUID()},
    () => ({slots: {}}), () => { throw new Error("injected reward failure"); }), /injected reward failure/);
  assert.deepEqual(await read(), before);
});

async function match(t, setupResult, verdict = "ok") {
  const {uid} = setupResult;
  const matchId = randomUUID().replace(/-/g, "");
  const fingerprint = "a".repeat(64), hash = "a".repeat(16);
  const deck = cards.map(({id}) => ({cardId: id, level: 3, hpBonus: 0, evolutionStage: 1,
    unlockedKeywords: 0, synergyUnlocked: true}));
  const growth = cards.map(({id}) => ({cardId: id, level: 3, limitBreak: 0}));
  const order = cards.map(({id}) => id);
  const deckHash = computeDeckHash(deck);
  const specPins = Object.fromEntries(["Card", "SynergyDef", "SynergyTierDef", "SynergyEffectDef"].map((name) =>
    [name, {blobPath: `envs/test/specs/${name}/releases/test`, payloadHash: hash}]));
  await db.doc(`envs/test/matches/${matchId}`).set({status: "pending", phase: "locked", lockStatus: "approved",
    seedSource: "server", seedHex: hash, rulesetVersion: 2, participantUids: [uid], mode: "solo",
    expectedParticipants: 1, resultProtocol: 1, adventureNodeId: "node_01", aiGrowthVersion: 1,
    aiDeck: {cardIds: order, cardGrowth: growth, snapshots: deck}, cardDataVersion: fingerprint, specPins,
    approvals: {[uid]: {ownerIndex: 0, cardSnapshots: deck, deckHash}},
    serverBoardOrders: {owner0: order, owner1: order}});
  t.mock.method(replayConfig, "isBattleReplayEnabled", async () => true);
  t.mock.method(replay, "callBattleReplay", async () => verdict === "ok" ? {kind: "ok", outcome: {
    firstOwner: 0, winnerOwner: 0, draw: false, remaining: [2, 0], destroyedByOwner: [6, 4],
    finalStateHash: hash, drawCount: 3, stats: {attacksByOwner: [7, 5], damageDealtByOwner: [20, 10],
      healedByOwner: [0, 0], synergyFiredByOwner: [0, 0], keywordsByOwner: [{}, {}], turns: 3},
  }} : {kind: verdict, reason: "injected_replay_failure"});
  return {auth: {uid}, data: {env: "test", matchId, seedSource: "server", myDeckHash: deckHash,
    opponentDeckHash: deckHash, finalStateHash: hash, stateHashChain: hash, stateHashChainPrev: hash,
    stateHashChainLength: 1, contentFingerprint: fingerprint, won: true, myRemaining: 2, opponentRemaining: 0,
    rankPointsBefore: 0, commandLogVersion: 1, commandLog: "", commandCount: 0,
    commandLogHash: createHash("sha256").update("").digest("hex"), commandLogTruncated: false,
    boardOrder: [order, order], endStateHash: hash}};
}

test("real settlement uses verified kills and starting active synergy, duplicate submission accrues once", async (t) => {
  const state = await setup(t, {});
  const input = await match(t, state);
  const first = await submitMatchResult.run(input);
  assert.equal(first.status, "confirmed", JSON.stringify(first));
  await submitMatchResult.run(input);
  const saved = (await state.read())[2];
  assert.deepEqual(saved.progress, {WinBattle: 1, DestroyCards: 6, WinStreak: 1, "PlaySynergy:Bulk": 1});
  assert.equal(saved.revision, 2);
});

test("unavailable replay leaves pending and invalid replay flags without any achievement mutation", async (t) => {
  for (const verdict of ["unavailable", "failed"]) {
    const state = await setup(t, {WinStreak: 3});
    const before = (await state.read())[2];
    const input = await match(t, state, verdict);
    const result = await submitMatchResult.run(input);
    assert.equal(result.status, verdict === "unavailable" ? "pending" : "flagged", JSON.stringify(result));
    assert.deepEqual((await state.read())[2], before);
  }
});

