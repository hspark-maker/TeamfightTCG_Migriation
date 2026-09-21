"use strict";
const assert = require("node:assert/strict");
const {test, after} = require("node:test");
const {randomUUID, createHash} = require("node:crypto");
if (!/^(127\.0\.0\.1|localhost):\d+$/.test(process.env.FIRESTORE_EMULATOR_HOST ?? "") ||
    !String(process.env.GCLOUD_PROJECT ?? "").startsWith("demo-")) {
  throw new Error("A local Firestore emulator and demo-* project are required.");
}
const {db} = require("../lib/firebaseApp");
const specs = require("../lib/specs/specBlobReader");
const replay = require("../lib/battleReplayService");
const config = require("../lib/battleReplayConfig");
const transactions = require("../lib/observability/countedTransaction");
const {submitMatchResult} = require("../lib/commands/submitMatchResult");
const {computeDeckHash, parseCardSpecRow, buildAiDeckSnapshots} = require("../lib/deckValidation");
after(() => db.terminate());

const hash = "a".repeat(16), fingerprint = "a".repeat(64);
const cards = Array.from({length: 6}, (_, i) => ({id: i + 1, channel: "Live", grade: "Common", maxHp: 5,
  keywords: 0, keywordUnlockLevel: 1, hp2: 6, hp3: 7, hp4: 8, synergies: "Data_Synergy_Bulk"}));
const order = cards.map((card) => card.id);
const deck = buildAiDeckSnapshots(order, 3, new Map(cards.map((card) => [card.id, parseCardSpecRow(card)])));
const deckHash = computeDeckHash(deck);
const specPins = Object.fromEntries(["Card", "SynergyDef", "SynergyTierDef", "SynergyEffectDef"].map((name) =>
  [name, {blobPath: `envs/test/specs/${name}/releases/efficiency`, payloadHash: hash}]));
const outcome = {firstOwner: 0, winnerOwner: 0, draw: false, remaining: [2, 0], destroyedByOwner: [6, 4],
  finalStateHash: hash, drawCount: 3, stats: {attacksByOwner: [7, 5], damageDealtByOwner: [20, 10],
    healedByOwner: [0, 0], synergyFiredByOwner: [0, 0], keywordsByOwner: [{}, {}], turns: 3}};

async function fixture(kind = "new") {
  const uid = "result-efficiency-" + randomUUID(), opponent = uid + "-other";
  const matchId = randomUUID().replace(/-/g, "");
  const ref = db.doc(`envs/test/matches/${matchId}`);
  const participants = kind === "pvp" ? [uid, opponent] : [uid];
  const match = {status: "pending", phase: "locked", lockStatus: "approved", seedSource: "server",
    seedHex: hash, rulesetVersion: 2, participantUids: participants, mode: kind === "pvp" ? "pvp" : "solo",
    expectedParticipants: participants.length, resultProtocol: 1,
    ...(kind === "adventure" ? {adventureNodeId: "efficiency-node"} : {}),
    ...(kind === "old" ? {} : {aiGrowthVersion: 1}),
    aiDeck: {cardIds: order, cardLevel: 3,
      ...(kind === "old" ? {} : {cardGrowth: order.map((cardId) => ({cardId, level: 3})), snapshots: deck})},
    cardDataVersion: fingerprint, specPins,
    approvals: Object.fromEntries(participants.map((id, index) =>
      [id, {ownerIndex: index, cardSnapshots: deck, deckHash}])),
    serverBoardOrders: {owner0: order, owner1: order}};
  await ref.set(match);
  await Promise.all(participants.map((id) => db.doc(`envs/test/users/${id}/save/current`).set({
    schemaVersion: 8, revision: 1, ownership: {cardIds: order}, rank: {points: 0}})));
  const request = (who = uid) => ({auth: {uid: who}, data: {env: "test", matchId, seedSource: "server",
    myDeckHash: deckHash, opponentDeckHash: deckHash, finalStateHash: hash, stateHashChain: hash,
    stateHashChainPrev: hash, stateHashChainLength: 1, contentFingerprint: fingerprint, won: who === uid,
    myRemaining: who === uid ? 2 : 0, opponentRemaining: who === uid ? 0 : 2, rankPointsBefore: 0,
    commandLogVersion: 1, commandLog: "", commandCount: 0,
    commandLogHash: createHash("sha256").update("").digest("hex"), commandLogTruncated: false,
    boardOrder: [order, order], endStateHash: hash}});
  return {uid, opponent, ref, request, match};
}

function mockSettlement(t, allowCurrentCard = false) {
  const currentReads = [], pinnedReads = [];
  t.mock.method(config, "isBattleReplayEnabled", async () => true);
  t.mock.method(specs, "readSpecRows", async (_env, name) => {
    currentReads.push(name);
    if (name === "Card") {
      assert.equal(allowCurrentCard, true, "new contracts must not request the current Card table");
      return cards;
    }
    if (name === "PassSeason") return [{id: 1, seasonId: "efficiency", displayName: "Test",
      startAtMs: 1, endAtMs: 4102444800000, maxLevel: 1}];
    if (name === "RankGrade") return [{id: 1, entryPoints: 0, pointsPerDivision: 10, winPoints: 2, losePoints: 1}];
    if (name === "Reward") return ["win.perCard", "win.floor", "lose.flat"].map((ownerId, i) =>
      ({id: i + 1, ownerType: "Battle", ownerId, order: 0, rewardType: "Currency", rewardId: "Gold", amount: 2}));
    throw new Error("Unexpected current table: " + name);
  });
  t.mock.method(specs, "readPinnedSpecRows", async (_env, name) => {
    pinnedReads.push(name);
    if (name === "Card") return cards;
    if (name === "SynergyTierDef") return [{id: 1, synergyId: "Bulk", requiredCount: 2}];
    throw new Error("Unexpected pinned table: " + name);
  });
  const replayMock = t.mock.method(replay, "callBattleReplay", async () => ({kind: "ok", outcome}));
  return {currentReads, pinnedReads, replayMock};
}

