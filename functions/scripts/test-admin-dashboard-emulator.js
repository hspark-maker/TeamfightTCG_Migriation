"use strict";
// Run only against a disposable local Firestore emulator. Auth is always mocked.
const assert = require("node:assert/strict");
const {test, after} = require("node:test");
const {randomUUID} = require("node:crypto");
if (!/^(127\.0\.0\.1|localhost):\d+$/.test(process.env.FIRESTORE_EMULATOR_HOST ?? "") ||
    !String(process.env.GCLOUD_PROJECT ?? "").startsWith("demo-")) {
  throw new Error("A local Firestore emulator and demo-* project are required.");
}
const {Timestamp} = require("firebase-admin/firestore");
const {getAuth} = require("firebase-admin/auth");
const {db} = require("../lib/firebaseApp");
const {adminDashboardOverview, adminDashboardPlayer} = require("../lib/commands/adminDashboard");
after(() => db.terminate());

const request = (data = {}) => ({auth: {uid: "dashboard-admin", token: {admin: true}}, data: {env: "test", ...data}});
const errorCode = (code) => (error) => error.code === code;
const missingAuth = async () => { throw Object.assign(new Error("Missing user"), {code: "auth/user-not-found"}); };
const mockMissingAuth = (t) => t.mock.method(getAuth(), "getUser", missingAuth);
const uid = () => "dashboard-test-" + randomUUID();
const paths = (env, player) => ["save", "wallet"].map((kind) => db.doc(`envs/${env}/users/${player}/${kind}/current`));
const snapshotState = async (refs) => (await db.getAll(...refs)).map((snap) => ({
  exists: snap.exists, data: snap.data(), updateTime: snap.updateTime?.toMillis(),
}));

test("admin checks and invalid scope reject before any Firestore or Auth call", async (t) => {
  const reads = t.mock.method(db, "getAll", async () => { throw new Error("Unexpected read"); });
  const refs = t.mock.method(db, "doc", () => { throw new Error("Unexpected reference"); });
  const auth = t.mock.method(getAuth(), "getUser", async () => { throw new Error("Unexpected Auth call"); });
  for (const endpoint of [adminDashboardOverview, adminDashboardPlayer]) {
    await assert.rejects(() => endpoint.run({data: {env: "test", uid: "valid"}}), errorCode("unauthenticated"));
    for (const token of [undefined, {}, {admin: false}, {admin: "true"}, {admin: 1}]) {
      await assert.rejects(() => endpoint.run({auth: {uid: "player", token}, data: {env: "test", uid: "valid"}}),
        errorCode("permission-denied"));
    }
    for (const env of [null, "", "Test", "test ", "../live", "test/users", 7]) {
      await assert.rejects(() => endpoint.run(request({env, uid: "valid"})), errorCode("invalid-argument"));
    }
  }
  for (const value of [undefined, null, 3, "", "x".repeat(129), " a", "a ", "a b", "a\nb", "a/b", "a\\b", ".", "..", "a\0b"]) {
    await assert.rejects(() => adminDashboardPlayer.run(request({uid: value})), errorCode("invalid-argument"));
  }
  assert.equal(reads.mock.callCount(), 0);
  assert.equal(refs.mock.callCount(), 0);
  assert.equal(auth.mock.callCount(), 0);
});

test("player returns safe projections and does not mutate stored documents", async (t) => {
  const player = uid();
  const refs = paths("test", player);
  const date = Timestamp.fromDate(new Date("2026-09-21T15:05:00.000Z"));
  const save = {schemaVersion: 8, revision: 42, updatedAt: date, deviceId: "device", appVersion: "1.2.3",
    ownership: {cardIds: [1, 2]}, deck: {slots: [{name: "deck", cardIds: [1, 2]}]}, cardGrowth: {entries: {"1": {level: 3}}},
    rank: {points: 50}, albumReward: {claimedKeys: []}, adventure: {clearedNodeIds: ["1"]},
    tutorial: {outgameCompleted: true}, profile: {nickname: "dashboard", nestedDate: date},
    receipts: {secret: "never"}, customClaims: {admin: true}, internalToken: "never"};
  const wallet = {schemaVersion: 1, rev: 9, balances: {Gold: 200}, paidBalances: {Gold: 100}, updatedAt: date,
    receiptToken: "never"};
  await Promise.all([refs[0].set(save), refs[1].set(wallet)]);
  t.mock.method(getAuth(), "getUser", async (seenUid) => {
    assert.equal(seenUid, player);
    return {uid: player, email: "operator@example.test", displayName: "Player", disabled: false,
      metadata: {creationTime: "Mon, 21 Sep 2026 15:05:00 GMT", lastSignInTime: "Mon, 21 Sep 2026 16:05:00 GMT"},
      providerData: [{providerId: "google.com", accessToken: "never"}], customClaims: {admin: true},
      tokensValidAfterTime: "never", passwordHash: "never"};
  });
  const before = await snapshotState(refs);
  const result = await adminDashboardPlayer.run(request({uid: player}));
  assert.equal(result.env, "test"); assert.equal(result.uid, player);
  assert.equal(typeof result.fetchedAtMs, "number");
  assert.deepEqual(result.account, {uid: player, email: "operator@example.test", displayName: "Player", disabled: false,
    createdAt: "2026-09-21T15:05:00.000Z", lastSignInAt: "2026-09-21T16:05:00.000Z", providers: ["google.com"]});
  const {receipts, customClaims, internalToken, ...safeSave} = save;
  assert.deepEqual(result.save, {...safeSave, updatedAt: date.toDate().toISOString(),
    profile: {...save.profile, nestedDate: date.toDate().toISOString()}});
  assert.deepEqual(result.wallet, {schemaVersion: 1, rev: 9, balances: {Gold: 200}, updatedAt: date.toDate().toISOString()});
  assert(!JSON.stringify(result).includes("never"));
  assert.deepEqual(await snapshotState(refs), before);
});

