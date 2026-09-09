const assert = require("node:assert/strict");
if (!process.env.FIRESTORE_EMULATOR_HOST) throw new Error("Emulator required; never run against a real account.");
const {db} = require("../lib/firebaseApp");
const {ensureSaveDocument, saveDocument} = require("../lib/save/saveDocument");
const {mutateWallet} = require("../lib/currency/walletTransaction");
const {nextWallet, walletRef} = require("../lib/currency/walletStore");
const {grant, spend} = require("../lib/currency/wallet");
const {buildFreshAccountBalances, buildFreshAccountSlots} = require("../lib/save/freshAccount");
const env = "test";
const uid = "roulette-integration-test";
const save = saveDocument(env, uid);
const wallet = walletRef(db, env, uid);
const spec = require("../lib/roulette/rouletteSpecReader");
spec.readRouletteHeaderRow = async () => ({rouletteId: "board", priceType: "RouletteTicket", price: 1});
spec.readRouletteSlotRows = async () => Array.from({length: 8}, (_, i) => ({
  id: i + 1, rouletteId: "board", slotIndex: i, rewardType: i === 7 ? "Pack" : "Currency",
  rewardId: i === 7 ? "testPack" : "Gold", amount: i === 7 ? 1 : 10, weight: 1,
}));
require("../lib/rewards/itemGrant").loadItemGrantContext = async () => ({
  catalog: new Set([1]), grades: new Map([[1, "Rare"]]), thresholds: [0, 100, 200, 300, 400], choices: [],
  packs: new Map([["testPack", {pack: {packId: "testPack", price: 100, drawCount: 2, uniqueDraw: false},
    drops: [{id: 1, packId: "testPack", minGrade: "Bronze", cardId: 1, weight: 1}]}]]),
});
require("../lib/specs/specBlobReader").readSpecRows = async () => [
  {id: 1, ownerType: "CardDuplicate", ownerId: "Rare", order: 1, rewardType: "Currency", rewardId: "Shard", amount: 2},
];
require("../lib/observability/analyticsEvent").recordEvent = () => {};
const crypto = require("node:crypto");
const randomInt = crypto.randomInt;
crypto.randomInt = max => max - 1;
const {spinRoulette} = require("../lib/commands/spinRoulette");
const call = txId => spinRoulette.run({auth: {uid}, data: {env, rouletteId: "board", txId}});
const readState = async () => ({save: (await save.get()).data(), wallet: (await wallet.get()).data()});

async function main() {
  await db.recursiveDelete(db.doc(`envs/${env}/users/${uid}`));
  await ensureSaveDocument(env, uid, "0123456789abcdef0123456789abcdef", "test",
    () => buildFreshAccountSlots([1, 28, 20, 6, 11, 30]), {...buildFreshAccountBalances(), RouletteTicket: 3});
  // Replay an actual pre-upgrade currency receipt through the new callable.
  const old = await mutateWallet(env, uid, "spinRoulette", {kind: "client", txId: "roulette-legacy-0001"},
    current => nextWallet(current, grant(spend(current.balances, "RouletteTicket", 1), [{currency: "Gold", amount: 10}]), "spinRoulette"),
    patch => ({rouletteId: "board", slotIndex: 0, gain: {currency: "Gold", amount: 10}, wallet: patch}));
  let before = await readState();
  assert.deepEqual(await call("roulette-legacy-0001"), old);
  assert.deepEqual(await readState(), before);
  // A pack consumes one ticket and awards two duplicates exactly once.
  const first = await call("roulette-pack-0002");
  assert.equal(first.rewardType, "Pack");
  assert.equal(first.cards.length, 2);
  assert.equal(first.wallet.balances.RouletteTicket, 1);
  assert.equal(first.wallet.balances.Shard, before.wallet.balances.Shard + 4);
  assert.equal(first.updatedSlots.cardGrowth.entries["1"].snack,
    (before.save.cardGrowth.entries["1"]?.snack || 0) + 2);
  before = await readState();
  assert.deepEqual(await call("roulette-pack-0002"), first);
  assert.deepEqual(await readState(), before);
  // Last ticket grants currency. No ticket remains for another transaction.
  crypto.randomInt = () => 0;
  const currency = await call("roulette-currency-0003");
  assert.equal(currency.rewardType, "Currency");
  assert.equal(currency.gain.amount, 10);
  before = await readState();
  await assert.rejects(call("roulette-empty-0004"), error => error.message.startsWith("InsufficientTicket"));
  assert.deepEqual(await readState(), before);
  await db.recursiveDelete(db.doc(`envs/${env}/users/${uid}`));
  console.log("spin roulette: legacy replay, pack/currency grants, duplicate replay and insufficient ticket atomicity OK");
}
main().catch(error => {console.error(error); process.exitCode = 1;}).finally(() => {crypto.randomInt = randomInt;});
