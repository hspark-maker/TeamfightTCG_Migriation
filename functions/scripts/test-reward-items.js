const assert = require("node:assert/strict");
const {resolveRewards, judgeRewardClaim} = require("../lib/rewardTable");
const {grantRewardItems, duplicateGains, unlockedRewardPacks} = require("../lib/rewards/itemGrant");
const {evaluateGuideProgress} = require("../lib/missions/guideProgress");
const {judgeMissionClaim} = require("../lib/missions/judgeMissionClaim");
const {applyPeriodReset} = require("../lib/missions/missionStore");
const {missionPeriod} = require("../lib/missions/period");
const {catalog} = require("./mission-fixture");
const {requireSnackGrowthCurve} = require("../lib/growth/snackGrowthSpec");

const row = (type, id, amount, order = 1, ownerType = "Pass", ownerId = "S1:3") =>
  ({id: order, ownerType, ownerId, order, rewardType: type, rewardId: id, amount});
const rewardRows = [row("Currency", "Gold", 20), row("Pack", "testPack", 2, 2)];
const resolution = resolveRewards(rewardRows, "Pass", "S1:3");
assert.deepEqual(resolution.gains, [{currency: "Gold", amount: 20}]);
assert.deepEqual(resolution.items, [{rewardType: "Pack", rewardId: "testPack", amount: 2}]);
assert.equal(judgeRewardClaim([rewardRows[1]], "Pass", "S1:3").allow, true, "pack-only rewards are claimable");
const duplicates = [row("Currency", "Shard", 2, 1, "CardDuplicate", "Rare")];
const pack = {packId: "testPack", price: 100, priceType: "Gold", priceAuthored: true,
  drawCount: 2, uniqueDraw: false, minRankGrade: "Bronze", refundType: "Shard", refundAmount: 0};
const context = {catalog: new Set([1]), grades: new Map([[1, "Rare"]]), thresholds: [0, 100, 200, 300, 400],
  cards: [{id: 1, grade: "Rare"}],
  snackGrowthCurve: requireSnackGrowthCurve([{maxLevel: 4, maxLimitBreak: 1}],
    [{stage: 1, hpGain: 10, snackCost: 100}]),
  choices: ["testPack"], packs: new Map([["testPack", {pack, drops: [
    {id: 1, packId: "testPack", minGrade: "Bronze", cardId: 1, weight: 1},
  ]}]])};
const current = {ownership: {cardIds: []}, cardGrowth: {entries: {}}};
const granted = grantRewardItems(current, resolution.items, context, duplicates, "", 0, () => 0);
assert.deepEqual(granted.slots.ownership.cardIds, [1]);
assert.deepEqual(granted.cards.map((c) => c.isNew), [true, false, false, false]);
assert.equal(granted.slots.cardGrowth.entries["1"].snack, 3, "duplicates across packs retain snack");
assert.equal(granted.currencies.reduce((n, g) => n + g.amount, 0), 6, "three duplicates award authored shards");
assert.deepEqual(current.ownership.cardIds, [], "calculation must not mutate input on transaction retry");
assert.deepEqual(grantRewardItems(current, resolution.items, context, duplicates, "", 0, () => 0), granted);
pack.price = 0;
assert.deepEqual(grantRewardItems(current, resolution.items, context, [], "", 0, () => 0).currencies, [],
  "zero-price tutorial packs never award shards");
assert.equal(duplicateGains([{cardId: 1, isNew: true, snack: 0}], context.grades, duplicates).length, 0);
assert.throws(() => duplicateGains([{cardId: 1, isNew: false, snack: 1}], context.grades, []));
assert.deepEqual(unlockedRewardPacks(context, -1), []);
const choice = [{rewardType: "PackChoice", rewardId: "UnlockedThemePack", amount: 1}];
assert.throws(() => grantRewardItems(current, choice, context, duplicates, "notAllowed", 0));
assert.equal(grantRewardItems(current, choice, context, duplicates, "testPack", 0, () => 0).cards.length, 2);
assert.throws(() => grantRewardItems(current, [{rewardType: "Card", rewardId: "999", amount: 1}], context, [], "", 0));
assert.equal(grantRewardItems(current, [{rewardType: "Card", rewardId: "1", amount: 1}], context, [], "", 0).cards[0].isNew, true);
assert.deepEqual(grantRewardItems({ownership: {cardIds: [1]}},
  [{rewardType: "Card", rewardId: "1", amount: 1}], context, duplicates, "", 0).currencies,
[{currency: "Shard", amount: 2}], "direct duplicate cards also receive authored shards");

// Pack boundaries follow actual draws, including unique pools smaller than drawCount.
const revealContext = {...context, catalog: new Set([1, 2, 3]),
  grades: new Map([[1, "Rare"], [2, "Rare"], [3, "Rare"]]),
  choices: ["shortPack"], packs: new Map([
    ["testPack", {pack: {...pack, price: 100}, drops: [
      {id: 1, packId: "testPack", minGrade: "Bronze", cardId: 1, weight: 1},
      {id: 2, packId: "testPack", minGrade: "Bronze", cardId: 2, weight: 1},
    ]}],
    ["shortPack", {pack: {...pack, packId: "shortPack", drawCount: 5, uniqueDraw: true}, drops: [
      {id: 1, packId: "shortPack", minGrade: "Bronze", cardId: 3, weight: 1},
    ]}],
  ])};
