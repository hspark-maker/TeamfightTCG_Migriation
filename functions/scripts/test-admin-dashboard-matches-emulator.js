"use strict";
// This suite creates fixtures only in a disposable demo-* Firestore emulator; callable Auth is mocked.
const assert = require("node:assert/strict");
const {test, after} = require("node:test");
const {randomUUID} = require("node:crypto");
if (!/^(127\.0\.0\.1|localhost):\d+$/.test(process.env.FIRESTORE_EMULATOR_HOST ?? "") ||
    !String(process.env.GCLOUD_PROJECT ?? "").startsWith("demo-")) {
  throw new Error("A local Firestore emulator and demo-* project are required.");
}
const {Timestamp, Query} = require("firebase-admin/firestore");
const {db} = require("../lib/firebaseApp");
const {adminDashboardMatches} = require("../lib/commands/adminDashboardMatches");
after(() => db.terminate());
const request = (data = {}) => ({auth: {uid: "dashboard-admin", token: {admin: true}}, data: {env: "test", ...data}});
const errorCode = (code) => (error) => error.code === code;
const timestamp = (ms) => Timestamp.fromMillis(ms);
const simulation = (overrides = {}) => ({ok: true, draw: false, winnerOwner: 0, firstOwner: 0, stats: {turns: 10}, ...overrides});
const match = (ms, overrides = {}) => ({status: "confirmed", mode: "solo", settledAt: timestamp(ms),
  serverSimulation: simulation(), ...overrides});
const matchRef = (env = "test") => db.doc(`envs/${env}/matches/dashboard-${randomUUID()}`);
const states = async (refs) => (await db.getAll(...refs)).map((snap) => ({
  exists: snap.exists, data: snap.data(), updateTime: snap.updateTime?.toMillis(),
}));

test("auth, strict admin claim, env and day range reject before any database read", async (t) => {
  const reads = t.mock.method(db, "getAll", async () => { throw new Error("Unexpected read"); });
  const refs = t.mock.method(db, "doc", () => { throw new Error("Unexpected reference"); });
  const queries = t.mock.method(db, "collection", () => { throw new Error("Unexpected query"); });
  await assert.rejects(() => adminDashboardMatches.run({data: {env: "test"}}), errorCode("unauthenticated"));
  await assert.rejects(() => adminDashboardMatches.run({auth: {uid: "", token: {admin: true}}, data: {env: "test"}}),
    errorCode("unauthenticated"));
  for (const token of [undefined, {}, {admin: false}, {admin: "true"}, {admin: 1}]) {
    await assert.rejects(() => adminDashboardMatches.run({auth: {uid: "player", token}, data: {env: "test"}}),
      errorCode("permission-denied"));
  }
  for (const env of [null, "", "Test", "test ", "../live", "test/users", 7]) {
    await assert.rejects(() => adminDashboardMatches.run(request({env})), errorCode("invalid-argument"));
  }
  for (const days of [null, 0, 91, 1000, "7", true, -1, 1.5, [], {}]) {
    await assert.rejects(() => adminDashboardMatches.run(request({days})), errorCode("invalid-argument"));
  }
  for (const range of [
    {startDate: "2024-01-01"}, {endDate: "2024-01-01"},
    {startDate: null, endDate: "2024-01-01"}, {startDate: "2024-01-01", endDate: false},
    {startDate: "2024-02-30", endDate: "2024-03-01"},
    {startDate: "2023-02-29", endDate: "2023-03-01"},
    {startDate: "2024-1-01", endDate: "2024-01-02"},
    {startDate: "2024-01-01T00:00:00Z", endDate: "2024-01-02"},
    {startDate: "2024-01-02", endDate: "2024-01-01"},
    {startDate: "2024-01-01", endDate: "2024-03-31"}, // 91 inclusive days in leap year.
    {startDate: "9999-01-01", endDate: "9999-01-01"},
    {days: 1, startDate: "2024-01-01", endDate: "2024-01-01"},
  ]) {
    await assert.rejects(() => adminDashboardMatches.run(request(range)), errorCode("invalid-argument"));
  }
  assert.equal(reads.mock.callCount(), 0); assert.equal(refs.mock.callCount(), 0); assert.equal(queries.mock.callCount(), 0);
});

