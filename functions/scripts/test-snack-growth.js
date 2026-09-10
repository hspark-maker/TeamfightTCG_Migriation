const assert = require("node:assert/strict");
const {addSnackAndGrow, addSnack, applyLimitBreak, SNACK_MAX} = require("../lib/growth/cardGrowth");
const {requireSnackGrowthCurve} = require("../lib/growth/snackGrowthSpec");
const {applyDrawnSnackGrowth, grantRewardItems} = require("../lib/rewards/itemGrant");

const rules = [{maxLevel: 4, maxLimitBreak: 3}];
const rows = [
  {stage: 1, hpGain: 10, snackCost: 2},
  {stage: 2, hpGain: 20, snackCost: 3},
  {stage: 3, hpGain: 30, snackCost: 4},
];
const curve = requireSnackGrowthCurve(rules, rows);
const entry = (snack, limitBreak = 0) => ({level: 3, snack, limitBreak});
const meta = (fromStage, toStage, hpGain, snackCost, snackLeft) =>
  ({fromStage, toStage, hpGain, snackCost, snackLeft});

// Identical to repeated manual limitBreakCard arithmetic; level and other cards survive.
const before = {1: entry(8), 2: entry(70, 1)};
const original = structuredClone(before);
const grown = addSnackAndGrow(before, 1, 2, curve);
let manual = addSnack(before, 1, 2);
for (const step of curve.steps.values()) manual = applyLimitBreak(manual, 1, step.stage, step.snackCost);
assert.deepEqual(grown.entries, manual);
assert.deepEqual(grown.snackGrowth, meta(0, 3, 60, 9, 1));
assert.deepEqual(before, original, "retry input is immutable");
assert.deepEqual(addSnackAndGrow(before, 1, 2, curve), grown, "retry is deterministic");
assert.deepEqual(addSnackAndGrow({1: entry(0)}, 1, 1, curve), {entries: {1: entry(1)}});
assert.deepEqual(addSnackAndGrow({1: entry(1)}, 1, 1, curve).snackGrowth, meta(0, 1, 10, 2, 0));
assert.deepEqual(addSnackAndGrow({1: entry(2, 1)}, 1, 1, curve).snackGrowth, meta(1, 2, 20, 3, 0));
assert.deepEqual(addSnackAndGrow({1: entry(7, 3)}, 1, 1, curve), {entries: {1: entry(8, 3)}});
assert.deepEqual(addSnackAndGrow({1: entry(SNACK_MAX, 3)}, 1, 1, curve),
  {entries: {1: entry(SNACK_MAX, 3)}}, "maximum-stage snack retains the existing saturation rule");
assert.deepEqual(addSnackAndGrow(before, 1, 0, curve), {entries: before}, "new card snack=0 never grows stored snacks");
assert.deepEqual(addSnackAndGrow({}, 1, 0, curve), {entries: {}}, "new cards create no growth entry");
assert.equal(addSnackAndGrow({1: entry(-4, -1)}, 1, 2, curve).snackGrowth.toStage, 1);

// Metadata belongs to the draw crossing each threshold, not all copies of that card.
const draws = [1, 2, 1, 1, 1, 1].map(cardId => ({cardId, isNew: false, snack: 1}));
const after = applyDrawnSnackGrowth({1: entry(0), 2: entry(1)}, draws, curve);
assert.deepEqual(draws.map(card => card.snackGrowth), [undefined, meta(0, 1, 10, 2, 0),
  meta(0, 1, 10, 2, 0), undefined, undefined, meta(1, 2, 20, 3, 0)]);
assert.deepEqual(after, {1: entry(0, 2), 2: entry(0, 1)});
assert.ok(!JSON.stringify(draws[0]).includes("snackGrowth"), "absent metadata is omitted on the wire");

// Existing parsers own clamps and the stage ceiling. Missing definitions never invent growth.
for (const [ruleRows, curveRows] of [[[], rows], [rules, []], [rules, rows.slice(1)],
  [rules, [rows[0], rows[2]]], [[{maxLevel: 4, maxLimitBreak: 0}], rows]]) {
  assert.throws(() => requireSnackGrowthCurve(ruleRows, curveRows),
    error => error.code === "failed-precondition" && error.message.includes("RuleUnavailable"));
}
assert.equal(requireSnackGrowthCurve([{maxLevel: 4, maxLimitBreak: 99}], rows).maxStage, 3);
assert.equal(requireSnackGrowthCurve([{maxLevel: 4, maxLimitBreak: 1}],
  [{stage: 1, snackCost: 0, hpGain: -1}]).steps.get(1).snackCost, 1);

// Direct cards, repeated packs and choice packs share one ordered growth stream.
const pack = {packId: "P", drawCount: 1, uniqueDraw: false, price: 0, minRankGrade: "Bronze"};
const context = {catalog: new Set([1]), cards: [{id: 1, grade: "Rare"}], grades: new Map([[1, "Rare"]]),
  thresholds: [0, 100, 200, 300, 400], choices: ["P"], snackGrowthCurve: curve,
  packs: new Map([["P", {pack, drops: [{id: 1, minGrade: "Bronze", cardId: 1, weight: 1}]}]])};
const rewards = [{id: 1, ownerType: "CardDuplicate", ownerId: "Rare", order: 1,
  rewardType: "Currency", rewardId: "Shard", amount: 2}];
const save = {ownership: {cardIds: [1]}, cardGrowth: {entries: {1: entry(0)}}};
const items = [
  {rewardType: "Pack", rewardId: "P", amount: 1},
  {rewardType: "Card", rewardId: "1", amount: 1},
  {rewardType: "PackChoice", rewardId: "UnlockedThemePack", amount: 3},
];
const grant = grantRewardItems(save, items, context, rewards, "P", 0, () => 0);
assert.deepEqual(grant.cards.map(card => card.snackGrowth),
  [undefined, meta(0, 1, 10, 2, 0), undefined, undefined, meta(1, 2, 20, 3, 0)]);
assert.deepEqual(grant.packs.map(p => p.cards[0].snackGrowth),
  [undefined, undefined, undefined, meta(1, 2, 20, 3, 0)]);
[0, 2, 3, 4].forEach((index, packIndex) => assert.strictEqual(grant.cards[index], grant.packs[packIndex].cards[0]));
assert.deepEqual(grant.slots.cardGrowth.entries[1], entry(0, 2));
assert.deepEqual(grant.currencies, [{currency: "Shard", amount: 2}]);
assert.deepEqual(save.cardGrowth.entries[1], entry(0));
assert.deepEqual(grantRewardItems(save, items, context, rewards, "P", 0, () => 0), grant);
console.log("PASS snack growth: manual parity, thresholds, multistage, ordered attribution, cap, missing specs, reward metadata");
