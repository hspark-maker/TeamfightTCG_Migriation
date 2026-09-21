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
const {getPlayerStatistics} = require("../lib/commands/getPlayerStatistics");
const {readAchievements, writeAchievements} = require("../lib/achievements/achievementStore");
const {beginStatistics, commitStatistics} = require("../lib/statistics/playerStatisticsStore");
const {applyStatisticsBattle} = require("../lib/statistics/playerStatistics");
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
    const execute = (packs) => saves.mutateSave("test", uid, source, receipt, async (_current, _tx, _wallet, prepare) => {
      await prepare(packs.length);
      return {slots: {}};
    },
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
  const stats = (await state.root.collection("statistics").doc("current").get()).data();
  assert.equal(stats.lifetime.wins, 1); assert.equal(stats.lifetime.cardsDestroyed, 6);
  assert.equal(stats.lifetime.synergyPlays.Bulk, 1); assert.equal(stats.battle.all.battles, 1);
  assert.equal(stats.battle.adventure.wins, 1); assert.equal(stats.battle.ranked.battles, 0);
  assert.equal(stats.battle.all.attacks, 7); assert.equal(stats.battle.all.damageDealt, 20);
  assert.equal(stats.revision, 1);
});

test("unavailable replay leaves pending and invalid replay flags without any achievement mutation", async (t) => {
  for (const verdict of ["unavailable", "failed"]) {
    const state = await setup(t, {WinStreak: 3});
    const before = (await state.read())[2];
    const input = await match(t, state, verdict);
    const result = await submitMatchResult.run(input);
    assert.equal(result.status, verdict === "unavailable" ? "pending" : "flagged", JSON.stringify(result));
    assert.deepEqual((await state.read())[2], before);
    assert.equal((await state.root.collection("statistics").doc("current").get()).exists, false);
  }
});

test("statistics lazily migrate once, preserve claims and query without Achievement definitions", async (t) => {
  const {root, request, read} = await setup(t, {WinBattle: 17, DestroyCards: 33, OpenPack: 4, WinStreak: 8,
    "PlaySynergy:Bulk": 9});
  await root.collection("achievements").doc("current").update({currentWinStreak: 3, claimed: {"wins.1": true}});
  t.mock.method(specs, "readSpecRows", async (_env, name) => {
    assert.notEqual(name, "Achievement"); return table(name);
  });
  const first = await getPlayerStatistics.run(request());
  const second = await getPlayerStatistics.run(request());
  assert.deepEqual(second, first);
  assert.equal(first.statistics.revision, 1); assert.equal(first.statistics.achievementRevision, 2);
  assert.equal(first.statistics.lifetime.wins, 17); assert.equal(first.statistics.lifetime.albumsCompleted, 1);
  assert.equal(first.statistics.lifetime.currentWinStreak, 3); assert.equal(first.statistics.lifetime.bestWinStreak, 8);
  for (const bucket of Object.values(first.statistics.battle)) {
    assert.equal(bucket.battles, 0); assert.equal(bucket.wins, 0); assert.equal(bucket.losses, 0);
  }
  assert.equal(first.achievements.claimed["wins.1"], true);
  assert.equal((await read())[0].revision, 1);
});

test("mixed old/new transactions keep all increments and claims through conflicts and rollback", async (t) => {
  const {root, request, uid} = await setup(t, {WinBattle: 10, DestroyCards: 20, WinStreak: 5});
  await getPlayerStatistics.run(request());
  const oldRef = root.collection("achievements").doc("current");
  const oldWrite = () => db.runTransaction(async (tx) => {
    const old = readAchievements(await tx.get(oldRef));
    old.progress.WinBattle = (old.progress.WinBattle ?? 0) + 1;
    old.progress.OpenPack = (old.progress.OpenPack ?? 0) + 1;
    old.currentWinStreak = 0;
    old.claimed["wins.1"] = true;
    writeAchievements(tx, oldRef, old, Date.now());
  });
  const newWrite = () => db.runTransaction(async (tx) => {
    const context = await beginStatistics(tx, db, "test", uid);
    applyStatisticsBattle(context.state, {verified: true, tutorial: false, mode: "ranked", won: true, draw: false,
      destroyed: 3, attacks: 4, damageDealt: 20, healed: 2, synergyTriggers: 1, synergies: ["Bulk"]});
    commitStatistics(tx, context, Date.now());
  });
  await Promise.all([oldWrite(), newWrite(), oldWrite(), newWrite()]);
  const final = await getPlayerStatistics.run(request());
  assert.equal(final.statistics.lifetime.wins, 14); assert.equal(final.statistics.lifetime.cardsDestroyed, 26);
  assert.equal(final.statistics.lifetime.packsOpened, 2); assert.equal(final.statistics.battle.all.battles, 2);
  assert.equal(final.statistics.battle.ranked.wins, 2); assert.equal(final.achievements.claimed["wins.1"], true);
  assert.deepEqual(await getPlayerStatistics.run(request()), final);
});