test("outcomes require confirmed valid server results; AI owner zero differs from first owner", async (t) => {
  const now = Date.parse("2037-01-15T12:00:00Z");
  t.mock.method(Date, "now", () => now);
  const values = [
    match(now, {serverSimulation: simulation({firstOwner: 1})}), // Human win even when AI moved first.
    match(now - 1, {serverSimulation: simulation({winnerOwner: 1, stats: {turns: 20}})}),
    match(now - 2, {adventureNodeId: "chapter-1", serverSimulation: simulation({draw: true, winnerOwner: -1, stats: {turns: 0}})}),
    match(now - 3, {serverSimulation: simulation({draw: undefined, winnerOwner: 0, stats: {turns: -1}})}),
    match(now - 4, {serverSimulation: simulation({draw: false, winnerOwner: -1, stats: {turns: 1.5}})}),
    match(now - 5, {status: "flagged", reason: "outcome_mismatch", serverSimulation: simulation({stats: {turns: 999}})}),
    match(now - 6, {adventureNodeId: "chapter-2", serverSimulation: {ok: false}}),
    match(now - 7, {mode: "pvp", serverSimulation: simulation({stats: {turns: 30}})}),
    match(now - 8, {mode: "pvp", serverSimulation: {ok: true}}),
    match(now - 9, {status: "new-status", mode: "new-mode", serverSimulation: null}),
    match(now - 10, {serverSimulation: simulation({stats: {turns: Number.MAX_SAFE_INTEGER + 1}})}),
  ];
  // Firestore does not accept undefined values; a missing draw is deliberately omitted.
  delete values[3].serverSimulation.draw;
  const refs = values.map(() => matchRef());
  await Promise.all(refs.map((ref, index) => ref.set(values[index])));
  const before = await states(refs);
  const result = await adminDashboardMatches.run(request({days: 1}));
  assert.equal(result.sampleSize, 11); assert.equal(result.hasMore, false);
  assert.deepEqual(result.summary, {confirmed: 9, flagged: 1, other: 1, replayed: 9,
    averageTurns: 15, turnSamples: 4, aiWins: 2, aiLosses: 1, aiDraws: 1, aiUnknown: 4});
  assert.deepEqual(result.modes, [
    {mode: "ai", total: 6, confirmed: 5, flagged: 1, wins: 2, losses: 1, draws: 0, unknown: 3},
    {mode: "adventure", total: 2, confirmed: 2, flagged: 0, wins: 0, losses: 0, draws: 1, unknown: 1},
    {mode: "pvp", total: 2, confirmed: 2, flagged: 0, wins: 0, losses: 0, draws: 0, unknown: 1},
    {mode: "unknown", total: 1, confirmed: 0, flagged: 0, wins: 0, losses: 0, draws: 0, unknown: 1},
  ]);
  assert.deepEqual(result.recent.map((row) => row.outcome),
    ["win", "loss", "draw", "unknown", "unknown", "unknown", "unknown", "pvp", "unknown", "unknown", "win"]);
  assert.deepEqual(result.recent.map((row) => row.turns), [10, 20, 0, null, null, null, null, 30, null, null, null]);
  assert.deepEqual(await states(refs), before);
});

test("UTC inclusive boundaries, default seven days, environment isolation and missing daily counters", async (t) => {
  const now = Date.parse("2037-03-01T00:00:00Z");
  const from = Date.parse("2037-02-23T00:00:00Z");
  t.mock.method(Date, "now", () => now);
  const offsets = [from - 1, from, now - 1, now, now + 1];
  const refs = offsets.map(() => matchRef());
  await Promise.all(refs.map((ref, index) => ref.set(match(offsets[index]))));
  const liveRef = matchRef("live");
  await liveRef.set(match(now, {status: "flagged", serverSimulation: null}));
  const ids = ["2037-02-23", "2037-02-24", "2037-02-25", "2037-02-26", "2037-02-27", "2037-02-28", "2037-03-01"];
  const dailyRefs = ids.map((day) => db.doc(`envs/test/telemetry/replayDaily/days/${day}`));
  await Promise.all(dailyRefs.map((ref) => ref.delete()));
  await Promise.all([dailyRefs[0].set({settled: 2000}), dailyRefs[1].set({settled: 0}),
    dailyRefs[2].set({settled: -1}), dailyRefs[3].set({settled: "10"}), dailyRefs[4].set({settled: 1.5})]);
  const before = await states([...refs, liveRef, ...dailyRefs]);
  const result = await adminDashboardMatches.run(request());
  assert.equal(result.days, 7); assert.equal(result.fromMs, from); assert.equal(result.toMs, now);
  assert.equal(result.startDate, ids[0]); assert.equal(result.endDate, ids[6]);
  assert.equal(result.detailFromMs, from); assert.equal(result.detailToMs, now);
  assert.equal(result.detailAvailable, true); assert.equal(result.detailLimited, false);
  assert.equal(result.fetchedAtMs, now); assert.equal(result.sampleSize, 3);
  assert.deepEqual(result.recent.map((row) => row.settledAtMs), [now, now - 1, from]);
  assert.deepEqual(result.daily, ids.map((day, index) => ({day, exists: index < 5, settled: index === 0 ? 2000 : index === 1 ? 0 : null})));
  const today = await adminDashboardMatches.run(request({days: 1}));
  assert.equal(today.sampleSize, 1); assert.equal(today.fromMs, now); assert.equal(today.daily.length, 1);
  const live = await adminDashboardMatches.run(request({days: 1, env: "live"}));
  assert.equal(live.sampleSize, 1); assert.equal(live.summary.flagged, 1); assert.equal(live.summary.confirmed, 0);
  assert.deepEqual(live.daily, [{day: ids[6], exists: false, settled: null}]);
  assert.deepEqual(await states([...refs, liveRef, ...dailyRefs]), before);
});