const rolls = [0, 1, 1, 0, 0, 0];
let rollCount = 0;
const mixed = grantRewardItems(current, [
  {rewardType: "Card", rewardId: "2", amount: 1},
  {rewardType: "Pack", rewardId: "testPack", amount: 2},
  {rewardType: "Card", rewardId: "3", amount: 1},
  {rewardType: "PackChoice", rewardId: "UnlockedThemePack", amount: 2},
], revealContext, duplicates, "shortPack", 0, (max) => {
  assert.ok(rollCount < rolls.length, "metadata must not draw cards again");
  const value = rolls[rollCount++];
  assert.ok(value < max);
  return value;
});
assert.equal(rollCount, 6, "exactly one roll per actual pack card; direct cards consume none");
assert.deepEqual(mixed.packs, [
  {packId: "testPack", cards: [
    {cardId: 1, isNew: true, snack: 0}, {cardId: 2, isNew: false, snack: 1},
  ]},
  {packId: "testPack", cards: [
    {cardId: 2, isNew: false, snack: 1}, {cardId: 1, isNew: false, snack: 1},
  ]},
  {packId: "shortPack", cards: [{cardId: 3, isNew: false, snack: 1}]},
  {packId: "shortPack", cards: [{cardId: 3, isNew: false, snack: 1}]},
], "each pack keeps its actual cards and the resolved choice ID");
assert.deepEqual(mixed.cards.map((card) => card.cardId), [2, 1, 2, 2, 1, 3, 3, 3],
  "flat cards preserve item order and include direct grants exactly once");
const packCardIndices = [1, 2, 3, 4, 6, 7];
mixed.packs.flatMap((entry) => entry.cards).forEach((card, index) => {
  assert.strictEqual(card, mixed.cards[packCardIndices[index]], "metadata reuses the granted draw");
});
assert.deepEqual(mixed.slots.ownership.cardIds, [2, 1, 3]);
assert.deepEqual([1, 2, 3].map((id) => mixed.slots.cardGrowth.entries[id].snack), [1, 2, 2]);
assert.deepEqual(mixed.currencies, Array(3).fill({currency: "Shard", amount: 2}),
  "metadata adds no duplicate currency grants; free packs still grant only snacks");
assert.deepEqual(current, {ownership: {cardIds: []}, cardGrowth: {entries: {}}});
const noRoll = () => assert.fail("direct or empty rewards must not draw packs");
assert.deepEqual(grantRewardItems(current, [{rewardType: "Card", rewardId: "1", amount: 2}],
  context, duplicates, "", 0, noRoll).packs, [], "direct cards have no reveal packs");
assert.deepEqual(grantRewardItems(current, [], context, [], "", 0, noRoll),
  {slots: {}, cards: [], packs: [], currencies: []});

const ids = [1, 2, 3, 4, 8, 9];
const guideSave = {ownership: {cardIds: ids}, deck: {selectedSlot: 0, slots: [{cardIds: ids}]},
  cardGrowth: {entries: Object.fromEntries(ids.map((id) => [id, {level: 4, snack: 0, limitBreak: 0}]))},
  adventure: {clearedNodeIds: ["node_01", "node_02", "node_03", "node_04", "node_05", "node_06"]}};
const cardSpecs = ids.map((id) => ({id, synergies: id <= 3 ? "Data_Synergy_Caretaker" : "",
  name: id === 8 ? "Data_Card_Nightchestnut" : id === 9 ? "Data_Card_MushroomCat" : ""}));
const progress = evaluateGuideProgress(guideSave, cardSpecs, catalog);
assert.equal(progress["guide.Guide.DeckSaved6"], 1);
assert.equal(progress["guide.Guide.CaretakerTraceDeck"], 2);
assert.equal(progress["guide.Guide.DeckCardsAtStar3"], 6);
assert.equal(evaluateGuideProgress({}, cardSpecs, catalog, progress)["guide.Guide.CaretakerTraceDeck"], 2,
  "completed locked guide survives deck changes");
const state = {dailyKey: "old", weeklyKey: "old", progress, claimed: {}, passExp: 0};
assert.equal(judgeMissionClaim("guide.02", state, catalog).allow, false, "future guide cannot be claimed early");
assert.equal(judgeMissionClaim("guide.01", state, catalog).allow, true);
state.claimed["guide.01"] = true;
assert.equal(judgeMissionClaim("guide.02", state, catalog).allow, true);
const reset = applyPeriodReset(state, missionPeriod(Date.now()));
assert.equal(reset.claimed["guide.01"], true, "daily/weekly reset must preserve guide claims");
assert.equal(reset.progress["guide.Guide.DeckSaved6"], 1);
console.log("reward items and guide progress: ok");
