"use strict";
const assert = require("node:assert/strict");
const {test} = require("node:test");
const {readStatistics, projectAchievements, statisticsResponse, applyStatisticsBattle,
  applyStatisticsPacks, emptyBattleStatistics} = require("../lib/statistics/playerStatistics");
const {onboardingResult} = require("../lib/save/onboardingOperation");

const legacy = () => ({revision: 71, progress: {WinBattle: 10, DestroyCards: 20, WinStreak: 5, OpenPack: 7,
  CompleteAlbum: 2, "PlaySynergy:Bulk": 6}, currentWinStreak: 2, claimed: {"wins.1": true}});
const battle = (state, extra = {}) => applyStatisticsBattle(state, {verified: true, tutorial: false,
  mode: "ranked", won: true, draw: false, destroyed: 3, attacks: 4, damageDealt: 20, healed: 2,
  synergyTriggers: 1, synergies: ["Bulk", "Bulk"], ...extra});
const saveProjection = (state, old) => {
  const projected = projectAchievements(state, old);
  projected.revision++;
  state.legacyProgress = {...projected.progress};
  state.legacyRevision = projected.revision;
  state.revision++;
  return projected;
};

test("migration preserves all historical counters and claims with empty, aligned battle denominators", () => {
  const old = legacy();
  const stats = readStatistics(undefined, old, 1000);
  assert.deepEqual(stats.lifetime, {wins: 10, cardsDestroyed: 20, currentWinStreak: 2, bestWinStreak: 5,
    packsOpened: 7, albumsCompleted: 2, synergyPlays: {Bulk: 6}});
  assert.equal(stats.trackedSinceMs, 1000);
  for (const bucket of Object.values(stats.battle)) assert.deepEqual(bucket, emptyBattleStatistics());
  assert.deepEqual(projectAchievements(stats, old), old);
});

test("new writes projected and re-read never double count; wins, draws and losses share one denominator", () => {
  let old = legacy();
  let stats = readStatistics(undefined, old, 1000);
  battle(stats); battle(stats, {mode: "adventure", won: false}); battle(stats, {won: false, draw: true});
  applyStatisticsPacks(stats, 2);
  old = saveProjection(stats, old);
  const before = structuredClone(stats);
  stats = readStatistics(stats, old, 2000);
  assert.deepEqual(stats, before);
  assert.equal(stats.battle.all.battles, 3);
  assert.equal(stats.battle.all.wins, 1); assert.equal(stats.battle.all.losses, 1); assert.equal(stats.battle.all.draws, 1);
  assert.equal(stats.battle.ranked.battles, 2); assert.equal(stats.battle.adventure.battles, 1);
  assert.equal(stats.battle.all.damageDealt, 60); assert.equal(stats.battle.all.healed, 6);
  assert.equal(stats.lifetime.wins, 11); assert.equal(stats.lifetime.packsOpened, 9);
  assert.equal(stats.lifetime.synergyPlays.Bulk, 9); assert.equal(stats.trackedSinceMs, 1000);
});

test("rollback writer positive deltas import once without inventing recent wins or historical losses", () => {
  let old = legacy(); let stats = readStatistics(undefined, old, 1000);
  battle(stats); old = saveProjection(stats, old);
  old.progress.WinBattle += 2; old.progress.OpenPack += 3; old.progress["PlaySynergy:Bulk"]++;
  old.progress.WinStreak = 8; old.currentWinStreak = 0; old.claimed["wins.2"] = true; old.revision += 5;
  stats = readStatistics(stats, old, 2000);
  assert.equal(stats.lifetime.wins, 13); assert.equal(stats.lifetime.packsOpened, 10);
  assert.equal(stats.lifetime.bestWinStreak, 8); assert.equal(stats.lifetime.currentWinStreak, 0);
  assert.equal(stats.battle.all.wins, 1); assert.equal(stats.battle.all.losses, 0);
  assert.equal(stats.battle.all.currentWinStreak, 0); assert.equal(stats.battle.all.bestWinStreak, 1);
  assert.equal(projectAchievements(stats, old).claimed["wins.2"], true);
  old = saveProjection(stats, old);
  assert.deepEqual(readStatistics(stats, old, 3000), stats);
  battle(stats);
  assert.equal(stats.lifetime.currentWinStreak, 1); assert.equal(stats.lifetime.bestWinStreak, 8);
});

test("unverified/tutorial results neither create wins nor break streaks", () => {
  const stats = readStatistics(undefined, legacy(), 1000);
  battle(stats);
  const before = structuredClone(stats);
  battle(stats, {verified: false, won: false}); battle(stats, {tutorial: true, won: false});
  assert.deepEqual(stats, before);
});

test("per-mode streaks ignore other modes, all-mode streaks reset on any recorded loss/draw", () => {
  const stats = readStatistics(undefined, legacy(), 1000);
  battle(stats); battle(stats); battle(stats, {mode: "adventure", won: false});
  assert.equal(stats.battle.all.currentWinStreak, 0); assert.equal(stats.battle.ranked.currentWinStreak, 2);
  battle(stats, {draw: true});
  assert.equal(stats.battle.ranked.currentWinStreak, 0); assert.equal(stats.battle.ranked.bestWinStreak, 2);
});

test("invalid counter data cannot decrease canonical lifetime or overflow safe integers", () => {
  let old = legacy(); let stats = readStatistics(undefined, old, 1000);
  old = saveProjection(stats, old);
  old.progress.WinBattle = 0; old.progress.OpenPack = -3; old.progress.Unknown = 20;
  stats = readStatistics(stats, old, 2000);
  assert.equal(stats.lifetime.wins, 10); assert.equal(stats.lifetime.packsOpened, 7);
  stats.lifetime.packsOpened = Number.MAX_SAFE_INTEGER;
  applyStatisticsPacks(stats, 9);
  assert.equal(stats.lifetime.packsOpened, Number.MAX_SAFE_INTEGER);
  battle(stats, {destroyed: NaN, attacks: -1, damageDealt: 1.1, synergies: ["bad/key"]});
  assert.equal(stats.battle.all.cardsDestroyed, 0); assert.equal(stats.battle.all.attacks, 0);
  assert.equal(stats.battle.all.damageDealt, 0); assert.equal(stats.lifetime.synergyPlays["bad/key"], undefined);
});

test("wire snapshots carry both revision axes without exposing migration internals or sharing mutable maps", () => {
  const stats = readStatistics(undefined, legacy(), 1000);
  saveProjection(stats, legacy());
  const wire = statisticsResponse(stats);
  assert.equal(wire.achievementRevision, 72); assert.equal(wire.revision, 1);
  assert.equal(wire.legacyProgress, undefined); assert.equal(wire.legacyRevision, undefined);
  wire.lifetime.synergyPlays.Bulk = 100; wire.battle.all.wins = 100;
  assert.equal(stats.lifetime.synergyPlays.Bulk, 6); assert.equal(stats.battle.all.wins, 0);
});

test("onboarding operation replays strip mutable statistics together with wallet/achievement state", () => {
  const result = onboardingResult({revision: 3, granted: [1], statistics: {revision: 2}, achievements: {}, wallet: {}, missions: {}});
  assert.deepEqual(result, {revision: 3, granted: [1]});
});
