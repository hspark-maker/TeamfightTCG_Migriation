"use strict";

const assert = require("node:assert/strict");
const {test} = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const {csv} = require("./publish-local-spec");
const {CURRENCY_KEYS, CURRENCY_MAX} = require("../lib/currency/currencyKeys");
const wallet = require("../lib/currency/wallet");
const currencyWallet = require("../../functions-currency/lib/generated/currency/wallet");
const {duplicateGains, grantRewardItems} = require("../lib/rewards/itemGrant");
const {drawPack} = require("../lib/packs/packDraw");

const matrix = csv(fs.readFileSync(path.resolve(__dirname, "../../docs/SpecData/Reward_sheet.csv"), "utf8"));
const header = matrix.findIndex((row) => row[0] === "id");
assert(header >= 0);
const rewards = matrix.slice(header + 2).filter((row) => row[0].trim()).map((row) =>
  Object.fromEntries(matrix[header].map((key, index) =>
    [key, ["id", "order", "amount"].includes(key) ? Number(row[index]) : row[index]])));
const grades = new Map([[1, "Common"], [2, "Rare"], [3, "Arcane"], [4, "Mythic"]]);
const context = {catalog: new Set(grades.keys()), grades, thresholds: [0],
  packs: new Map(), choices: [], cards: []};

test("legacy wallets start with zero CardDust without converting existing Shard", () => {
  const before = {Gold: 100, Shard: 230, RouletteTicket: 3};
  for (const implementation of [wallet, currencyWallet]) {
    const result = implementation.normalizeBalances(before);
    assert.equal(result.CardDust, 0);
    assert.equal(result.Shard, 230);
    assert.deepEqual(Object.keys(result), [...CURRENCY_KEYS]);
  }
  assert.deepEqual(before, {Gold: 100, Shard: 230, RouletteTicket: 3});
});

test("both codebases preserve CardDust through unrelated wallet operations", () => {
  const before = {Gold: 100, Shard: 230, CardDust: 80};
  for (const implementation of [wallet, currencyWallet]) {
    const granted = implementation.grant(before, [{currency: "Gold", amount: 20}]);
    assert.equal(granted.CardDust, 80);
    const spent = implementation.spend(granted, "Gold", 10);
    assert.equal(spent.CardDust, 80);
    assert.equal(spent.Gold, 110);
    assert.equal(spent.Shard, 230);
    assert.equal(implementation.canAfford(spent, "CardDust", 81), false);
    assert.equal(implementation.spend(spent, "CardDust", 80).CardDust, 0);
  }
});

test("CardDust keeps the shared wallet bounds", () => {
  for (const implementation of [wallet, currencyWallet]) {
    assert.equal(implementation.normalizeBalances({CardDust: -5}).CardDust, 0);
    assert.equal(implementation.grant({CardDust: CURRENCY_MAX - 1},
      [{currency: "CardDust", amount: 5}]).CardDust, CURRENCY_MAX);
  }
});

test("authored duplicate rewards pay CardDust by grade and never enhancement Shard", () => {
  const gains = duplicateGains([...grades.keys()].map((cardId) => ({cardId, isNew: false})), grades, rewards);
  assert.deepEqual(gains, [1, 2, 5, 10].map((amount) => ({currency: "CardDust", amount})));
  assert.deepEqual(duplicateGains([{cardId: 4, isNew: true}], grades, rewards), []);
  const result = wallet.grant({Shard: 230}, gains);
  assert.equal(result.CardDust, 18);
  assert.equal(result.Shard, 230);
});

test("same-pack duplicates count only copies after the first acquisition", () => {
  const drawn = drawPack([{cardId: 1, weight: 1}], 3, false, context.catalog, new Set(), () => 0);
  assert.deepEqual(drawn.map((card) => card.isNew), [true, false, false]);
  assert.deepEqual(duplicateGains(drawn, grades, rewards),
    [{currency: "CardDust", amount: 1}, {currency: "CardDust", amount: 1}]);
});

test("direct duplicate card rewards retain growth and pay the authored crafting amount", () => {
  const before = {ownership: {cardIds: [3]}, cardGrowth: {entries: {3: {level: 3, shardProgress: 7}}}};
  const result = grantRewardItems(before, [{rewardType: "Card", rewardId: "3", amount: 2}],
    context, rewards, "", 0, () => 0);
  assert.deepEqual(result.currencies, [{currency: "CardDust", amount: 5}, {currency: "CardDust", amount: 5}]);
  assert.deepEqual(result.slots.cardGrowth, before.cardGrowth);
  assert.deepEqual(result.slots.ownership.cardIds, [3]);
});

test("reward packs use CardDust while zero-price tutorial packs remain excluded", () => {
  for (const price of [0, 100]) {
    const packContext = {...context, packs: new Map([["pack", {
      pack: {price, drawCount: 2, uniqueDraw: false},
      drops: [{id: 1, packId: "pack", minGrade: "Unranked", cardId: 2, weight: 1}],
    }]])};
    const result = grantRewardItems({ownership: {cardIds: [2]}, cardGrowth: {entries: {}}},
      [{rewardType: "Pack", rewardId: "pack", amount: 1}], packContext, rewards, "", 0, () => 0);
    assert.deepEqual(result.currencies, price === 0 ? [] :
      [{currency: "CardDust", amount: 2}, {currency: "CardDust", amount: 2}]);
  }
});
