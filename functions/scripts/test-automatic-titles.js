"use strict";
const assert = require("node:assert/strict");
const {test} = require("node:test");
const {resolve} = require("node:path");
const output = process.env.TITLE_TEST_BUILD || "../lib";
const built = name => require(resolve(__dirname, output, name));
const {grantAutomaticTitles, loadAutomaticTitles, titleConditionDefinitions} = built("titles/automaticTitles");
const {parseTitles} = built("titles/titleOwnership");
const {readStatistics} = built("statistics/playerStatistics");
const specs = built("specs/specBlobReader");
const row = (id, eventKey, targetCount, synergyId = "") => ({id, titleId: "title_" + id,
  eventKey, targetCount, synergyId, description: "Server condition " + targetCount});
const rows = [row(1, "WinBattle", 1), row(2, "WinBattle", 10), row(3, "WinStreak", 2),
  row(4, "OpenPack", 10), row(5, "CompleteAlbum", 1), row(6, "PlaySynergy", 1, "Brand"), row(7, "", 0)];
const context = {catalog: parseTitles(rows), entries: [], themes: []};
const legacy = {revision: 1, progress: {}, claimed: {}, currentWinStreak: 0};

test("independent targets grant without achievement claims; manual awards never grant automatically", () => {
  const stats = readStatistics({lifetime: {wins: 10, bestWinStreak: 2, synergyPlays: {Brand: 1}}}, legacy, 1);
  const profile = {nickname: "kept", ownedAvatarIds: ["a"], equippedTitleId: ""};
  const awarded = grantAutomaticTitles(profile, stats, context);
  assert.deepEqual(awarded.titles.map(t => t.titleId), ["title_1", "title_2", "title_3", "title_6"]);
  assert.equal(awarded.profile.nickname, "kept");
  assert.deepEqual(awarded.profile.ownedAvatarIds, ["a"]);
  assert.equal(awarded.profile.equippedTitleId, "");
  stats.lifetime.currentWinStreak = 0;
  assert.deepEqual(grantAutomaticTitles(awarded.profile, stats, context).titles, []);
  assert.deepEqual(profile.ownedTitleIds, undefined);
});

test("missing fields, malformed conditions, duplicate IDs and unsafe targets fail closed", () => {
  const invalid = [{eventKey: undefined}, {eventKey: "Unknown"}, {eventKey: "", targetCount: 1},
    {targetCount: 0}, {targetCount: -1}, {targetCount: 1.5}, {targetCount: "1"}, {targetCount: 2147483648},
    {synergyId: "Brand"}, {eventKey: "PlaySynergy", synergyId: ""}, {eventKey: "PlaySynergy", synergyId: "bad/key"},
    {description: ""}, {description: " padded "}, {description: undefined}];
  for (const patch of invalid) assert.throws(() => parseTitles([{...rows[0], ...patch}]));
  assert.throws(() => parseTitles([rows[0], {...rows[1], titleId: rows[0].titleId}]));
  assert.throws(() => parseTitles([rows[0], {...rows[1], id: rows[0].id}]));
  assert.deepEqual(titleConditionDefinitions(context)[1], {titleId: "title_2", description: "Server condition 10"});
});

test("absent Title index disables only titles without querying Reward, Achievement or albums", async t => {
  t.mock.method(specs, "readOptionalSpecRows", async (_env, table) => {assert.equal(table, "Title"); return null;});
  t.mock.method(specs, "readSpecRows", async (_env, table) => {throw new Error("Unexpected table " + table);});
  assert.equal(await loadAutomaticTitles("live"), null);
});

test("Title catalog itself supplies targets and text without Reward or Achievement", async t => {
  t.mock.method(specs, "readOptionalSpecRows", async () => [rows[1]]);
  t.mock.method(specs, "readSpecRows", async (_env, table) => {throw new Error("Unexpected table " + table);});
  const loaded = await loadAutomaticTitles("test");
  assert.equal(loaded.catalog[0].targetCount, 10);
  assert.equal(titleConditionDefinitions(loaded)[0].description, "Server condition 10");
});

test("unreadable index, damaged blob and malformed published Title never become an absent feature", async t => {
  for (const result of [new Error("index denied"), new Error("blob missing"), new Error("hash mismatch"), []]) {
    const mock = t.mock.method(specs, "readOptionalSpecRows", async () => {if (result instanceof Error) throw result; return result;});
    await assert.rejects(loadAutomaticTitles("test"), error => error.code === "unavailable" &&
      error.details.reason === "TitleConditionsUnavailable");
    mock.mock.restore();
  }
});