test("all 1 to 90 day presets retain complete daily counters and cap details to rolling seven days", async (t) => {
  const dayMs = 86400000;
  const now = Date.parse("2040-04-15T12:00:00Z");
  const today = Date.parse("2040-04-15T00:00:00Z");
  t.mock.method(Date, "now", () => now);
  t.mock.method(Query.prototype, "get", async () => ({docs: [], size: 0}));
  let dailyCount = 0;
  t.mock.method(db, "getAll", async (...args) => {
    assert.deepEqual(args.pop(), {fieldMask: ["settled"]});
    dailyCount = args.length;
    return args.map(() => ({exists: true, data: () => ({settled: 1})}));
  });
  for (let days = 1; days <= 90; days++) {
    const result = await adminDashboardMatches.run(request({days}));
    assert.equal(result.days, days); assert.equal(result.daily.length, days); assert.equal(dailyCount, days);
    assert.equal(result.fromMs, today - (days - 1) * dayMs); assert.equal(result.toMs, now);
    assert.equal(result.detailFromMs, Math.max(result.fromMs, now - 7 * dayMs));
    assert.equal(result.detailToMs, now); assert.equal(result.detailAvailable, true);
    assert.equal(result.detailLimited, days >= 8);
    assert.equal(result.daily.reduce((sum, row) => sum + row.settled, 0), days);
  }
});

test("custom yesterday is inclusive through its final millisecond; today is clamped to now", async (t) => {
  const now = Date.parse("2040-06-02T12:30:00Z");
  const from = Date.parse("2040-06-01T00:00:00Z");
  const end = Date.parse("2040-06-01T23:59:59.999Z");
  t.mock.method(Date, "now", () => now);
  const times = [from - 1, from, end, end + 1, now, now + 1];
  await Promise.all(times.map((time) => matchRef().set(match(time))));
  const yesterday = await adminDashboardMatches.run(request({startDate: "2040-06-01", endDate: "2040-06-01"}));
  assert.equal(yesterday.days, 1); assert.equal(yesterday.fromMs, from); assert.equal(yesterday.toMs, end);
  assert.deepEqual(yesterday.recent.map((row) => row.settledAtMs), [end, from]);
  const throughToday = await adminDashboardMatches.run(request({startDate: "2040-06-01", endDate: "2040-06-02"}));
  assert.equal(throughToday.days, 2); assert.equal(throughToday.toMs, now);
  assert.deepEqual(throughToday.recent.map((row) => row.settledAtMs), [now, end + 1, end, from]);
  await assert.rejects(() => adminDashboardMatches.run(request({startDate: "2040-06-02", endDate: "2040-06-03"})),
    errorCode("invalid-argument"));
});

test("old custom leap-year range loads all 90 days and never queries retained matches", async (t) => {
  const now = Date.parse("2040-07-15T12:00:00Z");
  t.mock.method(Date, "now", () => now);
  const query = t.mock.method(db, "collection", () => { throw new Error("Old range must not query matches"); });
  await db.doc("envs/test/telemetry/replayDaily/days/2040-02-29").set({settled: 1234});
  const result = await adminDashboardMatches.run(request({startDate: "2040-01-01", endDate: "2040-03-30"}));
  assert.equal(result.days, 90); assert.equal(result.daily.length, 90);
  assert.equal(result.daily[0].day, "2040-01-01"); assert.equal(result.daily[89].day, "2040-03-30");
  assert.deepEqual(result.daily.find((row) => row.day === "2040-02-29"), {day: "2040-02-29", exists: true, settled: 1234});
  assert.equal(result.detailAvailable, false); assert.equal(result.detailLimited, true);
  assert.equal(result.detailFromMs, null); assert.equal(result.detailToMs, null);
  assert.equal(result.sampleSize, 0); assert.equal(result.hasMore, false); assert.equal(query.mock.callCount(), 0);
});

