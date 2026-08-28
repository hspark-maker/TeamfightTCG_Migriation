const assert = require("node:assert/strict");
const {
  computeDeckHash,
  parseCardSpecRow,
  validateDeckShape,
  validateDeckSnapshots,
} = require("../lib/deckValidation.js");

const spec = parseCardSpecRow({
  id: 12,
  keywords: "Ranged Taunt",
  keywordUnlockLevel: 3,
  hp2: 2,
  hp3: 3,
  hp4: 4,
});
assert.ok(spec);
const specs = new Map([[12, spec]]);
const save = {
  ownership: {cardIds: [12]},
  cardGrowth: {entries: {"12": {level: 3, snack: 0, limitBreak: 2}}},
  keywordGrowth: {levels: {"1": 4, "8": 3}},
};
const valid = [{
  cardId: 12,
  level: 3,
  hpBonus: 14,
  evolutionStage: 1,
  unlockedKeywords: 9,
  synergyUnlocked: true,
}];

assert.deepEqual(validateDeckSnapshots(valid, specs, save), {ok: true});
assert.equal(
  validateDeckSnapshots(valid, specs, {
    ...save,
    keywordGrowth: {levels: {Ranged: 4, Taunt: 3}},
  }).code,
  "hp_bonus_mismatch"
);
assert.equal(
  validateDeckSnapshots([{...valid[0], cardId: 99}], specs, save).code,
  "card_not_found"
);
assert.equal(
  validateDeckSnapshots(valid, specs, {...save, ownership: {cardIds: []}}).code,
  "card_not_owned"
);
assert.equal(
  validateDeckSnapshots([{...valid[0], hpBonus: 13}], specs, save).code,
  "hp_bonus_mismatch"
);
assert.equal(
  validateDeckSnapshots([{...valid[0], level: 4}], specs, save).code,
  "level_mismatch"
);
assert.match(computeDeckHash(valid), /^[0-9a-f]{64}$/);

const baseLevel = [{
  cardId: 12,
  level: 1,
  hpBonus: 0,
  evolutionStage: 0,
  unlockedKeywords: 0,
  synergyUnlocked: false,
}];
const baseSave = {
  ownership: {cardIds: [12]},
  cardGrowth: {entries: {}},
  keywordGrowth: {levels: {}},
};
assert.deepEqual(validateDeckSnapshots(baseLevel, specs, baseSave), {ok: true});

const sixCards = Array.from({length: 6}, (_, index) => ({
  ...baseLevel[0],
  cardId: index + 1,
}));
assert.equal(validateDeckShape(sixCards), null);
assert.match(validateDeckShape(sixCards.slice(0, 5)), /^deck_size:/);
assert.match(validateDeckShape([...sixCards.slice(0, 5), sixCards[0]]), /^duplicate_card:/);

// cardId 오름차순 규약: 순서만 다른 같은 덱은 거절한다.
const swapped = [...sixCards];
[swapped[0], swapped[1]] = [swapped[1], swapped[0]];
assert.match(validateDeckShape(swapped), /^deck_order:/);
assert.equal(validateDeckShape([...swapped].sort((a, b) => a.cardId - b.cardId)), null);

console.log("deck-validation tests passed");