test("player test/live isolation and missing data do not create or repair accounts", async (t) => {
  mockMissingAuth(t);
  const player = uid();
  const testRefs = paths("test", player); const liveRefs = paths("live", player);
  await Promise.all([testRefs[0].set({revision: 1, profile: {nickname: "test"}}),
    liveRefs[0].set({revision: 2, profile: {nickname: "live"}}), liveRefs[1].set({rev: 2, balances: {Gold: 900}})]);
  const allRefs = [...testRefs, ...liveRefs];
  const before = await snapshotState(allRefs);
  const testPlayer = await adminDashboardPlayer.run(request({uid: player}));
  const livePlayer = await adminDashboardPlayer.run(request({uid: player, env: "live"}));
  assert.equal(testPlayer.account, null); assert.equal(testPlayer.wallet, null);
  assert.equal(testPlayer.save.profile.nickname, "test");
  assert.equal(livePlayer.save.profile.nickname, "live"); assert.equal(livePlayer.wallet.balances.Gold, 900);
  assert.deepEqual(await snapshotState(allRefs), before);
  const absent = uid();
  await assert.rejects(() => adminDashboardPlayer.run(request({uid: absent})), errorCode("not-found"));
  assert((await db.getAll(...paths("test", absent))).every((snap) => !snap.exists));
  const walletOnly = uid();
  await paths("test", walletOnly)[1].set({rev: 1, balances: {Gold: 7}});
  const partial = await adminDashboardPlayer.run(request({uid: walletOnly}));
  assert.equal(partial.save, null); assert.equal(partial.wallet.balances.Gold, 7);
});

test("Auth-only users stay visible and dependency failures propagate", async (t) => {
  const player = uid();
  const lookup = t.mock.method(getAuth(), "getUser", async () => ({uid: player, disabled: true, metadata: {}, providerData: []}));
  const result = await adminDashboardPlayer.run(request({uid: player}));
  assert.equal(result.account.disabled, true); assert.equal(result.account.email, null);
  assert.equal(result.save, null); assert.equal(result.wallet, null);
  const failedAuth = Object.assign(new Error("Auth unavailable"), {code: "auth/internal-error"});
  lookup.mock.mockImplementation(async () => { throw failedAuth; });
  await assert.rejects(() => adminDashboardPlayer.run(request({uid: player})), (error) => error === failedAuth);
  lookup.mock.mockImplementation(missingAuth);
  t.mock.method(db, "getAll", async () => { throw new Error("Firestore unavailable"); });
  await assert.rejects(() => adminDashboardPlayer.run(request({uid: player})), /Firestore unavailable/);
  await assert.rejects(() => adminDashboardOverview.run(request()), /Firestore unavailable/);
});

test("overview uses seven UTC dates, real counters, environment isolation and explicit absence", async (t) => {
  const now = Date.parse("2036-01-01T00:00:00.000Z");
  t.mock.method(Date, "now", () => now);
  const ids = ["2036-01-01", "2035-12-31", "2035-12-30", "2035-12-29", "2035-12-28", "2035-12-27", "2035-12-26"];
  const root = "envs/test";
  const overviewPaths = [`${root}/specs/_index`, `${root}/config/app`, `${root}/config/battleReplay`,
    ...ids.map((day) => `${root}/telemetry/replayDaily/days/${day}`)];
  const refs = overviewPaths.map((path) => db.doc(path));
  await Promise.all(refs.map((ref) => ref.delete()));
  await Promise.all([
    refs[0].set({major: 6, minor: 12, contentVersion: "6.12", tables: {Card: {revision: 5}}, privateKey: "never"}),
    refs[1].set({minSupported: "1.0.0", latest: "1.3.0", noticeBody: "Notice", adminToken: "never"}),
    refs[2].set({enabled: true}),
    refs[3].set({settled: 10, replayOk: 7, replayFailed: 2, unavailable: 1, divergent: 2, outcomeMismatch: 1, hashMismatch: 2}),
    refs[4].set({settled: 0}),
  ]);
  const before = await snapshotState(refs);
  const result = await adminDashboardOverview.run(request());
  assert.equal(result.fetchedAtMs, now); assert.equal(result.env, "test");
  assert.deepEqual(result.content, {major: 6, minor: 12, contentVersion: "6.12", tables: {Card: {revision: 5}}});
  assert.deepEqual(result.appPolicy, {minSupported: "1.0.0", latest: "1.3.0", noticeBody: "Notice"});
  assert.equal(result.replay.enabled, true);
  assert.deepEqual(result.replay.days.map((day) => day.day), ids);
  assert.deepEqual(result.replay.days[0], {day: ids[0], exists: true, settled: 10, replayOk: 7, replayFailed: 2,
    unavailable: 1, divergent: 2, outcomeMismatch: 1, hashMismatch: 2});
  assert.equal(result.replay.days[1].exists, true); assert.equal(result.replay.days[1].settled, 0);
  assert.equal(result.replay.days[2].exists, false); assert.equal(result.replay.days[2].settled, 0);
  assert.deepEqual(await snapshotState(refs), before);
  const liveRefs = overviewPaths.map((path) => db.doc(path.replace("envs/test/", "envs/live/")));
  await Promise.all(liveRefs.map((ref) => ref.delete()));
  const live = await adminDashboardOverview.run(request({env: "live"}));
  assert.equal(live.content, null); assert.equal(live.appPolicy, null); assert.equal(live.replay.enabled, null);
  assert(live.replay.days.every((day) => !day.exists));
  assert((await db.getAll(...liveRefs)).every((snap) => !snap.exists));
});
