"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const lib = process.env.FUNCTIONS_TEST_LIB || path.resolve(__dirname, "../lib");
const {drawRouletteCycle, rouletteCycleKey} = require(path.join(lib, "roulette/rouletteCycle"));
const {resolveRouletteBoard} = require(path.join(lib, "roulette/rouletteDraw"));
function csv(name) {
  const lines = fs.readFileSync(path.resolve(__dirname, "../../docs/SpecData", name), "utf8").trim().split(/\r?\n/);
  const keys = lines[1].split(",");
  return lines.slice(3).map((line) => Object.fromEntries(line.split(",").map((value, i) => [keys[i], value])));
}
const board = resolveRouletteBoard(csv("Roulette_sheet.csv")[0], csv("RouletteSlot_sheet.csv"));
assert.equal(board.droppedRows, 0);
assert.equal(board.slots.length, 8);
const weights = [25, 20, 25, 15, 8, 4, 2, 1];
assert.deepEqual(board.slots.map((slot) => slot.weight), weights);

// 最初の抽選の全100通りが元の確率を正確に表す。
const firstHits = Array(8).fill(0);
for (let ticket = 0; ticket < 100; ticket++) {
  firstHits[drawRouletteCycle(board.slots, undefined, (max) => {
    assert.equal(max, 100);
    return ticket;
  }).slot.slotIndex]++;
}
assert.deepEqual(firstHits, weights);

const first = drawRouletteCycle(board.slots, undefined, () => 0);
assert.equal(first.cycle.remaining[0], 24);
assert(24 / 99 < 25 / 100);
assert(20 / 99 > 20 / 100);
const originalState = structuredClone(first.cycle);
drawRouletteCycle(board.slots, first.cycle, () => 0);
assert.deepEqual(first.cycle, originalState, "Input state must not be mutated on retry");

// 決定的な複数シードで各プレイヤーの3周期を比較する。
for (let seed = 1; seed <= 500; seed++) {
  let random = seed, saved;
  const roll = (max) => {
    random = (Math.imul(random, 1664525) + 1013904223) >>> 0;
    return Math.floor(random / 2 ** 32 * max);
  };
  for (let cycle = 0; cycle < 3; cycle++) {
    const hits = Array(8).fill(0);
    for (let spin = 0; spin < 100; spin++) {
      const result = drawRouletteCycle(board.slots, saved, roll);
      hits[result.slot.slotIndex]++;
      assert(hits[result.slot.slotIndex] <= weights[result.slot.slotIndex]);
      saved = JSON.parse(JSON.stringify(result.cycle));
      assert.equal(saved.remaining.reduce((a, b) => a + b, 0), 99 - spin);
    }
    assert.deepEqual(hits, weights);
  }
}

// 枯渇した1%枠は次周期まで出ず、並べ替えでは周期がリセットされない。
const rare = drawRouletteCycle(board.slots, undefined, (max) => max - 1);
assert.equal(rare.slot.slotIndex, 7);
const afterRare = drawRouletteCycle([...board.slots].reverse(), rare.cycle, (max) => max - 1);
assert.equal(afterRare.slot.slotIndex, 6);
assert.equal(afterRare.cycle.signature, rare.cycle.signature);
assert.equal(afterRare.cycle.remaining[7], 0);
for (const field of ["amount", "weight", "rewardId"]) {
  const changed = board.slots.map((slot) => ({...slot}));
  changed[0][field] = field === "rewardId" ? "Diamond" : changed[0][field] + 1;
  const reset = drawRouletteCycle(changed, rare.cycle, () => 0);
  assert.notEqual(reset.cycle.signature, rare.cycle.signature);
  assert.equal(reset.cycle.remaining[7], 1);
}
for (const invalid of [[], [-1, ...weights.slice(1)], [0.5, ...weights.slice(1)], [26, ...weights.slice(1)]]) {
  assert.throws(() => drawRouletteCycle(board.slots, {signature: first.cycle.signature, remaining: invalid}, () => 0));
}
assert.throws(() => drawRouletteCycle([], undefined, () => 0));
assert.throws(() => drawRouletteCycle(board.slots, undefined, (max) => max));
assert.throws(() => drawRouletteCycle(board.slots, undefined, () => -1));
assert.equal(rouletteCycleKey("a/b").includes("/"), false);
assert.notEqual(rouletteCycleKey("a/b"), rouletteCycleKey("a_b"));
console.log("PASS: original odds, depletion, 500 players x 3 exact 100-spin cycles, restart, spec changes and invalid-state checks.");
