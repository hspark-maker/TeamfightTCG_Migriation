"use strict";
const assert = require("node:assert/strict");
const {test} = require("node:test");
const {resolve} = require("node:path");
const {readFileSync} = require("node:fs");
const output = process.env.TITLE_TEST_BUILD || "../lib";
const built = name => require(resolve(__dirname, output, name));
const {grantAchievementTitles, loadAchievementTitles, titleConditionDefinitions} = built("titles/achievementTitles");
const {parseTitles} = built("titles/titleOwnership");
const {parseAchievementCatalog} = built("achievements/achievementCatalog");
const {readAchievementCatalog} = built("achievements/achievementSpec");
const {parseRewardRows} = built("rewardTable");
const specs = built("specs/specBlobReader");
const rows = [{id: 1, titleId: "first"}, {id: 2, titleId: "second"}, {id: 3, titleId: "unassigned"}];
const titles = parseTitles(rows);
const achievementRows = [1, 2].map(stage => ({id: stage, achievementId: "win." + stage,
  groupId: "win", stage, eventKey: "WinBattle", synergyId: "", targetCount: stage === 1 ? 1 : 10,
  title: "Wins", description: "Win condition " + stage, sortOrder: stage, enabled: 1}));
const currencyRewards = achievementRows.map(r => ({id: r.id, ownerType: "Achievement", ownerId: r.achievementId,
  order: 1, rewardType: "Currency", rewardId: "Gold", amount: r.id * 10}));
const titleRewards = achievementRows.map((r, i) => ({id: 10 + r.id, ownerType: "Achievement", ownerId: r.achievementId,
  order: 2, rewardType: "Title", rewardId: rows[i].titleId, amount: 1}));
const rewards = [...currencyRewards, ...titleRewards];
const catalog = () => parseAchievementCatalog(achievementRows, rewards);

test("Reward owner IDs supply exact stage title and currency independently from conditions", () => {
  const definitions = catalog();
  assert.deepEqual(definitions[0].reward, {currencies: [{currency: "Gold", amount: 10}],
    items: [{rewardType: "Title", rewardId: "first", amount: 1}]});
  assert.deepEqual(definitions[1].reward.items, [{rewardType: "Title", rewardId: "second", amount: 1}]);
  const changed = parseAchievementCatalog(achievementRows.map(r => ({...r, targetCount: r.targetCount * 10})), rewards);
  assert.deepEqual(changed.map(d => d.reward), definitions.map(d => d.reward));
  const reassigned = parseAchievementCatalog(achievementRows, [...currencyRewards,
    {...titleRewards[0], ownerId: "win.2"}]);
  assert.deepEqual(reassigned[0].reward.items, []);
  assert.deepEqual(reassigned[1].reward.items, [{rewardType: "Title", rewardId: "first", amount: 1}]);
});

test("claim grants only its authored stage title, preserves profile and does not equip or duplicate ownership", () => {
  const definitions = catalog();
  const profile = {nickname: "kept", ownedAvatarIds: ["a"], equippedTitleId: "old", ownedTitleIds: ["old"]};
  const first = grantAchievementTitles(profile, definitions[0], titles);
  assert.deepEqual(first.titles, [{titleId: "first", isNew: true}]);
  assert.deepEqual(first.profile, {...profile, ownedTitleIds: ["old", "first"]});
  assert.deepEqual(profile.ownedTitleIds, ["old"]);
  assert.deepEqual(grantAchievementTitles(first.profile, definitions[0], titles).titles, [{titleId: "first", isNew: false}]);
  assert.deepEqual(grantAchievementTitles(first.profile, definitions[1], titles).titles, [{titleId: "second", isNew: true}]);
});

test("a title-only achievement is supported and another owner's identically named reward does not leak", () => {
  const definitions = parseAchievementCatalog(achievementRows, [...titleRewards,
    {...currencyRewards[0], ownerType: "Mission"}]);
  assert.deepEqual(definitions[0].reward.currencies, []);
  assert.deepEqual(definitions[0].reward.items, [{rewardType: "Title", rewardId: "first", amount: 1}]);
});

test("title unlock text comes from the Reward-linked achievement description", () => {
  const definitions = titleConditionDefinitions(titles, catalog());
  assert.match(definitions.find(d => d.titleId === "first").description, /Win condition 1/);
  assert.match(definitions.find(d => d.titleId === "first").description, /업적 보상/);
  assert.match(definitions.find(d => d.titleId === "second").description, /Win condition 2/);
  assert.ok(!definitions.some(d => d.titleId === "unassigned" && /Win condition/.test(d.description)));
});

test("currency-only achievement rewards remain usable without a published Title table", async t => {
  t.mock.method(specs, "readOptionalSpecRows", async (_env, name) => {assert.equal(name, "Title"); return null;});
  t.mock.method(specs, "readSpecRows", async (_env, name) => {
    if (name === "Achievement") return achievementRows;
    if (name === "Reward") return currencyRewards;
    throw new Error("Unexpected table " + name);
  });
  assert.deepEqual(await loadAchievementTitles("live"), []);
  const loaded = await readAchievementCatalog("test");
  assert.deepEqual(loaded[0].reward.currencies, [{currency: "Gold", amount: 10}]);
  assert.deepEqual(loaded.flatMap(d => d.reward.items), []);
});

