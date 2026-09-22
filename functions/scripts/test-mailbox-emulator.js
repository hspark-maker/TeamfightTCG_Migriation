"use strict";
// Exercise real callable transactions only in a local disposable demo project.
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
const {sendMail, getMailbox, claimMail, claimAllMail} = require("../lib/commands/mailbox");
after(() => db.terminate());

const rejectedFor = (reason) => (error) => error.details?.reason === reason;
const rejectedCode = (code) => (error) => error.code === code;
const coins = (amount = 100) => ({currencies: [{currency: "Gold", amount}], items: []});
const future = () => Date.now() + 86400000;

async function setup() {
  const uid = "mail-test-" + randomUUID();
  const env = "test";
  const root = db.doc(`envs/${env}/users/${uid}`);
  await Promise.all([
    root.collection("save").doc("current").set({schemaVersion: 8, revision: 1,
      ownership: {cardIds: [1]}, cardGrowth: {entries: {"1": {level: 3, shardProgress: 7}}},
      profile: {nickname: "preserved"}}),
    root.collection("wallet").doc("current").set({rev: 1, balances: {Gold: 1000, Shard: 7, CardDust: 0}}),
  ]);
  const request = (data = {}) => ({auth: {uid}, data: {env, ...data}});
  const issue = (mailId = randomUUID(), patch = {}) => ({auth: {uid: "mail-test-admin", token: {admin: true}},
    data: {env, uid, mailId, title: "Test mail", body: "Mail reward", expiresAtMs: future(), rewards: coins(), ...patch}});
  const deliver = async (mailId = randomUUID(), patch = {}) => {
    await sendMail.run(issue(mailId, patch));
    return mailId;
  };
  const claim = (mailId, txId = randomUUID()) => request({mailId, txId});
  const all = (txId = randomUUID()) => request({txId});
  const read = async () => (await db.getAll(root.collection("save").doc("current"),
    root.collection("wallet").doc("current"))).map((snapshot) => snapshot.data());
  return {uid, env, root, request, issue, deliver, claim, all, read};
}

function mockItemSpecs(t) {
  const table = (_env, name) => {
    if (name === "Card") return [1, 2].map((id) => ({id, grade: "Common", channel: "Live",
      synergies: "Data_Synergy_Caretaker"}));
    if (name === "CardPack") return [{id: 1, packId: "mail-pack", price: 10, priceType: "Gold", drawCount: 1}];
    if (name === "CardPackDrop") return [{id: 1, packId: "mail-pack", cardId: 2, weight: 1}];
    if (name === "Reward") return [{id: 1, ownerType: "CardDuplicate", ownerId: "Common", amount: 3,
      rewardType: "Currency", rewardId: "CardDust", order: 1}];
    if (name === "RankGrade") return ["Bronze", "Silver", "Gold", "Platinum", "Diamond"].map((gradeKey, index) => ({
      id: index + 1, gradeKey, entryPoints: 100 + index * 160,
    }));
    if (["AlbumEntry", "AlbumThemeInfo", "Achievement", "PassSeason"].includes(name)) return [];
    throw new Error("Unexpected table: " + name);
  };
  t.mock.method(specs, "readSpecRows", async (...args) => table(...args));
  t.mock.method(specs, "readPinnedSpecRows", async (...args) => table(...args));
  t.mock.method(missions, "readMissionCatalog", async () => []);
}

test("sending requires an authenticated admin and an existing recipient", async () => {
  const {issue, root} = await setup();
  const input = issue();
  await assert.rejects(() => sendMail.run({data: input.data}), rejectedCode("unauthenticated"));
  for (const auth of [{uid: "player"}, {uid: "player", token: {}}, {uid: "player", token: {admin: "true"}}]) {
    await assert.rejects(() => sendMail.run({...input, auth}), rejectedCode("permission-denied"));
  }
  await assert.rejects(() => sendMail.run(issue(randomUUID(), {uid: "missing-" + randomUUID()})),
    rejectedCode("not-found"));
  assert.equal((await root.collection("mail").get()).size, 0);
});

