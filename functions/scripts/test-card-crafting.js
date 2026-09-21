"use strict";
const assert = require("node:assert/strict");
const {test} = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const {csv} = require("./publish-local-spec");
const {parseCraftRecipes, CRAFT_CURRENCY} = require("../lib/crafting/catalog");
const {CURRENCY_MAX} = require("../lib/currency/currencyKeys");
const matrix = csv(fs.readFileSync(path.resolve(__dirname, "../../docs/SpecData/CardCraft_sheet.csv"), "utf8"));
const header = matrix.findIndex((row) => row[0] === "id");
const rules = matrix.slice(header + 2).filter((row) => row[0].trim()).map((row) =>
  Object.fromEntries(matrix[header].map((key, index) => [key, key === "grade" ? row[index] : Number(row[index])])));
const cards = ["Common", "Rare", "Arcane", "Mythic"].map((grade, i) => ({id: i + 1, grade}));
const ids = new Set(cards.map((card) => card.id));

test("authored CSV resolves every grade to its server price in CardDust", () => {
  assert.equal(CRAFT_CURRENCY, "CardDust");
  assert.deepEqual(parseCraftRecipes(rules, cards, ids), cards.map((card, i) =>
    ({cardId: card.id, grade: card.grade, cost: [20, 40, 100, 200][i]})));
});

test("catalog restrictions and disabled grades remove recipes", () => {
  const disabled = rules.map((rule) => ({...rule, enabled: rule.grade === "Rare" ? 0 : 1}));
  assert.deepEqual(parseCraftRecipes(disabled, cards, new Set([1, 2, 3])).map((card) => card.cardId), [1, 3]);
  assert.deepEqual(parseCraftRecipes(rules.map((rule) => ({...rule, enabled: 0})), cards, ids), []);
});

test("missing, malformed, duplicate and unsafe prices cannot become free or ambiguous recipes", () => {
  assert.throws(() => parseCraftRecipes([], cards, ids));
  for (const cost of [0, -1, 1.5, NaN, Infinity, CURRENCY_MAX + 1, "20", null, undefined]) {
    assert.throws(() => parseCraftRecipes([{...rules[0], cost}], cards, ids));
  }
  for (const patch of [{grade: "Unknown"}, {enabled: true}, {enabled: 2}, {id: 0}, {id: "1"}]) {
    assert.throws(() => parseCraftRecipes([{...rules[0], ...patch}], cards, ids));
  }
  assert.throws(() => parseCraftRecipes([rules[0], rules[0]], cards, ids));
  assert.throws(() => parseCraftRecipes([rules[0], {...rules[0], id: 9}], cards, ids));
});

test("invalid card definitions fail closed while noncatalog cards are excluded", () => {
  assert.throws(() => parseCraftRecipes(rules, [...cards, cards[0]], ids));
  assert.throws(() => parseCraftRecipes(rules, [{id: 1, grade: "Unknown"}], ids));
  assert.deepEqual(parseCraftRecipes(rules, [{id: 99, grade: "Unknown"}], ids), []);
});
