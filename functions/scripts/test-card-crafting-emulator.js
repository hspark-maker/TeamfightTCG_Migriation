"use strict";
// Real transactions are exercised only against a local, disposable demo project.
const assert = require("node:assert/strict");
const {test, after} = require("node:test");
const {randomUUID} = require("node:crypto");
if (!/^(127\.0\.0\.1|localhost):\d+$/.test(process.env.FIRESTORE_EMULATOR_HOST ?? "") ||
    !String(process.env.GCLOUD_PROJECT ?? "").startsWith("demo-")) {
  throw new Error("A local Firestore emulator and demo-* project are required.");
}
const {db} = require("../lib/firebaseApp");
const specs = require("../lib/specs/specBlobReader");
const saves = require("../lib/save/saveDocument");
const missions = require("../lib/missions/missionSpec");
const {craftCard} = require("../lib/commands/craftCard");
const {getCardCrafting} = require("../lib/commands/getCardCrafting");
const {openPack} = require("../lib/commands/openPack");
after(() => db.terminate());

const cardRows = ["Common", "Common", "Rare", "Arcane", "Mythic", "Common"].map((grade, index) => ({
  id: index + 1, grade, channel: index === 5 ? "Test" : "Live", synergies: "Data_Synergy_Caretaker",
}));
const recipeRows = ["Common", "Rare", "Arcane", "Mythic"].map((grade, index) => ({
  id: index + 1, grade, cost: [20, 40, 100, 200][index], enabled: 1,
}));
const guideCatalog = [{id: "guide.caretaker", period: "guide", event: "Guide.CaretakerCardsAtStar1", target: 1,
  title: "Collect", description: "Collect a developed caretaker", passExp: 0, accountExp: 0,
  sortOrder: 1, guideActId: 1, guideActName: "Collection", enabled: true}];

async function setup(t, options = {}) {
  const uid = "craft-test-" + randomUUID();
  const env = options.env ?? "test";
  const root = db.doc(`envs/${env}/users/${uid}`);
  const authored = {recipes: recipeRows.map((row) => ({...row})), cards: cardRows.map((row) => ({...row}))};
  const table = (_env, name) => {
    if (name === "CardCraft") return authored.recipes;
    if (name === "Card") return authored.cards;
    if (name === "AlbumEntry") return [{id: 1, themeId: "crafted", pageId: "one", cardId: 4}];
    if (name === "AlbumThemeInfo") return [{id: 1, themeId: "crafted", locked: 0}];
    if (name === "Achievement") return [{id: 1, achievementId: "album.1", groupId: "album", stage: 1,
      eventKey: "CompleteAlbum", synergyId: "", targetCount: 1, title: "Albums", description: "Complete themes",
      rewardCurrency: "Shard", rewardAmount: 3, sortOrder: 1, enabled: 1}];
    if (name === "CardPack") return [{id: 1, packId: "craft-race", price: 10, priceType: "Gold", drawCount: 1}];
    if (name === "CardPackDrop") return [{id: 1, packId: "craft-race", cardId: 2, weight: 1}];
    if (name === "Reward") return [{id: 1, ownerType: "CardDuplicate", ownerId: "Common", amount: 1,
      rewardType: "Currency", rewardId: "CardDust", order: 1}];
    if (name === "RankGrade") return ["Bronze", "Silver", "Gold", "Platinum", "Diamond"].map((gradeKey, index) => ({
      id: index + 1, gradeKey, entryPoints: 100 + index * 160,
    }));
    if (name === "PassSeason") return [];
    throw new Error("Unexpected table: " + name);
  };
  t.mock.method(specs, "readSpecRows", async (...args) => table(...args));
  t.mock.method(specs, "readPinnedSpecRows", async (...args) => table(...args));
  t.mock.method(missions, "readMissionCatalog", async () => guideCatalog);
  await Promise.all([
    root.collection("save").doc("current").set({schemaVersion: 8, revision: 1,
      ownership: {cardIds: [1]}, cardGrowth: {entries: {"1": {level: 3, shardProgress: 7}}},
      profile: {nickname: "keep-me"}}),
    root.collection("wallet").doc("current").set({rev: 1, balances: {
      Gold: 100, Shard: 37, CardDust: options.dust ?? 500,
    }}),
    root.collection("achievements").doc("current").set({schemaVersion: 1, revision: 1,
      progress: {}, currentWinStreak: 0, claimed: {}}),
  ]);
  const request = (cardId = 2, txId = randomUUID()) => ({auth: {uid}, data: {env, cardId, txId}});
  const refs = ["save", "wallet", "achievements", "missions"].map((name) => root.collection(name).doc("current"));
  const read = async () => (await db.getAll(...refs)).map((snapshot) => snapshot.data());
  return {uid, env, root, authored, request, read};
}