test("invalid delivery identifiers, times and rewards never create mail", async () => {
  const {issue, root} = await setup();
  const invalid = [{env: "other"}, {uid: "x/y"}, {mailId: "x/y"}, {mailId: ""}, {title: ""},
    {expiresAtMs: Date.now() - 1000}, {expiresAtMs: "tomorrow"}, {expiresAtMs: Number.MAX_SAFE_INTEGER + 1},
    {rewards: coins(0)}, {rewards: coins(-1)}, {rewards: coins(0.5)},
    {rewards: {currencies: [{currency: "Unknown", amount: 1}], items: []}},
    {rewards: {currencies: [], items: [{rewardType: "PackChoice", rewardId: "UnlockedThemePack", amount: 1}]}},
    {rewards: {currencies: [], items: [{rewardType: "Card", rewardId: "2", amount: 101}]}}];
  for (const patch of invalid) {
    await assert.rejects(() => sendMail.run(issue(randomUUID(), patch)), rejectedCode("invalid-argument"));
  }
  assert.equal((await root.collection("mail").get()).size, 0);
});

test("stable delivery IDs create once under retries and cannot replace an existing reward", async () => {
  const {issue, root} = await setup();
  const input = issue();
  const results = await Promise.all([sendMail.run(input), sendMail.run(input)]);
  assert.deepEqual(results.map((result) => result.created).sort(), [false, true]);
  assert.equal(results[0].mailId, input.data.mailId);
  const before = (await root.collection("mail").doc(input.data.mailId).get()).data();
  assert.equal(typeof before.createdAtMs, "number"); assert.equal(before.claimedAtMs, null);
  await assert.rejects(() => sendMail.run({...input, data: {...input.data, rewards: coins(999)}}));
  assert.deepEqual((await root.collection("mail").doc(input.data.mailId).get()).data(), before);
});

test("mailbox and claims validate authentication, environment, pagination and receipt IDs", async () => {
  const {request, claim, all, read} = await setup();
  const before = await read();
  for (const callable of [getMailbox, claimMail, claimAllMail]) {
    await assert.rejects(() => callable.run({data: {env: "test"}}), rejectedCode("unauthenticated"));
    await assert.rejects(() => callable.run(request({env: "other"})), rejectedCode("invalid-argument"));
  }
  for (const limit of [0, -1, 51, 1.5, "10"]) {
    await assert.rejects(() => getMailbox.run(request({limit})), rejectedCode("invalid-argument"));
  }
  for (const cursor of [{}, {createdAtMs: 1}, {createdAtMs: 1, mailId: "x/y"},
    {createdAtMs: "1", mailId: "valid-mail"}]) {
    await assert.rejects(() => getMailbox.run(request({cursor})), rejectedCode("invalid-argument"));
  }
  for (const txId of ["", "short", "a/bbbbbbb", ".".repeat(8), "x".repeat(129), 12345678]) {
    await assert.rejects(() => claimMail.run(claim("valid-mail", txId)), rejectedCode("invalid-argument"));
    await assert.rejects(() => claimAllMail.run(all(txId)), rejectedCode("invalid-argument"));
  }
  assert.deepEqual(await read(), before);
});

test("recipients cannot list or claim another recipient's mail", async () => {
  const owner = await setup();
  const stranger = await setup();
  const mailId = await owner.deliver();
  const listing = await getMailbox.run(stranger.request({uid: owner.uid}));
  assert.deepEqual(listing.mails, []); assert.equal(listing.hasClaimable, false);
  await assert.rejects(() => claimMail.run(stranger.request({uid: owner.uid, mailId, txId: randomUUID()})),
    rejectedFor("MailNotFound"));
  assert.equal((await owner.read())[1].balances.Gold, 1000);
  assert.equal((await stranger.read())[1].balances.Gold, 1000);
});

test("currency claim needs no spec, atomically grants once and preserves other state", async (t) => {
  const {deliver, claim, root, read} = await setup();
  t.mock.method(specs, "readSpecRows", async () => { throw new Error("spec unavailable"); });
  t.mock.method(specs, "readPinnedSpecRows", async () => { throw new Error("spec unavailable"); });
  const mailId = await deliver();
  const result = await claimMail.run(claim(mailId));
  const [save, wallet] = await read();
  assert.deepEqual(result.claimedMailIds, [mailId]); assert.deepEqual(result.granted, coins().currencies);
  assert.deepEqual(result.cards, []); assert.deepEqual(result.packs, []);
  assert.equal(result.revision, 2); assert.equal(save.revision, 2); assert.equal(wallet.rev, 2);
  assert.equal(wallet.balances.Gold, 1100); assert.equal(wallet.balances.Shard, 7);
  assert.equal(save.profile.nickname, "preserved"); assert.deepEqual(save.ownership.cardIds, [1]);
  assert.deepEqual(save.cardGrowth.entries["1"], {level: 3, shardProgress: 7});
  assert.equal(result.wallet.balances.Gold, 1100);
  assert.equal(typeof (await root.collection("mail").doc(mailId).get()).data().claimedAtMs, "number");
});

