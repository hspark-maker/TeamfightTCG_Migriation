"use strict";
const assert = require("node:assert/strict");
const {test} = require("node:test");
const {parseAchievementCatalog, achievementProgressKey} = require("../lib/achievements/achievementCatalog");
const {activeAchievementSynergies, applyAchievementAlbums, applyAchievementBattle,
  incrementAchievement, judgeAchievementClaim} = require("../lib/achievements/achievementProgress");
const {achievementsRef, readAchievements, writeAchievements, achievementResponse} = require("../lib/achievements/achievementStore");
const {parseAlbumEntryRows, parseAlbumThemeRows} = require("../lib/completionTable");

const row = (stage = 1, extra = {}) => ({id: stage, achievementId: `wins.${stage}`, groupId: "wins", stage,
  eventKey: "WinBattle", synergyId: "", targetCount: stage * 10, title: "Wins", description: "Total wins",
  rewardCurrency: "Gold", rewardAmount: 10, sortOrder: 1, enabled: 1, ...extra});
const fresh = () => readAchievements({data: () => undefined});
const battle = (state, extra = {}) => applyAchievementBattle(state,
  {verified: true, tutorial: false, won: true, draw: false, destroyed: 3, synergies: ["Bulk"], ...extra});

test("catalog exposes strict existing currency rewards, stages and parameterized keys", () => {
  const catalog = parseAchievementCatalog([row(2), row(1)]);
  assert.deepEqual(catalog.map((d) => d.stage), [1, 2]);
  for (const currency of ["Gold", "Diamond", "Shard"]) {
    assert.equal(parseAchievementCatalog([row(1, {rewardCurrency: currency})])[0].reward.currencies[0].currency, currency);
  }
  assert.equal(achievementProgressKey({event: "PlaySynergy", synergyId: "Bulk"}), "PlaySynergy:Bulk");
  assert.equal(achievementProgressKey(catalog[0]), "WinBattle");
});

test("malformed reward, event, duplicate id and stage definitions fail closed", () => {
  for (const extra of [{rewardCurrency: "Gem"}, {rewardCurrency: "ProfileFrame"}, {rewardAmount: -1},
    {rewardAmount: 1.5}, {rewardAmount: Number.MAX_SAFE_INTEGER}, {targetCount: 0}, {eventKey: "Unknown"},
    {synergyId: "Bulk"}, {eventKey: "PlaySynergy", synergyId: ""}, {achievementId: "bad/key"}]) {
    assert.throws(() => parseAchievementCatalog([row(1, extra)]), /Invalid Achievement/);
  }
  for (const rows of [[row(), row()], [row(2)], [row(), row(2, {targetCount: 9})],
    [row(), row(2, {eventKey: "OpenPack"})]]) assert.throws(() => parseAchievementCatalog(rows));
  assert.deepEqual(parseAchievementCatalog([row(1, {enabled: 0, rewardCurrency: "ProfileFrame"})]), []);
});

test("wins, destroys and synergy plays accrue across claims and dates without resets", () => {
  const state = fresh();
  battle(state);
  state.claimed["wins.1"] = true;
  battle(state, {won: false, destroyed: 2, synergies: ["Bulk", "Bulk", "Trace"]});
  incrementAchievement(state, "OpenPack", 2);
  assert.deepEqual(state.progress, {WinBattle: 1, DestroyCards: 5, WinStreak: 1,
    "PlaySynergy:Bulk": 2, "PlaySynergy:Trace": 1, OpenPack: 2});
  assert.equal(state.claimed["wins.1"], true);
});

test("consecutive wins reset on loss and draw while best streak stays claimable", () => {
  const state = fresh();
  battle(state); battle(state); battle(state);
  assert.equal(state.currentWinStreak, 3);
  battle(state, {won: false});
  assert.equal(state.currentWinStreak, 0);
  battle(state); battle(state, {draw: true});
  assert.equal(state.currentWinStreak, 0);
  assert.equal(state.progress.WinStreak, 3);
  assert.equal(state.progress.WinBattle, 4);
});

