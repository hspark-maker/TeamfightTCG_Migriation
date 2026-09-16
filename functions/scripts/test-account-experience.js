"use strict";

const assert = require("node:assert/strict");
const {test} = require("node:test");
const {
  parseAccountLevels, resolveAccountLevel, validateAccountRewards,
  grantAccountExperience, battleAccountExperience,
} = require("../lib/account/accountProgression");
const {grantRewardItems} = require("../lib/rewards/itemGrant");
const {parseMissionCatalog} = require("../lib/missions/catalog");

const rows = [0, 100, 300, 600].map((requiredExp, index) =>
  ({id: index + 1, requiredExp, winExp: 100, loseExp: 50}));
const levels = parseAccountLevels(rows);
const reward = (level, rewardType, rewardId, amount = 1, order = 1) =>
  ({id: level * 10 + order, ownerType: "AccountLevel", ownerId: String(level), order, rewardType, rewardId, amount});
const currencyRows = [reward(2, "Currency", "Gold", 10), reward(3, "Currency", "Gold", 20),
  reward(4, "Currency", "Diamond", 3)];
const context = {levels, rewardRows: currencyRows, itemContext: null};

test("strict curve validation rejects empty, gaps, duplicate XP and unsafe values", () => {
  for (const invalid of [[], rows.slice(1), [rows[0], rows[2]], [rows[0], {...rows[1], requiredExp: 0}],
    [rows[0], {...rows[1], loseExp: -1}], [rows[0], {...rows[1], winExp: Number.MAX_SAFE_INTEGER + 1}]]) {
    assert.throws(() => parseAccountLevels(invalid));
  }
  assert.deepEqual(parseAccountLevels([...rows].reverse()), levels);
  assert.equal(resolveAccountLevel(99, levels), 1);
  assert.equal(resolveAccountLevel(100, levels), 2);
});

test("all crossed levels grant their bonuses atomically and preserve profile data", () => {
  const current = {profile: {accountExp: 90, nickname: "player", contentUnlocks: {version: 1}}};
  const before = structuredClone(current);
  const result = grantAccountExperience(current, 230, context, 0);
  assert.deepEqual(result.accountExperience, {grantedExp: 230, previousLevel: 1, level: 3});
  assert.deepEqual(result.currencies, [{currency: "Gold", amount: 10}, {currency: "Gold", amount: 20}]);
  assert.deepEqual(result.slots.profile, {...current.profile, accountExp: 320, accountRewardLevel: 3});
  assert.deepEqual(current, before, "transaction retries must receive untouched source data");
  assert.deepEqual(grantAccountExperience(current, 230, context, 0), result);
});

test("legacy high-level accounts skip past bonuses; sequential claims do not repeat a level", () => {
  const legacy = {profile: {accountExp: 350}};
  const first = grantAccountExperience(legacy, 100, context, 0);
  assert.deepEqual(first.currencies, []);
  assert.equal(first.slots.profile.accountRewardLevel, 3);
  const next = grantAccountExperience({...legacy, ...first.slots}, 200, context, 0);
  assert.deepEqual(next.currencies, [{currency: "Diamond", amount: 3}]);
  assert.deepEqual(grantAccountExperience({...legacy, ...next.slots}, 100, context, 0).currencies, []);
});

test("maximum XP caps safely and existing watermark prevents duplicate bonuses after curve shifts", () => {
  const max = grantAccountExperience({profile: {accountExp: 590}}, Number.MAX_SAFE_INTEGER, context, 0);
  assert.equal(max.slots.profile.accountExp, 600);
  assert.equal(max.accountExperience.grantedExp, 10);
  assert.deepEqual(grantAccountExperience({profile: {accountExp: 90, accountRewardLevel: 3}}, 230, context, 0).currencies, []);
  assert.throws(() => grantAccountExperience({profile: {accountExp: -1}}, 10, context, 0));
  assert.throws(() => grantAccountExperience({}, Number.NaN, context, 0));
  assert.equal(battleAccountExperience({}, true, context), 100);
  assert.equal(battleAccountExperience({}, false, context), 50);
});

test("bad automatic reward authoring fails instead of consuming the reached level", () => {
  for (const bad of [[], currencyRows.slice(1), [reward(1, "Currency", "Gold")], [reward(5, "Currency", "Gold")],
    [reward(2, "Currency", "Unknown")], [reward(2, "Currency", "Gold", 0)],
    [reward(2, "PackChoice", "UnlockedThemePack")],
    [reward(2, "Card", "1", 101)], [currencyRows[0], currencyRows[0]]]) {
    assert.throws(() => validateAccountRewards(levels, bad));
  }
  validateAccountRewards(levels, currencyRows);
  assert.throws(() => grantAccountExperience({profile: {accountExp: 90}}, 20,
    {...context, rewardRows: []}, 0), /Missing or invalid AccountLevel reward/);
});

