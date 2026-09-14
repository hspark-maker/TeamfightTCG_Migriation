"use strict";

// npm.cmd run build && node --test scripts/test-mission-unlock.js
const assert = require("node:assert/strict");
const {test} = require("node:test");
const {
  beginMissionBump, missionBumpFromSnapshot, commitMissionBump, commitMissionBumps,
} = require("../lib/missions/missionStore");
const {applySnackGrowthProgress} = require("../lib/missions/snackGrowthProgress");
const {missionPeriod} = require("../lib/missions/period");
const {EVENTS} = require("../lib/analytics/eventNames");

const period = missionPeriod(Date.parse("2026-09-14T03:00:00Z"));
const unlockedSave = {profile: {contentUnlocks: {version: 1, unlocked: ["Mission"]}}};
const reference = {path: "envs/test/users/test-user/missions/current"};
const snapshot = (data) => ({exists: data !== undefined, data: () => data});
const bumpFor = (save, data) => missionBumpFromSnapshot(reference, snapshot(data), period, save);
const transaction = (writes) => ({set: (ref, data) => writes.push({ref, data})});
const events = ["OpenPack", "EnhanceCard", "PlayBattle", "WinBattle", "WinRankedBattle",
  "DestroyCard", "Attack", "TriggerSynergy", "TriggerKeyword", "ClaimReward"];

test("missing unlock, other content, and pending presentation do not count", () => {
  for (const save of [undefined, {}, {tutorial: {outgameCompleted: true}},
    {profile: {contentUnlocks: {unlocked: []}}},
    {profile: {contentUnlocks: {unlocked: ["Adventure"], pending: ["Mission"]}}},
    {profile: {contentUnlocks: {unlocked: "Mission"}}}]) {
    const bump = bumpFor(save);
    const writes = [];
    commitMissionBumps(transaction(writes), bump, events.map((event) => ({event, amount: 3})), 0);
    assert.deepEqual(bump.state.progress, {});
    assert.deepEqual(writes.at(-1).data.progress, {});
  }
});

test("unlock starts daily and weekly counts without retroactive progress", () => {
  const writes = [];
  const locked = bumpFor({});
  commitMissionBump(transaction(writes), locked, "OpenPack", 5, 0);
  const unlocked = bumpFor(unlockedSave, writes.at(-1).data);
  commitMissionBump(transaction(writes), unlocked, "OpenPack", 1, 0);
  assert.deepEqual(writes.at(-1).data.progress, {"daily.OpenPack": 1, "weekly.OpenPack": 1});
});

test("unlocked batch events increment both periods by their amounts", () => {
  const bump = bumpFor(unlockedSave);
  commitMissionBumps(transaction([]), bump, events.map((event) => ({event, amount: 3})), 0);
  for (const event of events) {
    assert.equal(bump.state.progress["daily." + event], 3);
    assert.equal(bump.state.progress["weekly." + event], 3);
  }
});

test("automatic snack growth uses the same unlock gate", () => {
  const cards = [{snackGrowth: {fromStage: 0, toStage: 2}}];
  const locked = bumpFor({});
  applySnackGrowthProgress(locked, cards);
  assert.deepEqual(locked.state.progress, {});
  const unlocked = bumpFor(unlockedSave);
  applySnackGrowthProgress(unlocked, cards);
  const event = EVENTS.cardLimitBreakCompleted.missionKey;
  assert.equal(unlocked.state.progress["daily." + event], 2);
  assert.equal(unlocked.state.progress["weekly." + event], 2);
});

test("blocked increments preserve stored progress, guide progress, claims, and pass exp", () => {
  const stored = {
    dailyKey: period.daily, weeklyKey: period.weekly,
    progress: {"daily.OpenPack": 2, "weekly.OpenPack": 4, "guide.Guide.DeckSaved6": 1},
    claimed: {"daily.pack": true}, passExp: 30,
  };
  const bump = bumpFor({}, structuredClone(stored));
  commitMissionBump(transaction([]), bump, "OpenPack", 1, 0);
  assert.deepEqual(bump.state, stored);
});

test("single-document reads and batched reads use the same saved unlock", async () => {
  for (const save of [{}, unlockedSave]) {
    let reads = 0;
    const bump = await beginMissionBump({get: async (ref) => {
      assert.equal(ref, reference);
      reads++;
      return snapshot(undefined);
    }}, {doc: () => reference}, "test", "test-user", period, save);
    assert.deepEqual(bump, bumpFor(save));
    assert.equal(reads, 1);
  }
});
