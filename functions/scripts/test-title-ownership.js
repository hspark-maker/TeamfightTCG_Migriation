"use strict";
const assert = require("node:assert/strict");
const {test} = require("node:test");
const path = require("node:path");
const {readFileSync} = require("node:fs");
const output = process.env.TITLE_TEST_BUILD || "../lib";
const {parseTitles, grantTitle} = require(path.resolve(__dirname, output, "titles/titleOwnership"));
const manual = row => ({eventKey: "", synergyId: "", targetCount: 0, description: "Manual award", ...row});
const catalog = parseTitles([manual({id: 1, titleId: "first"}), manual({id: 2, titleId: "second"})]);

test("title catalog rejects malformed and duplicate identifiers", () => {
  for (const rows of [[], [{id: 0, titleId: "a"}], [{id: 1, titleId: " a"}],
    [{id: 1, titleId: ""}], [{id: 2147483648, titleId: "a"}],
    [{id: 1, titleId: "a"}, {id: 2, titleId: "a"}],
    [{id: 1, titleId: "a"}, {id: 1, titleId: "b"}]]) assert.throws(() => parseTitles(rows.map(manual)));
});

test("grants preserve profile and unknown ownership without equipping or mutating input", () => {
  const profile = {nickname: "name", accountExp: 12, equippedTitleId: "future", ownedTitleIds: ["future"],
    ownedAvatarIds: ["avatar"]};
  const first = grantTitle(profile, "first", catalog);
  assert.deepEqual(first.title, {titleId: "first", isNew: true});
  assert.deepEqual(first.profile, {...profile, ownedTitleIds: ["future", "first"]});
  assert.deepEqual(profile.ownedTitleIds, ["future"]);
  const repeated = grantTitle(first.profile, "first", catalog);
  assert.equal(repeated.title.isNew, false);
  assert.deepEqual(repeated.profile, first.profile);
  assert.deepEqual(grantTitle(repeated.profile, "second", catalog).profile.ownedTitleIds, ["future", "first", "second"]);
});

test("legacy accounts get only the explicitly granted title", () => {
  assert.deepEqual(grantTitle({}, "first", catalog).profile, {ownedTitleIds: ["first"]});
  assert.throws(() => grantTitle({}, "unknown", catalog));
  assert.throws(() => grantTitle({ownedTitleIds: [2]}, "first", catalog));
});

test("Title CSV preserves all authored display IDs", () => {
  const root = path.resolve(__dirname, "../..");
  const csv = readFileSync(path.join(root, "docs/SpecData/Title_sheet.csv"), "utf8");
  assert.equal(csv.charCodeAt(0), 0xfeff);
  const rows = csv.trim().split(/\r?\n/).slice(3).map((line) => {
    const [id, titleId, eventKey, synergyId, targetCount, description] = line.split(",");
    return {id: Number(id), titleId, eventKey, synergyId, targetCount: Number(targetCount), description};
  });
  const display = readFileSync(path.join(root, "Assets/SO/Profile/TitleCatalog.asset"), "utf8");
  assert.deepEqual(parseTitles(rows).map((row) => row.titleId),
    [...display.matchAll(/^  - id: (.+)$/gm)].map((match) => match[1].trim()));
});

test("Title is rejected by the shared Reward table and item grant", () => {
  const {resolveRewards} = require(path.resolve(__dirname, output, "rewardTable"));
  const row = {id: 1, ownerType: "Achievement", ownerId: "win.1", order: 1, rewardType: "Title", rewardId: "first", amount: 1};
  const parsed = resolveRewards([row], "Achievement", "win.1");
  assert.deepEqual(parsed.items, []);
  assert.equal(parsed.dropped[0].reason, "UnknownRewardType");
  const {grantRewardItems} = require(path.resolve(__dirname, output, "rewards/itemGrant"));
  const context = {catalog: new Set(), grades: new Map(), thresholds: [], packs: new Map(), choices: [], cards: []};
  assert.throws(() => grantRewardItems({}, [row], context, [], "", 0));
});

test("independent title grants retain shared cosmetic and experience profile fields", () => {
  const profile = {accountExp: 100, ownedAvatarIds: ["avatar"], equippedTitleId: "", nickname: "kept"};
  const title = grantTitle(profile, "first", catalog);
  assert.deepEqual(title.profile, {...profile, ownedTitleIds: ["first"]});
  assert.deepEqual(title.title, {titleId: "first", isNew: true});
});