test("identical concurrent receipt retries return the same response and pay once", async () => {
  const {deliver, claim, read} = await setup();
  const input = claim(await deliver());
  const [a, b] = await Promise.all([claimMail.run(input), claimMail.run(input)]);
  assert.deepEqual(a, b);
  assert.equal((await read())[1].balances.Gold, 1100); assert.equal((await read())[0].revision, 2);
});

test("different receipt IDs racing on one mail yield one grant", async () => {
  const {deliver, claim, read} = await setup();
  const mailId = await deliver();
  const results = await Promise.allSettled([claimMail.run(claim(mailId)), claimMail.run(claim(mailId))]);
  assert.equal(results.filter((result) => result.status === "fulfilled").length, 1);
  assert.equal(results.find((result) => result.status === "rejected").reason.details.reason, "MailAlreadyClaimed");
  assert.equal((await read())[1].balances.Gold, 1100);
});

test("receipt deletion cannot make claimed mail payable again", async () => {
  const {deliver, claim, root, read} = await setup();
  const input = claim(await deliver());
  await claimMail.run(input);
  const receipts = await root.collection("wallet").doc("current").collection("receipts").get();
  assert.equal(receipts.size, 1);
  await Promise.all(receipts.docs.map((snapshot) => snapshot.ref.delete()));
  await assert.rejects(() => claimMail.run(input), rejectedFor("MailAlreadyClaimed"));
  await assert.rejects(() => claimMail.run(claim(input.data.mailId)), rejectedFor("MailAlreadyClaimed"));
  assert.equal((await read())[1].balances.Gold, 1100);
});

test("a receipt cannot be reused for another mail or the bulk command", async () => {
  const {deliver, claim, all, read} = await setup();
  const [first, second] = await Promise.all([deliver(), deliver()]);
  const input = claim(first);
  await claimMail.run(input);
  await assert.rejects(() => claimMail.run(claim(second, input.data.txId)), rejectedFor("TxIdReused"));
  await assert.rejects(() => claimAllMail.run(all(input.data.txId)), rejectedFor("TxIdReused"));
  assert.equal((await read())[1].balances.Gold, 1100);
});

test("expired and missing mail reject without writes; listing exposes server-derived states", async () => {
  const {deliver, claim, request, root, read} = await setup();
  const [expired, claimed] = await Promise.all([deliver(), deliver()]);
  await root.collection("mail").doc(expired).update({expiresAtMs: Date.now() - 1000});
  await claimMail.run(claim(claimed));
  const before = await read();
  await assert.rejects(() => claimMail.run(claim(expired)), rejectedFor("MailExpired"));
  await assert.rejects(() => claimMail.run(claim("missing-mail")), rejectedFor("MailNotFound"));
  assert.deepEqual(await read(), before);
  const listing = await getMailbox.run(request());
  assert.equal(listing.mails.find((mail) => mail.mailId === expired).state, "Expired");
  assert.equal(listing.mails.find((mail) => mail.mailId === claimed).state, "Claimed");
  assert.equal(listing.hasClaimable, false); assert.equal(typeof listing.serverNowMs, "number");
});

test("pagination handles identical creation times without gaps and alert includes later pages", async () => {
  const {deliver, request, root} = await setup();
  const ids = ["page-a", "page-b", "page-c", "page-d", "page-e"];
  const createdAtMs = Date.now() - 1000;
  for (const id of ids) await deliver(id);
  await Promise.all(ids.map((id) => root.collection("mail").doc(id).update({createdAtMs})));
  const first = await getMailbox.run(request({limit: 2}));
  assert.equal(first.mails.length, 2); assert.ok(first.nextCursor);
  const second = await getMailbox.run(request({limit: 2, cursor: first.nextCursor}));
  const third = await getMailbox.run(request({limit: 2, cursor: second.nextCursor}));
  assert.equal(third.nextCursor, null);
  assert.deepEqual([...first.mails, ...second.mails, ...third.mails].map((mail) => mail.mailId).sort(), ids);
  await Promise.all(first.mails.map((mail) => root.collection("mail").doc(mail.mailId)
    .update({expiresAtMs: Date.now() - 1})));
  const page = await getMailbox.run(request({limit: 2}));
  assert.equal(page.mails.every((mail) => mail.state === "Expired"), true);
  assert.equal(page.hasClaimable, true);
});