test("mission card followed by level card keeps ownership and grants duplicate snack growth", () => {
  const duplicateRows = [{id: 99, ownerType: "CardDuplicate", ownerId: "Common", order: 1,
    rewardType: "Currency", rewardId: "Gold", amount: 2}];
  const itemContext = {catalog: new Set([1, 2]), grades: new Map([[1, "Common"], [2, "Common"]]),
    thresholds: [0], packs: new Map(), choices: [], cards: [],
    snackGrowthCurve: {maxStage: 1, steps: new Map([[1, {stage: 1, hpGain: 1, snackCost: 100}]])}};
  const current = {profile: {accountExp: 90}, ownership: {cardIds: [2]}};
  const mission = grantRewardItems(current, [{rewardType: "Card", rewardId: "1", amount: 1}],
    itemContext, duplicateRows, "", 0, () => 0);
  const result = grantAccountExperience({...current, ...mission.slots}, 20,
    {levels, rewardRows: [...duplicateRows, reward(2, "Card", "1")], itemContext}, 0, () => 0);
  assert.deepEqual(result.slots.ownership.cardIds, [2, 1]);
  assert.equal(result.cards[0].isNew, false);
  assert.equal(result.slots.cardGrowth.entries["1"].snack, result.cards[0].snack);
  assert.deepEqual(result.currencies, [{currency: "Gold", amount: 2}]);
});

test("mission account XP defaults to zero for older specs and validates authoring", () => {
  const row = {missionId: "daily.play", enabled: 1, period: "daily", eventKey: "PlayBattle", targetCount: 1,
    title: "Play", description: "Play a battle", passExp: 0, sortOrder: 1};
  assert.equal(parseMissionCatalog([row])[0].accountExp, 0);
  assert.equal(parseMissionCatalog([{...row, accountExp: 100}])[0].accountExp, 100);
  for (const accountExp of [-1, 1.5, "", Number.MAX_SAFE_INTEGER + 1]) {
    assert.throws(() => parseMissionCatalog([{...row, accountExp}]));
  }
});

test("XP-only mission works without a pass, survives transaction retry and rejects a second claim", async (t) => {
  const specs = require("../lib/specs/specBlobReader");
  const catalog = require("../lib/missions/missionSpec");
  const saves = require("../lib/save/saveDocument");
  const missions = require("../lib/missions/missionStore");
  const analytics = require("../lib/observability/analyticsEvent");
  const {claimMission} = require("../lib/commands/claimMission");
  const mission = {id: "daily.play", enabled: true, period: "daily", event: "PlayBattle", target: 1,
    title: "Play", description: "Play", passExp: 0, accountExp: 230, sortOrder: 1, guideActId: 0, guideActName: ""};
  let current = {profile: {accountExp: 90, nickname: "existing"}};
  let state = {progress: {"daily.PlayBattle": 1}, claimed: {}, passExp: 0};
  let wallet = {balances: {Gold: 5}, paidBalances: {}, rev: 0};
  let commits = 0;
  t.mock.method(specs, "readSpecRows", async (_env, name) => {
    if (name === "Reward") return currencyRows;
    if (name === "AccountLevel") return rows;
    if (name === "PassSeason") return [];
    throw new Error("Unexpected spec read: " + name);
  });
  t.mock.method(catalog, "readMissionCatalog", async () => [mission]);
  t.mock.method(analytics, "recordEvent", () => {});
  t.mock.method(missions, "beginMissionBump", async (_tx, _db, _env, _uid, period) =>
    ({ref: {}, unlocked: true, period, state: structuredClone(state)}));
  t.mock.method(saves, "mutateSave", async (_env, _uid, _source, _key, mutate, finalize) => {
    // The first callback loses a transaction conflict. Its writes must not leak into the retry.
    await mutate(structuredClone(current), {set() {}}, wallet);
    let missionWrite;
    const mutation = await mutate(structuredClone(current), {set(_ref, data) { missionWrite = data; }}, wallet);
    current = {...current, ...mutation.slots};
    state = missionWrite;
    wallet = mutation.wallet?.next ?? wallet;
    commits++;
    return finalize({revision: commits, updatedSlots: mutation.slots, wallet});
  });
  const request = {auth: {uid: "xp-test"}, data: {env: "test", missionId: mission.id}};
  const result = await claimMission.run(request);
  assert.equal(result.grantedAccountExp, 230);
  assert.deepEqual(result.accountExperience, {grantedExp: 230, previousLevel: 1, level: 3});
  assert.equal(current.profile.accountExp, 320);
  assert.equal(current.profile.nickname, "existing");
  assert.equal(state.claimed[mission.id], true);
  assert.equal(wallet.balances.Gold, 35);
  assert.deepEqual(result.granted, [{currency: "Gold", amount: 10}, {currency: "Gold", amount: 20}]);
  await assert.rejects(() => claimMission.run(request), (error) => error.details?.reason === "AlreadyClaimed");
  assert.equal(current.profile.accountExp, 320);
  assert.equal(wallet.balances.Gold, 35);
  assert.equal(commits, 1);
});
