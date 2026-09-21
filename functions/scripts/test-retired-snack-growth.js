"use strict";
const assert = require("node:assert/strict");
const {test} = require("node:test");
const {drawPack} = require("../lib/packs/packDraw");
const {grantRewardItems, applyDrawnCardGrowth} = require("../lib/rewards/itemGrant");
const {readGrowthEntries, growthSlot, feedShard} = require("../lib/growth/cardGrowth");
const {parseAiCardGrowth, buildAiDeckSnapshots, validateDeckSnapshots} = require("../lib/deckValidation");
const {parseCardEnhanceRule} = require("../lib/growth/enhanceRules");
const {onboardingFingerprint} = require("../lib/save/onboardingOperation");
const {createHash} = require("node:crypto");

const grades = new Map([[1, "Common"], [2, "Arcane"]]);
const duplicateRows = [{id: 1, ownerType: "CardDuplicate", ownerId: "Common", order: 1,
  rewardType: "Currency", rewardId: "Gold", amount: 2}];
const context = {catalog: new Set([1, 2]), grades, thresholds: [0], packs: new Map(), choices: [], cards: []};
const legacy = {level: 3, shardProgress: 2, snack: 27, limitBreak: 3};
const activeGrowth = {level: 3, shardProgress: 2};

test("reward initialization requires neither retired tables nor card enhancement rules", async () => {
  const Module = require("node:module");
  const originalLoad = Module._load;
  const file = require.resolve("../lib/rewards/itemGrant");
  const previous = require.cache[file];
  const reads = [];
  Module._load = function(request, parent, isMain) {
    if (request === "../packs/cardCatalog") return {loadCatalogIds: async () => new Set([1])};
    if (request === "../packs/packSpecReader") return {
      readSpecRows: async (env, table) => {
        reads.push(table);
        if (table === "Card") return [{id: 1, grade: "Common"}];
        if (table === "CardPack") return [];
        throw new Error(`Unexpected required table: ${table}`);
      },
      readRankGradeRows: async () => ["Bronze", "Silver", "Gold", "Platinum", "Diamond"]
        .map((gradeKey, id) => ({id, gradeKey, entryPoints: id * 100})),
    };
    return originalLoad.call(this, request, parent, isMain);
  };
  try {
    delete require.cache[file];
    const {loadItemGrantContext} = require(file);
    const loaded = await loadItemGrantContext("test", []);
    assert.deepEqual(reads, ["Card", "CardPack"]);
    assert.equal(loaded.grades.get(1), "Common");
  } finally {
    Module._load = originalLoad;
    require.cache[file] = previous;
  }
});

test("repeated pack cards retain duplicate identity without snack rewards or growth", () => {
  const cards = drawPack([{cardId: 1, weight: 1}], 3, false, context.catalog, new Set(), () => 0);
  assert.deepEqual(cards, [{cardId: 1, isNew: true}, {cardId: 1, isNew: false}, {cardId: 1, isNew: false}]);
  assert.deepEqual(applyDrawnCardGrowth({1: legacy}, cards, grades), {1: legacy});
});

test("direct duplicate rewards preserve old growth and only grant authored currency", () => {
  const current = {ownership: {cardIds: [1]}, cardGrowth: {entries: {1: {...legacy}}}};
  const result = grantRewardItems(current, [{rewardType: "Card", rewardId: "1", amount: 2}],
    context, duplicateRows, "", 0, () => 0);
  assert.deepEqual(result.cards, [{cardId: 1, isNew: false}, {cardId: 1, isNew: false}]);
  assert.deepEqual(result.slots.cardGrowth.entries, {1: activeGrowth});
  assert.deepEqual(result.currencies, [{currency: "Gold", amount: 2}, {currency: "Gold", amount: 2}]);
  assert.deepEqual(current.cardGrowth.entries, {1: legacy});
});

test("free duplicate pack pays no invented currency and new rare cards retain starting level", () => {
  const packContext = {...context, packs: new Map([["free", {
    pack: {price: 0, drawCount: 2, uniqueDraw: false},
    drops: [{id: 1, packId: "free", minGrade: "Unranked", cardId: 1, weight: 1}],
  }]])};
  const result = grantRewardItems({ownership: {cardIds: [1]}, cardGrowth: {entries: {1: legacy}}},
    [{rewardType: "Pack", rewardId: "free", amount: 1}], packContext, [], "", 0, () => 0);
  assert.deepEqual(result.currencies, []);
  assert.deepEqual(result.slots.cardGrowth.entries, {1: activeGrowth});
  assert.deepEqual(result.cards, [{cardId: 1, isNew: false}, {cardId: 1, isNew: false}]);
  assert.equal(applyDrawnCardGrowth({}, [{cardId: 2, isNew: true}], grades)[2].level, 2);
});

test("card shard enhancement removes retired storage while preserving active growth and HP validation", () => {
  const old = readGrowthEntries({entries: {1: legacy}});
  const fed = feedShard(old, 1, 10, false, 2);
  assert.deepEqual(growthSlot(fed.entries).entries[1], {...activeGrowth, shardProgress: 4});
  const spec = {id: 1, maxHp: 10, keywords: 1, keywordUnlockLevel: 3,
    defaultEvolutionStage: 0, synergies: [], hp2: 4, hp3: 6, hp4: 10};
  const card = {cardId: 1, level: 3, hpBonus: 14, evolutionStage: 1, unlockedKeywords: 1, synergyUnlocked: true};
  const steps = new Map([[4, {cost: 10}]]);
  for (const retired of [{}, {snack: 27, limitBreak: 3}, {snack: 999999, limitBreak: 999}]) {
    const save = {ownership: {cardIds: [1]}, cardGrowth: {entries: {1: {level: 3, shardProgress: 4, ...retired}}}};
    assert.deepEqual(validateDeckSnapshots([card], new Map([[1, spec]]), save, steps), {ok: true});
    assert.equal(validateDeckSnapshots([{...card, hpBonus: 22}], new Map([[1, spec]]), save, steps).code,
      "hp_bonus_mismatch");
  }
  assert.deepEqual(parseCardEnhanceRule([{maxLevel: 4, baseEnhanceCost: 10, costGrowthPerLevel: 0}]),
    {maxLevel: 4, baseEnhanceCost: 10, costGrowthPerLevel: 0});
});

test("AI ignores old limit-break authoring and no longer requires a limit-break table", () => {
  const ids = [1, 2, 3, 4, 5, 6];
  const raw = ids.map((cardId) => ({cardId, level: 2, limitBreak: 999}));
  const growth = parseAiCardGrowth(ids, raw);
  assert.deepEqual(growth, ids.map((cardId) => ({cardId, level: 2})));
  const specs = new Map(ids.map((id) => [id, {id, maxHp: 10, keywords: 1, keywordUnlockLevel: 3,
    hp2: 4, hp3: 6, hp4: 10, defaultEvolutionStage: 0, synergies: []}]));
  assert(buildAiDeckSnapshots(ids, growth, specs).every((card) => card.hpBonus === 4 && card.unlockedKeywords === 0));
  assert.equal(parseAiCardGrowth(ids, raw.map((entry) => ({...entry, level: 5}))), null);
});

test("retired limit-break operation keeps the original read-only recovery fingerprint", () => {
  const expected = createHash("sha256").update(JSON.stringify({command: "limitBreakCard", args: {cardId: 1}})).digest("hex");
  assert.equal(onboardingFingerprint("limitBreakCard", {cardId: "1", env: "test"}), expected);
});