test("bulk claims at most 20 earliest-expiring mails and drains the remainder once", async () => {
  const {deliver, all, read, root} = await setup();
  const expires = future();
  for (let i = 0; i < 23; i++) await deliver(`bulk-${String(i).padStart(2, "0")}`,
    {expiresAtMs: expires + i * 1000, rewards: coins(1)});
  const expired = await deliver("bulk-expired", {rewards: coins(999)});
  await root.collection("mail").doc(expired).update({expiresAtMs: Date.now() - 1000});
  const input = all();
  const first = await claimAllMail.run(input);
  assert.equal(first.claimedMailIds.length, 20);
  assert.deepEqual(first.claimedMailIds, Array.from({length: 20}, (_, i) => `bulk-${String(i).padStart(2, "0")}`));
  assert.deepEqual(await claimAllMail.run(input), first);
  const second = await claimAllMail.run(all());
  assert.deepEqual(second.claimedMailIds, ["bulk-20", "bulk-21", "bulk-22"]);
  assert.equal((await read())[1].balances.Gold, 1023); assert.equal((await read())[0].revision, 3);
  await assert.rejects(() => claimAllMail.run(all()), rejectedFor("MailNothingToClaim"));
});

test("bulk versus individual concurrency cannot double-grant any mail", async () => {
  const {deliver, claim, all, read} = await setup();
  const ids = await Promise.all([deliver(), deliver(), deliver()]);
  const results = await Promise.allSettled([claimAllMail.run(all()), claimMail.run(claim(ids[0]))]);
  assert.equal(results[0].status, "fulfilled", String(results[0].reason));
  if (results[1].status === "rejected") assert.equal(results[1].reason.details.reason, "MailAlreadyClaimed");
  const claimed = results.filter((result) => result.status === "fulfilled")
    .flatMap((result) => result.value.claimedMailIds);
  assert.deepEqual(claimed.sort(), ids.sort());
  assert.equal((await read())[1].balances.Gold, 1300);
});

test("transaction failure rolls back mail, balances, save and receipt together", async (t) => {
  const {deliver, claim, root, read} = await setup();
  const mailId = await deliver();
  const before = await read();
  const mailBefore = (await root.collection("mail").doc(mailId).get()).data();
  const real = saves.mutateSave;
  t.mock.method(saves, "mutateSave", (env, uid, source, receipt, mutate, ...rest) =>
    real(env, uid, source, receipt, async (...args) => { await mutate(...args); throw new Error("injected abort"); }, ...rest));
  await assert.rejects(() => claimMail.run(claim(mailId)), /injected abort/);
  assert.deepEqual(await read(), before);
  assert.deepEqual((await root.collection("mail").doc(mailId).get()).data(), mailBefore);
  assert.equal((await root.collection("wallet").doc("current").collection("receipts").get()).size, 0);
});

test("card and pack rewards share ownership, duplicate payout and receipt replay", async (t) => {
  mockItemSpecs(t);
  const {deliver, claim, read} = await setup();
  const mailId = await deliver(undefined, {rewards: {currencies: [{currency: "Gold", amount: 5}], items: [
    {rewardType: "Card", rewardId: "2", amount: 1}, {rewardType: "Pack", rewardId: "mail-pack", amount: 1},
  ]}});
  const input = claim(mailId);
  const result = await claimMail.run(input);
  assert.deepEqual(result.cards, [{cardId: 2, isNew: true}, {cardId: 2, isNew: false}]);
  assert.equal(result.packs.length, 1); assert.equal(result.packs[0].packId, "mail-pack");
  assert.deepEqual((await read())[0].ownership.cardIds, [1, 2]);
  assert.equal((await read())[1].balances.Gold, 1005); assert.equal((await read())[1].balances.CardDust, 3);
  t.mock.method(specs, "readSpecRows", async () => { throw new Error("spec unavailable"); });
  t.mock.method(specs, "readPinnedSpecRows", async () => { throw new Error("spec unavailable"); });
  assert.deepEqual(await claimMail.run(input), result);
  assert.equal((await read())[1].balances.CardDust, 3);
});
