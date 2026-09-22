"use strict";
const assert = require("node:assert/strict");
const {test} = require("node:test");
const {readGrowthEntries, growthSlot, feedShard} = require("../lib/growth/cardGrowth");
const {buildFreshAccountSlots, buildFreshAccountBalances} = require("../lib/save/freshAccount");

test("new accounts contain no retired growth data and retain starters and wallet defaults", () => {
  const slots = buildFreshAccountSlots([1, 2, 3, 4, 5, 6], "tester", new Map([[1, "Arcane"]]));
  assert.equal(Object.hasOwn(slots, "keywordGrowth"), false);
  assert.deepEqual(slots.cardGrowth, {entries: {1: {level: 2}}});
  assert.deepEqual(slots.ownership.cardIds, [1, 2, 3, 4, 5, 6]);
  assert.deepEqual(slots.deck.slots[0].cardIds, [1, 2, 3, 4, 5, 6]);
  assert.deepEqual(buildFreshAccountBalances(), {Gold: 100, Diamond: 0, Energy: 0, Shard: 0, RouletteTicket: 0, CardDust: 0});
});

test("card serialization strips retired fields from legacy and direct input without changing active growth", () => {
  const old = {entries: {
    1: {level: 3, shardProgress: 7, snack: 12, limitBreak: 2},
    2: {level: 4, snack: 5, limitBreak: 3},
    3: {level: 1, shardProgress: 0, snack: 80, limitBreak: 1},
  }};
  const expected = {entries: {1: {level: 3, shardProgress: 7}, 2: {level: 4}}};
  assert.deepEqual(growthSlot(readGrowthEntries(old)), expected);
  assert.deepEqual(growthSlot(old.entries), expected);
  assert.deepEqual(growthSlot(feedShard(readGrowthEntries(old), 1, 10, false, 1).entries),
    {entries: {...expected.entries, 1: {level: 3, shardProgress: 8}}});
  assert.equal(old.entries[1].snack, 12);
});