test("old writer loss resets lifetime streak before new wins and does not invent a recorded defeat", async (t) => {
  const {root, request, uid} = await setup(t, {WinBattle: 10, WinStreak: 7});
  await root.collection("achievements").doc("current").update({currentWinStreak: 4});
  const before = await getPlayerStatistics.run(request());
  await root.collection("achievements").doc("current").update({currentWinStreak: 0, revision: before.achievements.revision + 1});
  await db.runTransaction(async (tx) => {
    const context = await beginStatistics(tx, db, "test", uid);
    applyStatisticsBattle(context.state, {verified: true, tutorial: false, mode: "adventure", won: true, draw: false,
      destroyed: 0, attacks: 0, damageDealt: 0, healed: 0, synergyTriggers: 0, synergies: []});
    commitStatistics(tx, context, Date.now());
  });
  const after = await getPlayerStatistics.run(request());
  assert.equal(after.statistics.lifetime.currentWinStreak, 1); assert.equal(after.statistics.lifetime.bestWinStreak, 7);
  assert.equal(after.statistics.battle.all.losses, 0); assert.equal(after.statistics.battle.all.wins, 1);
  assert.equal(after.statistics.trackedSinceMs, before.statistics.trackedSinceMs);
});

test("receipt replay after independent statistics refresh returns its old revision without mutation", async (t) => {
  const {root, request} = await setup(t, {});
  const pack = {auth: request().auth, data: {env: "test", packId: "pack", txId: randomUUID()}};
  const opened = await openPack.run(pack);
  assert.equal(opened.statistics.lifetime.packsOpened, 1);
  const ref = root.collection("statistics").doc("current");
  // Ownership-derived albums are resolved by the separate read endpoint, without a save revision bump.
  const refreshed = await getPlayerStatistics.run(request());
  assert.ok(refreshed.statistics.revision > opened.statistics.revision);
  const stored = (await ref.get()).data();
  const replayed = await openPack.run(pack);
  assert.equal(replayed.statistics.revision, opened.statistics.revision);
  assert.deepEqual((await ref.get()).data(), stored);
});

test("statistics queries require own authenticated existing account and known environment", async (t) => {
  const {request} = await setup(t);
  await assert.rejects(() => getPlayerStatistics.run({data: {env: "test"}}), {code: "unauthenticated"});
  await assert.rejects(() => getPlayerStatistics.run({...request(), data: {env: "unknown"}}), {code: "invalid-argument"});
  await assert.rejects(() => getPlayerStatistics.run({auth: {uid: randomUUID()}, data: {env: "test"}}),
    {code: "failed-precondition"});
  const actual = await getPlayerStatistics.run({...request(), data: {env: "test", uid: "someone-else"}});
  assert.equal(actual.statistics.lifetime.wins, 3);
});

test("normal solo AI settlement without adventureNodeId is counted as ranked", async (t) => {
  const state = await setup(t, {});
  const input = await match(t, state);
  const ref = db.doc(`envs/test/matches/${input.data.matchId}`);
  const stored = (await ref.get()).data();
  delete stored.adventureNodeId;
  await ref.set(stored);
  t.mock.method(specs, "readSpecRows", async (_env, name) => {
    if (name === "RankGrade") return [{id: 1, entryPoints: 0, pointsPerDivision: 10, winPoints: 2, losePoints: 1}];
    if (name === "PassSeason") return [{id: 1, seasonId: "test", displayName: "Test", startAtMs: 1,
      endAtMs: 4102444800000, maxLevel: 10}];
    if (name === "Reward") return ["win.perCard", "win.floor", "lose.flat"].map((ownerId, i) =>
      ({id: i + 1, ownerType: "Battle", ownerId, order: 1, rewardType: "Currency", rewardId: "Gold", amount: 1}));
    return table(name);
  });
  const result = await submitMatchResult.run(input);
  assert.equal(result.status, "confirmed", JSON.stringify(result));
  const stats = (await state.root.collection("statistics").doc("current").get()).data();
  assert.equal(stats.battle.ranked.battles, 1); assert.equal(stats.battle.ranked.wins, 1);
  assert.equal(stats.battle.adventure.battles, 0); assert.equal(stats.battle.all.battles, 1);
});
