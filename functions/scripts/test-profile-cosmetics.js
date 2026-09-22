"use strict";
const assert = require("node:assert/strict");
const {test} = require("node:test");
const {readFileSync} = require("node:fs");
const {parseCosmeticItems, ensureCosmeticOwnership, grantCosmetic, assertCosmeticPurchasable,
  loadCosmeticItems} = require("../lib/profile/cosmetics");
const {grantRewardItems} = require("../lib/rewards/itemGrant");
const {grantAccountExperience} = require("../lib/account/accountProgression");
const {resolveRewards} = require("../lib/rewardTable");
const {buildFreshAccountSlots} = require("../lib/save/freshAccount");
const specs = require("../lib/specs/specBlobReader");
const base = [{id: 1, itemType: "Avatar", itemId: "base", defaultOwned: 1},
  {id: 2, itemType: "Frame", itemId: "base-frame", defaultOwned: 1},
  {id: 3, itemType: "Emote", itemId: "1", defaultOwned: 1},
  {id: 4, itemType: "Avatar", itemId: "premium", defaultOwned: 0},
  {id: 5, itemType: "Frame", itemId: "premium-frame", defaultOwned: 0}];
const cosmetics = parseCosmeticItems(base);
const context = {cosmetics, catalog: new Set([1]), grades: new Map([[1, "Common"]]),
  thresholds: [0], packs: new Map(), choices: [], cards: []};
const item = (rewardType="Avatar", rewardId="premium", amount=1) => ({rewardType, rewardId, amount});
const row = {...item(), id: 1, ownerType: "AccountLevel", ownerId: "2", order: 1};

test("catalog rejects malformed, duplicate, unknown-type and noncanonical emote rows", () => {
  for (const rows of [[], [...base, base[0]], [{...base[0], defaultOwned: 2}],
    [{...base[0], itemType: "Title"}], [{...base[2], itemId: "01"}], [{...base[2], itemId: "0"}],
    [{...base[0], id: 2147483648}], [{...base[2], itemId: "2147483648"}],
    [{...base[0], id: 1.5}], [base[0], {...base[0], id: 99}]]) assert.throws(() => parseCosmeticItems(rows));
});
test("new and legacy defaults preserve unknown awards and all unrelated profile fields, idempotently", () => {
  const profile = {nickname: "kept", avatarId: "future", titleId: "title", accountExp: 9,
    ownedAvatarIds: ["future"], emoteIds: [1,0,0,0,0,0]};
  assert.throws(() => ensureCosmeticOwnership({ownedEmoteIds: [2147483648]}, cosmetics));
  const next = ensureCosmeticOwnership(profile, cosmetics);
  assert.deepEqual(next.ownedAvatarIds, ["future", "base"]);
  assert.equal(next.avatarId, "future");
  assert.equal(next.titleId, "title");
  assert.equal(next.accountExp, 9);
  assert.deepEqual(next, ensureCosmeticOwnership(next, cosmetics));
  assert.deepEqual(buildFreshAccountSlots([], "name", new Map(), cosmetics).profile.ownedEmoteIds, [1]);
  assert.deepEqual(profile.ownedAvatarIds, ["future"]);
});
test("permanent grants distinguish duplicates without compensation or automatic equip", () => {
  const profile = {avatarId: "base"};
  const first = grantCosmetic(profile, "Avatar", "premium", cosmetics);
  assert.equal(first.cosmetic.isNew, true);
  assert.equal(first.profile.avatarId, "base");
  const again = grantCosmetic(first.profile, "Avatar", "premium", cosmetics);
  assert.equal(again.cosmetic.isNew, false);
  assert.deepEqual(first.profile, again.profile);
  assert.throws(() => grantCosmetic(profile, "Frame", "premium", cosmetics));
  assert.throws(() => grantCosmetic(profile, "Avatar", "unknown", cosmetics));
  assert.throws(() => assertCosmeticPurchasable(first.profile, "Avatar", "premium", cosmetics),
    error => error.details.reason === "AlreadyOwned");
  assert.throws(() => assertCosmeticPurchasable({}, "Avatar", "base", cosmetics));
  assert.doesNotThrow(() => assertCosmeticPurchasable({}, "Avatar", "premium", cosmetics));
});
test("reward parser and common grant enforce amount one and merge card ownership", () => {
  assert.equal(resolveRewards([{...row, amount: 2}], "AccountLevel", "2").items.length, 0);
  assert.equal(resolveRewards([{...row, rewardType: "Emote", rewardId: "0"}], "AccountLevel", "2").items.length, 0);
  assert.throws(() => grantRewardItems({}, [item("Avatar", "premium", 2)], context, [], "", 0));
  const result = grantRewardItems({profile: {nickname: "kept"}}, [item(), item(), item("Card", "1")], context, [], "", 0);
  assert.deepEqual(result.cosmetics.map(x => x.isNew), [true, false]);
  assert.deepEqual(result.slots.ownership.cardIds, [1]);
  assert.deepEqual(result.currencies, []);
  assert.equal(result.slots.profile.nickname, "kept");
});
test("level-up profile merge retains the cosmetic awarded in the same transaction", () => {
  const result = grantAccountExperience({profile: {accountExp: 90, accountRewardLevel: 1, nickname: "kept"}}, 20,
    {levels: [{id: 1, requiredExp: 0}, {id: 2, requiredExp: 100}], rewardRows: [row], itemContext: context}, 0);
  assert.deepEqual(result.slots.profile.ownedAvatarIds, ["base", "premium"]);
  assert.equal(result.slots.profile.accountExp, 100);
  assert.equal(result.slots.profile.accountRewardLevel, 2);
  assert.equal(result.cosmetics[0].isNew, true);
});
test("unpublished indexed table fails explicitly; no implicit ownership fallback", async t => {
  t.mock.method(specs, "readSpecRows", async () => {throw new Error("no indexed table");});
  await assert.rejects(loadCosmeticItems("test"), e => e.code === "unavailable" &&
    e.details.reason === "CosmeticCatalogUnavailable");
});
test("authored CSV grants exactly the approved existing 31 cosmetics", () => {
  const lines = readFileSync(require("node:path").resolve(__dirname, "../../docs/SpecData/CosmeticItem_sheet.csv"), "utf8")
    .trim().split(/\r?\n/).slice(3);
  const parsed = parseCosmeticItems(lines.map(line => {const [id,itemType,itemId,defaultOwned] = line.split(",");
    return {id:Number(id),itemType,itemId,defaultOwned:Number(defaultOwned)};}));
  const defaults = ensureCosmeticOwnership({}, parsed);
  assert.equal(defaults.ownedAvatarIds.length, 3);
  assert.equal(defaults.ownedFrameIds.length, 7);
  assert.equal(defaults.ownedEmoteIds.length, 21);
});