test("extended selection excludes expired-but-retained documents and clips partially overlapping custom ranges", async (t) => {
  const dayMs = 86400000;
  const now = Date.parse("2040-09-15T12:00:00Z");
  const cutoff = now - 7 * dayMs;
  t.mock.method(Date, "now", () => now);
  const times = [now - 20 * dayMs, cutoff - 1, cutoff, cutoff + 1, now, now + 1];
  await Promise.all(times.map((time) => matchRef().set(match(time))));
  const month = await adminDashboardMatches.run(request({days: 30}));
  assert.equal(month.daily.length, 30); assert.equal(month.detailLimited, true);
  assert.deepEqual(month.recent.map((row) => row.settledAtMs), [now, cutoff + 1, cutoff]);
  const partial = await adminDashboardMatches.run(request({startDate: "2040-09-01", endDate: "2040-09-08"}));
  assert.equal(partial.detailAvailable, true); assert.equal(partial.detailFromMs, cutoff);
  assert.equal(partial.detailToMs, Date.parse("2040-09-08T23:59:59.999Z"));
  assert.deepEqual(partial.recent.map((row) => row.settledAtMs), [cutoff + 1, cutoff]);
});

test("query reads at most 501 projected matches; analysis is capped at 500 and recent at 50", async (t) => {
  const now = Date.parse("2037-05-01T12:00:00Z");
  t.mock.method(Date, "now", () => now);
  const refs = Array.from({length: 503}, () => matchRef());
  for (let start = 0; start < refs.length; start += 400) {
    const batch = db.batch();
    refs.slice(start, start + 400).forEach((ref, offset) => batch.set(ref, match(now - start - offset, {
      participantUids: ["never-return-user"], submissions: {token: "never-return-token"},
      decks: ["never-return-deck"], extra: "never-return-private",
      serverSimulation: simulation({privateToken: "never-return-simulation", stats: {turns: 10, private: "never-return-stats"}}),
    })));
    await batch.commit();
  }
  const originalGet = Query.prototype.get;
  let returnedRows = 0;
  t.mock.method(Query.prototype, "get", async function(...args) {
    const snapshot = await originalGet.apply(this, args);
    returnedRows = snapshot.size;
    for (const doc of snapshot.docs) {
      assert(!JSON.stringify(doc.data()).includes("never-return"), "Projection must remove private fields before aggregation");
      assert.deepEqual(Object.keys(doc.data()).sort(), ["mode", "serverSimulation", "settledAt", "status"]);
    }
    return snapshot;
  });
  const result = await adminDashboardMatches.run(request({days: 1}));
  assert.equal(returnedRows, 501); assert.equal(result.sampleSize, 500); assert.equal(result.limit, 500);
  assert.equal(result.hasMore, true); assert.equal(result.recent.length, 50); assert.equal(result.summary.aiWins, 500);
  assert.equal(result.recent[0].id, refs[0].id); assert.equal(result.recent[49].id, refs[49].id);
  assert(!JSON.stringify(result).includes("never-return"));
  await Promise.all(refs.slice(500).map((ref) => ref.delete()));
  const exactLimit = await adminDashboardMatches.run(request({days: 1}));
  assert.equal(returnedRows, 500); assert.equal(exactLimit.sampleSize, 500); assert.equal(exactLimit.hasMore, false);
});

test("empty retained window has null average and no invented zero daily totals", async (t) => {
  const now = Date.parse("2037-07-01T12:00:00Z");
  t.mock.method(Date, "now", () => now);
  const result = await adminDashboardMatches.run(request({days: 1}));
  assert.equal(result.sampleSize, 0); assert.equal(result.hasMore, false); assert.deepEqual(result.recent, []);
  assert.equal(result.summary.averageTurns, null); assert.equal(result.summary.turnSamples, 0);
  assert.deepEqual(result.daily, [{day: "2037-07-01", exists: false, settled: null}]);
  assert(result.modes.every((mode) => mode.total === 0));
});

test("dependency errors propagate without creating gameplay data", async (t) => {
  const query = t.mock.method(Query.prototype, "get", async () => ({docs: [], size: 0}));
  const daily = t.mock.method(db, "getAll", async () => { throw new Error("Daily telemetry unavailable"); });
  await assert.rejects(() => adminDashboardMatches.run(request()), /Daily telemetry unavailable/);
  daily.mock.mockImplementation(async () => []);
  query.mock.mockImplementation(async () => { throw new Error("Match query unavailable"); });
  await assert.rejects(() => adminDashboardMatches.run(request()), /Match query unavailable/);
});