function forbidSettlementWork(t) {
  const mocks = [t.mock.method(config, "isBattleReplayEnabled", async () => { throw new Error("config must be skipped"); }),
    t.mock.method(specs, "readSpecRows", async () => { throw new Error("current specs must be skipped"); }),
    t.mock.method(specs, "readPinnedSpecRows", async () => { throw new Error("pinned specs must be skipped"); }),
    t.mock.method(transactions, "withCountedTransaction", async () => { throw new Error("transaction must be skipped"); }),
    t.mock.method(replay, "callBattleReplay", async () => { throw new Error("replay must be skipped"); })];
  return () => mocks.forEach((mock) => assert.equal(mock.mock.callCount(), 0));
}

for (const status of ["confirmed", "flagged"]) {
  test(`${status} retries return stored results without specs/config/transaction even during dependency outage`, async (t) => {
    const {ref, request} = await fixture();
    await ref.update({status, reason: status === "flagged" ? "bad-replay" : null});
    const before = await ref.get();
    const assertSkipped = forbidSettlementWork(t);
    assert.deepEqual(await submitMatchResult.run(request()), {status, reason: before.data().reason});
    assertSkipped();
    assert.equal((await ref.get()).updateTime.isEqual(before.updateTime), true);
  });
}

test("terminal adventure retry preserves recorded win/draw and does not trust incoming won", async (t) => {
  const {ref, request} = await fixture("adventure");
  await ref.update({status: "confirmed", serverSimulation: {ok: true, winnerOwner: 1, draw: false}});
  const assertSkipped = forbidSettlementWork(t);
  assert.deepEqual(await submitMatchResult.run(request()), {status: "confirmed", reason: null,
    adventureNodeId: "efficiency-node", won: false, draw: false});
  await ref.update({"serverSimulation.draw": true});
  assert.equal((await submitMatchResult.run(request())).draw, true);
  assertSkipped();
});

test("terminal shortcut rejects nonparticipant, foreign environment, nonserver identity and missing auth", async (t) => {
  const {ref, request} = await fixture();
  await ref.update({status: "confirmed"});
  const assertSkipped = forbidSettlementWork(t);
  await assert.rejects(() => submitMatchResult.run(request("stranger")), (e) => e.code === "permission-denied");
  const foreign = request(); foreign.data.env = "live";
  await assert.rejects(() => submitMatchResult.run(foreign), (e) => e.code === "permission-denied");
  await assert.rejects(() => submitMatchResult.run({data: request().data}), (e) => e.code === "unauthenticated");
  await ref.update({seedSource: "commit_reveal"});
  await assert.rejects(() => submitMatchResult.run(request()), (e) => e.code === "permission-denied");
  assertSkipped();
});

for (const kind of ["new", "old", "adventure", "pvp"]) {
  test(`${kind} pending settlement preserves replay, rewards and statistics; current Card only for old solo`, async (t) => {
    const {uid, opponent, ref, request} = await fixture(kind);
    const {currentReads, pinnedReads, replayMock} = mockSettlement(t, kind === "old");
    const first = await submitMatchResult.run(request());
    if (kind === "pvp") {
      assert.equal(first.status, "pending");
      assert.equal((await submitMatchResult.run(request(opponent))).status, "confirmed");
    } else assert.equal(first.status, "confirmed", JSON.stringify(first));
    assert.equal((await ref.get()).data().status, "confirmed");
    assert.equal(currentReads.filter((name) => name === "Card").length, kind === "old" ? 1 : 0);
    assert(pinnedReads.includes("Card")); assert(pinnedReads.includes("SynergyTierDef"));
    assert.equal(replayMock.mock.callCount(), 1);
    const statsRef = db.doc(`envs/test/users/${uid}/statistics/current`);
    const stats = await statsRef.get();
    assert.equal(stats.data().battle.all.wins, 1);
    assert.equal(stats.data().battle[kind === "adventure" ? "adventure" : "ranked"].wins, 1);
    assert.equal(stats.data().lifetime.synergyPlays.Bulk, 1);
    const payouts = await db.collection(`envs/test/users/${uid}/payouts`).get();
    assert.equal(payouts.size, kind === "adventure" ? 0 : 1);
    if (payouts.size) assert.equal(payouts.docs[0].data().currency.amount, 4);
    if (kind === "pvp") {
      assert.equal((await db.doc(`envs/test/users/${opponent}/statistics/current`).get()).data().battle.all.losses, 1);
    }
    const assertSkipped = forbidSettlementWork(t);
    assert.equal((await submitMatchResult.run(request())).status, "confirmed");
    assertSkipped();
    assert.equal((await statsRef.get()).updateTime.isEqual(stats.updateTime), true);
  });
}

test("a changed AI contract between initial read and transaction retries without committing an invalid result", async (t) => {
  const {ref, request} = await fixture("new");
  mockSettlement(t);
  const original = transactions.withCountedTransaction;
  t.mock.method(transactions, "withCountedTransaction", async (...args) => {
    const {FieldValue} = require("firebase-admin/firestore");
    await ref.update({aiGrowthVersion: FieldValue.delete()});
    return original(...args);
  });
  await assert.rejects(() => submitMatchResult.run(request()), (e) => e.code === "unavailable");
  assert.equal((await ref.get()).data().status, "pending");
});