const rejectedFor = (reason) => (error) => error.details?.reason === reason;
const rejectedCode = (code) => (error) => error.code === code;

test("craft validates authentication, environment, numeric cardId and required receipt before mutation", async (t) => {
  const {request, read} = await setup(t);
  const before = await read();
  await assert.rejects(() => craftCard.run({data: request().data}), rejectedCode("unauthenticated"));
  const invalid = [{env: "other"}, ...[0, -1, 1.5, "2", null, Number.MAX_SAFE_INTEGER + 1].map((cardId) => ({cardId})),
    ...[undefined, "", "short", "a/bbbbbbb", ".".repeat(8), "x".repeat(129), 12345678].map((txId) => ({txId}))];
  for (const patch of invalid) {
    const input = request(); input.data = {...input.data, ...patch};
    await assert.rejects(() => craftCard.run(input), rejectedCode("invalid-argument"));
  }
  await assert.rejects(() => craftCard.run({...request(), auth: {uid: "uninitialized-" + randomUUID()}}),
    rejectedCode("failed-precondition"));
  assert.deepEqual(await read(), before);
});

test("craft atomically pays the authored price, grants ownership and preserves existing growth and other currencies", async (t) => {
  const {request, read, authored} = await setup(t);
  authored.recipes[0].cost = 7;
  const result = await craftCard.run(request());
  const [save, wallet] = await read();
  assert.equal(result.cardId, 2); assert.equal(result.currency, "CardDust"); assert.equal(result.cost, 7);
  assert.equal(result.revision, 2); assert.deepEqual(save.ownership.cardIds, [1, 2]);
  assert.deepEqual(save.cardGrowth.entries["1"], {level: 3, shardProgress: 7});
  assert.equal(save.profile.nickname, "keep-me"); assert.equal(wallet.balances.CardDust, 493);
  assert.equal(wallet.balances.Shard, 37); assert.equal(wallet.balances.Gold, 100);
  assert.equal(result.wallet.balances.CardDust, 493);
});

test("insufficient currency and already-owned cards reject without any writes", async (t) => {
  const {request, read} = await setup(t, {dust: 19});
  const before = await read();
  await assert.rejects(() => craftCard.run(request()), rejectedFor("NotAffordable"));
  await assert.rejects(() => craftCard.run(request(1)), rejectedFor("AlreadyOwned"));
  assert.deepEqual(await read(), before);
});

test("identical concurrent retries charge once and return the identical receipt", async (t) => {
  const {request, read} = await setup(t);
  const input = request();
  const [a, b] = await Promise.all([craftCard.run(input), craftCard.run(input)]);
  assert.deepEqual(a, b);
  const [save, wallet] = await read();
  assert.equal(save.revision, 2); assert.equal(wallet.rev, 2); assert.equal(wallet.balances.CardDust, 480);
  assert.equal(save.ownership.cardIds.filter((id) => id === 2).length, 1);
});