test("unverified and tutorial matches never progress or break an ongoing streak", () => {
  const state = fresh(); battle(state);
  const before = structuredClone(state);
  battle(state, {verified: false, won: false});
  battle(state, {tutorial: true, won: false});
  assert.deepEqual(state, before);
});

test("active synergy requires unlocked unique cards and the pinned threshold", () => {
  const cards = [{id: 1, synergies: "Data_Synergy_Bulk/Data_Synergy_Trace"},
    {id: 2, synergies: "Data_Synergy_Bulk"}, {id: 3, synergies: "Data_Synergy_Trace"}];
  const deck = [{cardId: 1, synergyUnlocked: true}, {cardId: 2, synergyUnlocked: true},
    {cardId: 3, synergyUnlocked: false}];
  const tiers = [{synergyId: "Bulk", requiredCount: 2}, {synergyId: "Bulk", requiredCount: 3},
    {synergyId: "Trace", requiredCount: 2}];
  assert.deepEqual(activeAchievementSynergies(deck, [...cards, cards[0]], tiers), ["Bulk"]);
  assert.deepEqual(activeAchievementSynergies(deck, cards, [{synergyId: "Bulk", requiredCount: 3}]), []);
  assert.deepEqual(activeAchievementSynergies(deck, cards, []), []);
});

test("album counts complete published themes, excludes locked/empty/unknown themes and never counts pages", () => {
  const entries = parseAlbumEntryRows([{themeId: "A", pageId: "p1", cardId: 1},
    {themeId: "A", pageId: "p2", cardId: 2}, {themeId: "B", pageId: "p1", cardId: 1},
    {themeId: "C", pageId: "p1", cardId: 1}, {themeId: "unknown", pageId: "p1", cardId: 1}]);
  const themes = parseAlbumThemeRows([{themeId: "A", locked: 0}, {themeId: "A", locked: 0},
    {themeId: "B", locked: 1}, {themeId: "C", locked: 0}, {themeId: "empty", locked: 0}]);
  const state = fresh();
  applyAchievementAlbums(state, {ownership: {cardIds: [1]}}, entries, themes);
  assert.equal(state.progress.CompleteAlbum, 1);
  applyAchievementAlbums(state, {ownership: {cardIds: [1, 2]}}, entries, themes);
  assert.equal(state.progress.CompleteAlbum, 2);
  applyAchievementAlbums(state, {}, entries, themes);
  assert.equal(state.progress.CompleteAlbum, 2);
});

test("tier claims require earlier stages but do not consume progress", () => {
  const catalog = parseAchievementCatalog([row(), row(2)]);
  const state = fresh(); state.progress.WinBattle = 25;
  assert.equal(judgeAchievementClaim("wins.2", state, catalog), "NotEligible");
  assert.equal(judgeAchievementClaim("wins.1", state, catalog), null);
  state.claimed["wins.1"] = true;
  assert.equal(judgeAchievementClaim("wins.2", state, catalog), null);
  assert.equal(judgeAchievementClaim("wins.1", state, catalog), "AlreadyClaimed");
  assert.equal(judgeAchievementClaim("unknown", state, catalog), "AchievementNotFound");
  assert.equal(state.progress.WinBattle, 25);
});

test("stored state normalizes malformed counters and response revision increases with committed mutations", () => {
  const state = readAchievements({data: () => ({revision: 7, progress: {WinBattle: -1, OpenPack: "3", Unknown: 20},
    claimed: {"wins.1": true, "wins.2": "true"}, currentWinStreak: NaN})});
  assert.deepEqual(state.progress, {WinBattle: 0, OpenPack: 0});
  let saved;
  writeAchievements({set: (_ref, value) => { saved = value; }}, {}, state, 100);
  const response = achievementResponse(state);
  assert.equal(saved.revision, 8); assert.equal(response.revision, 8);
  state.progress.WinBattle = 8;
  assert.equal(response.progress.WinBattle, 0);
  assert.equal(achievementsRef({doc: (path) => path}, "test", "user"), "envs/test/users/user/achievements/current");
});