test("published Title IDs are validated against Reward references before claims", async t => {
  t.mock.method(specs, "readOptionalSpecRows", async () => rows);
  t.mock.method(specs, "readSpecRows", async (_env, name) => {
    if (name === "Achievement") return achievementRows;
    if (name === "Reward") return rewards;
    throw new Error("Unexpected table " + name);
  });
  assert.deepEqual((await readAchievementCatalog("test"))[1].reward.items,
    [{rewardType: "Title", rewardId: "second", amount: 1}]);
  t.mock.method(specs, "readOptionalSpecRows", async () => [rows[0]]);
  await assert.rejects(readAchievementCatalog("test"), error => error.code === "unavailable");
});

test("unreadable index, damaged blob and malformed published Title never become an absent feature", async t => {
  for (const value of [new Error("index denied"), new Error("blob missing"), new Error("hash mismatch"), []]) {
    const mock = t.mock.method(specs, "readOptionalSpecRows", async () => {if (value instanceof Error) throw value; return value;});
    await assert.rejects(loadAchievementTitles("test"), error => error.code === "unavailable");
    mock.mock.restore();
  }
});

// These authored CSVs contain no quoted commas; reject that explicitly if their format changes.
function csv(name) {
  const lines = readFileSync(resolve(__dirname, "../../docs/SpecData/" + name + "_sheet.csv"), "utf8")
    .replace(/^\uFEFF/, "").trim().split(/\r?\n/);
  const headers = lines[1].split(","), types = lines[2].split(",");
  return {headers, rows: lines.slice(3).map(line => {
    assert.ok(!line.includes('"'), "Extend test CSV parser for quoted fields");
    const values = line.split(","); assert.equal(values.length, headers.length);
    return Object.fromEntries(headers.map((key, i) => [key, types[i] === "string" ? values[i] : Number(values[i])]));
  })};
}

test("CSV migration preserves all 61 currency rewards and all eight title-stage associations", () => {
  const achievements = csv("Achievement"), titleTable = csv("Title"), rewardTable = csv("Reward");
  assert.ok(!achievements.headers.includes("rewardCurrency"));
  assert.ok(!achievements.headers.includes("rewardAmount"));
  assert.deepEqual(titleTable.headers, ["id", "titleId"]);
  assert.deepEqual(rewardTable.headers.filter(h => !h.startsWith("#")),
    ["id", "ownerType", "ownerId", "order", "rewardType", "rewardId", "amount"]);
  const authored = rewardTable.rows.filter(r => r.ownerType === "Achievement");
  const groups = ["win", "destroy", ...["Brand", "Bulk", "Caretaker", "Flow", "Legacy", "Predator", "Scale", "Trace"]
    .map(id => "synergy." + id), "streak", "pack"];
  const stageRewards = [["Gold", 100], ["Shard", 20], ["Diamond", 5], ["Gold", 500], ["Diamond", 20]];
  const expectedCurrencies = [...groups.flatMap(group => stageRewards.map(([currency, amount], i) =>
    [group + "." + (i + 1), currency, amount])), ["album.1", "Gold", 100]];
  const actualCurrencies = authored.filter(r => r.rewardType === "Currency").map(r => [r.ownerId, r.rewardId, r.amount]);
  assert.equal(actualCurrencies.length, 61);
  assert.deepEqual(actualCurrencies.sort(), expectedCurrencies.sort());
  const expectedTitles = [["win.1", "test_first_step"], ["win.2", "test_blue_traveler"],
    ["pack.2", "test_golden_collector"], ["streak.1", "test_dawn_guardian"], ["win.4", "test_long_journey"],
    ["destroy.2", "test_silent_sword"], ["album.1", "test_small_universe"], ["win.5", "test_endless_adventure"]];
  const actualTitles = authored.filter(r => r.rewardType === "Title");
  assert.equal(actualTitles.length, 8); assert.ok(actualTitles.every(r => r.amount === 1));
  assert.deepEqual(actualTitles.map(r => [r.ownerId, r.rewardId]).sort(), expectedTitles.sort());
  assert.deepEqual(new Set(parseTitles(titleTable.rows).map(r => r.titleId)), new Set(actualTitles.map(r => r.rewardId)));
  assert.equal(parseAchievementCatalog(achievements.rows, parseRewardRows(rewardTable.rows)).length, 61);
});

test("local CSV-only publisher parses the migrated Achievement and Title schemas without generated bytes", () => {
  const {csvOnlyRows} = require("./publish-local-spec");
  const read = name => readFileSync(resolve(__dirname, "../../docs/SpecData/" + name + "_sheet.csv"), "utf8");
  const fields = [["int", "id"], ["string", "achievementId"], ["string", "groupId"], ["int", "stage"],
    ["string", "eventKey"], ["string", "synergyId"], ["int", "targetCount"], ["string", "title"],
    ["string", "description"], ["int", "sortOrder"], ["int", "enabled"]];
  const achievements = csvOnlyRows("Achievement", fields, read("Achievement"));
  const titleRows = csvOnlyRows("Title", [["int", "id"], ["string", "titleId"]], read("Title"));
  assert.equal(achievements.length, 61);
  assert.equal(titleRows.length, 8);
  assert.deepEqual(achievements, csv("Achievement").rows);
  assert.deepEqual(parseTitles(titleRows), parseTitles(csv("Title").rows));
});