test("different receipt IDs racing on the same card charge only the winner", async (t) => {
  const {request, read} = await setup(t);
  const results = await Promise.allSettled([craftCard.run(request()), craftCard.run(request())]);
  assert.equal(results.filter((result) => result.status === "fulfilled").length, 1);
  assert.equal(results.find((result) => result.status === "rejected").reason.details.reason, "AlreadyOwned");
  const [save, wallet] = await read();
  assert.equal(save.revision, 2); assert.equal(wallet.balances.CardDust, 480);
});

test("different cards racing for one wallet cannot overspend", async (t) => {
  const {request, read} = await setup(t, {dust: 40});
  const results = await Promise.allSettled([craftCard.run(request(2)), craftCard.run(request(3))]);
  assert.equal(results.filter((result) => result.status === "fulfilled").length, 1);
  assert.equal(results.find((result) => result.status === "rejected").reason.details.reason, "NotAffordable");
  const [save, wallet] = await read();
  const winner = results.find((result) => result.status === "fulfilled").value;
  assert.equal(wallet.balances.CardDust, 40 - winner.cost);
  assert.deepEqual(save.ownership.cardIds, [1, winner.cardId]); assert.equal(save.revision, 2);
});

test("successful receipts replay after recipes are disabled or unavailable; changed arguments cannot reuse them", async (t) => {
  const {request, read, authored} = await setup(t);
  const input = request();
  const first = await craftCard.run(input);
  authored.recipes[0].enabled = 0;
  assert.deepEqual(await craftCard.run(input), first);
  await assert.rejects(() => craftCard.run(request(3, input.data.txId)), rejectedFor("TxIdReused"));
  t.mock.method(specs, "readSpecRows", async () => { throw new Error("spec unavailable"); });
  assert.deepEqual(await craftCard.run(input), first);
  assert.equal((await read())[1].balances.CardDust, 480);
});

test("failure after queued crafting writes rolls back wallet, ownership, progress and receipts together", async (t) => {
  const {request, read, root} = await setup(t);
  const before = await read();
  const real = saves.mutateSave;
  t.mock.method(saves, "mutateSave", (env, uid, source, receipt, mutate, ...rest) =>
    real(env, uid, source, receipt, async (...args) => { await mutate(...args); throw new Error("injected abort"); }, ...rest));
  await assert.rejects(() => craftCard.run(request(4)), /injected abort/);
  assert.deepEqual(await read(), before);
  assert.equal((await root.collection("wallet").doc("current").collection("receipts").get()).size, 0);
});

test("Common and Rare use base level; Arcane and Mythic receive their existing initial growth", async (t) => {
  const {request, read} = await setup(t);
  for (const id of [2, 3, 4, 5]) await craftCard.run(request(id));
  const entries = (await read())[0].cardGrowth.entries;
  assert.equal(entries["2"]?.level ?? 1, 1); assert.equal(entries["3"]?.level ?? 1, 1);
  assert.equal(entries["4"].level, 2); assert.equal(entries["5"].level, 2);
});

test("crafting immediately advances album and ownership guides without pack progress", async (t) => {
  const {request, read} = await setup(t);
  const first = await craftCard.run(request(4));
  const [, , achievements, missionState] = await read();
  assert.equal(first.achievements.progress.CompleteAlbum, 1);
  assert.equal(achievements.progress.CompleteAlbum, 1); assert.equal(achievements.progress.OpenPack ?? 0, 0);
  assert.equal(first.missions.progress["guide.Guide.CaretakerCardsAtStar1"], 2);
  assert.equal(missionState.progress["daily.OpenPack"] ?? 0, 0);
  assert.equal(missionState.progress["weekly.OpenPack"] ?? 0, 0);
});

test("paid pack acquisition racing with crafting grants ownership once and accounts for a duplicate exactly", async (t) => {
  const {request, read} = await setup(t, {dust: 100});
  const packInput = request(); packInput.data.packId = "craft-race";
  const [craft, pack] = await Promise.allSettled([craftCard.run(request()), openPack.run(packInput)]);
  assert.equal(pack.status, "fulfilled", String(pack.reason));
  const [save, wallet, achievements] = await read();
  assert.equal(save.ownership.cardIds.filter((id) => id === 2).length, 1);
  assert.equal(wallet.balances.Gold, 90); assert.equal(achievements.progress.OpenPack, 1);
  if (craft.status === "fulfilled") {
    assert.equal(pack.value.cards[0].isNew, false); assert.equal(wallet.balances.CardDust, 81);
  } else {
    assert.equal(craft.reason.details?.reason, "AlreadyOwned");
    assert.equal(pack.value.cards[0].isNew, true); assert.equal(wallet.balances.CardDust, 100);
  }
});

test("test catalog includes Test-channel cards and disabled recipes cannot list or craft", async (t) => {
  const {request, authored, read} = await setup(t);
  authored.recipes[1].enabled = 0;
  const catalog = await getCardCrafting.run(request());
  assert.equal(catalog.currency, "CardDust");
  assert.equal(catalog.cards.find((card) => card.cardId === 6).cost, 20);
  assert.equal(catalog.cards.some((card) => card.cardId === 3), false);
  const before = await read();
  await assert.rejects(() => craftCard.run(request(3)), rejectedFor("CardNotCraftable"));
  await assert.rejects(() => craftCard.run(request(9999)), rejectedFor("CardNotCraftable"));
  assert.deepEqual(await read(), before);
  await craftCard.run(request(6));
});

test("a later paid duplicate of a crafted card grants CardDust once without changing Shard or ownership", async (t) => {
  const {request, read} = await setup(t, {dust: 100});
  await craftCard.run(request());
  const input = request(); input.data.packId = "craft-race";
  const first = await openPack.run(input);
  assert.deepEqual(await openPack.run(input), first);
  assert.deepEqual(first.granted, [{currency: "CardDust", amount: 1}]);
  assert.equal(first.cards[0].isNew, false);
  const [save, wallet, achievements] = await read();
  assert.deepEqual(save.ownership.cardIds, [1, 2]); assert.equal(save.revision, 3);
  assert.equal(wallet.balances.CardDust, 81); assert.equal(wallet.balances.Shard, 37);
  assert.equal(wallet.balances.Gold, 90); assert.equal(achievements.progress.OpenPack, 1);
});

test("live catalog excludes Test-channel cards and requests cannot cross the environment boundary", async (t) => {
  const {request, read} = await setup(t, {env: "live"});
  const catalog = await getCardCrafting.run(request());
  assert.equal(catalog.cards.some((card) => card.cardId === 6), false);
  const before = await read();
  await assert.rejects(() => craftCard.run(request(6)), rejectedFor("CardNotCraftable"));
  const other = request(); other.data.env = "test";
  await assert.rejects(() => craftCard.run(other), rejectedCode("failed-precondition"));
  assert.deepEqual(await read(), before);
});

test("catalog validates auth and env, and malformed or missing recipes fail closed without any writes", async (t) => {
  const {request, authored, read} = await setup(t);
  await assert.rejects(() => getCardCrafting.run({data: {env: "test"}}), rejectedCode("unauthenticated"));
  await assert.rejects(() => getCardCrafting.run({...request(), data: {env: "other"}}), rejectedCode("invalid-argument"));
  const before = await read();
  for (const cost of [0, -1, 0.5, Number.MAX_SAFE_INTEGER + 1]) {
    authored.recipes[0].cost = cost;
    await assert.rejects(() => craftCard.run(request()), rejectedCode("unavailable"));
    await assert.rejects(() => getCardCrafting.run(request()), rejectedCode("unavailable"));
  }
  authored.recipes = [];
  await assert.rejects(() => craftCard.run(request()), rejectedCode("unavailable"));
  assert.deepEqual(await read(), before);
});
