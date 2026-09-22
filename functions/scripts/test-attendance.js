"use strict";
// Actual callable + mutateSave + wallet/receipt code with an in-memory conflict/retry driver.
// No credentials, live data, network or spec importer.
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const clone = (value) => value === undefined ? undefined : structuredClone(value);
const documents = new Map(), versions = new Map();
let commits = 0, retries = 0, beforeCommit = null;
let clock = Date.parse("2026-09-14T20:00:00Z");
Date.now = () => clock;
function reference(key) {
  return {path: key, id: key.split("/").at(-1),
    get parent() {return reference(key.split("/").slice(0, -1).join("/"));},
    collection: (name) => ({doc: (id) => reference(`${key}/${name}/${id}`)}),
    get: async () => snapshot(key)};
}
function snapshot(key) {
  const value = clone(documents.get(key));
  return {ref: reference(key), exists: value !== undefined, data: () => clone(value)};
}
const db = {
  doc: reference,
  collection: (name) => ({doc: (id) => reference(`${name}/${id}`)}),
  async runTransaction(callback) {
    for (let attempt = 0; attempt < 30; attempt++) {
      const readVersions = new Map(), writes = [];
      const transaction = {
        async get(ref) {
          assert.equal(writes.length, 0, "Firestore reads must precede every write");
          readVersions.set(ref.path, versions.get(ref.path) || 0);
          return snapshot(ref.path);
        },
        async getAll(...refs) { return Promise.all(refs.map((ref) => this.get(ref))); },
        set(ref, value, options) { writes.push({ref, value, merge: options?.merge}); return this; },
        update(ref, value) { writes.push({ref, value, merge: true}); return this; },
        create(ref, value) { writes.push({ref, value}); return this; },
      };
      const result = await callback(transaction);
      if (beforeCommit) { const hook = beforeCommit; beforeCommit = null; await hook(); }
      if ([...readVersions].some(([key, version]) => version !== (versions.get(key) || 0))) {
        retries++;
        continue;
      }
      for (const {ref, value, merge} of writes) {
        documents.set(ref.path, clone(merge ? {...documents.get(ref.path), ...value} : value));
        versions.set(ref.path, (versions.get(ref.path) || 0) + 1);
      }
      if (writes.length) commits++;
      return result;
    }
    throw new Error("Too much contention in test transaction driver");
  },
};
function stub(modulePath, exports) {
  const filename = require.resolve(modulePath);
  require.cache[filename] = {id: filename, filename, loaded: true, exports};
}
const lines = fs.readFileSync(path.join(__dirname, "../../docs/SpecData/Reward_sheet.csv"), "utf8").split(/\r?\n/);
const rows = lines.filter((line) => line.includes(",Attendance,")).map((line) => {
  const [id, ownerType, ownerId, order, rewardType, rewardId, amount] = line.split(",");
  return {id: Number(id), ownerType, ownerId, order: Number(order), rewardType, rewardId, amount: Number(amount)};
});
let authoredRows = rows, failItemGrant = false;
stub("../lib/firebaseApp", {db});
stub("../lib/packs/packSpecReader", {readSpecRows: async () => authoredRows});
stub("../lib/missions/missionSpec", {readMissionCatalog: async () => []});
stub("../lib/rewards/itemGrant", {
  loadItemGrantContext: async () => ({cards: []}),
  grantRewardItems: (current, items) => {
    if (failItemGrant) throw new Error("pack grant failed");
    if (items[0]?.rewardType === "Avatar") {
      assert.deepEqual(items, [{rewardType: "Avatar", rewardId: "reward-avatar", amount: 1}]);
      return {slots: {profile: {...current.profile, ownedAvatarIds: ["reward-avatar"]}}, cards: [],
        packs: [], currencies: [], cosmetics: [{itemType: "Avatar", itemId: "reward-avatar", isNew: true}]};
    }
    assert.deepEqual(items, [{rewardType: "Pack", rewardId: "NormalPack_TEST", amount: 1}]);
    const cards = [{cardId: 1, isNew: true, snack: 0}];
    return {slots: {ownership: {cardIds: [1]}}, cards,
      packs: [{packId: "NormalPack_TEST", cards}], currencies: []};
  },
});
const {claimAttendance} = require("../lib/commands/claimAttendance");
const {getAttendance} = require("../lib/commands/getAttendance");
const {readAttendance, attendanceResponse, judgeAttendanceClaim} = require("../lib/attendance/attendanceState");
const {attendanceDays} = require("../lib/attendance/attendanceSpec");
const {parseRewardRows} = require("../lib/rewardTable");
const root = "envs/test/users/attendance-test";
const savePath = root + "/save/current", attendancePath = root + "/attendance/current", walletPath = root + "/wallet/current";
const auth = {uid: "attendance-test"};
const get = () => getAttendance.run({auth, data: {env: "test"}});
const claim = (view, txId, extra = {}) => claimAttendance.run({auth, data: {
  env: "test", txId, dailyKey: view.dailyKey, cycle: view.cycle, day: view.claimDay, ...extra,
}});
const reset = (state) => {
  documents.clear(); versions.clear(); commits = 0; retries = 0;
  documents.set(savePath, {schemaVersion: 8, revision: 0});
  documents.set(walletPath, {rev: 1, balances: {Gold: 0}});
  if (state) documents.set(attendancePath, state);
  clock = Date.parse("2026-09-14T20:00:00Z");
};
const test = async (name, run) => { reset(); await run(); console.log("PASS " + name); };
(async () => {
  await test("complete seven-day authored rewards and existing Live pack", async () => {
    const days = attendanceDays(parseRewardRows(rows));
    assert.equal(days.length, 7);
    assert.ok(days.every((day) => day.reward.currencies.length + day.reward.items.length <= 3));
    const pack = fs.readFileSync(path.join(__dirname, "../../docs/SpecData/CardPack_sheet.csv"), "utf8");
    assert.match(pack, /,NormalPack_TEST,[^,]+,Live,/);
    assert.throws(() => attendanceDays(parseRewardRows(rows.slice(0, 6))));
    assert.throws(() => attendanceDays(parseRewardRows([...rows, {...rows[0], id: 999}])));
  });
  await test("query is read-only with KST 05 reset", async () => {
    clock--;
    const before = (await get()).attendance;
    assert.equal(before.dailyKey, "2026-09-14");
    clock++;
    const after = (await get()).attendance;
    assert.equal(after.dailyKey, "2026-09-15");
    assert.equal(before.nextResetAtMs, clock);
    assert.equal(after.nextResetAtMs, clock + 86400000);
    assert.equal(after.claimDay, 1);
    assert.equal(after.cycle, 1);
    assert.equal(commits, 0);
  });
  await test("same txId concurrent requests replay exact response and credit once", async () => {
    const view = (await get()).attendance;
    const results = await Promise.all([claim(view, "attendance-same"), claim(view, "attendance-same")]);
    assert.deepEqual(results[0], results[1]);
    assert.equal(documents.get(walletPath).balances.Gold, 110);
    assert.equal(commits, 1);
    assert.ok(retries > 0);
  });
  await test("different txIds concurrent requests grant once", async () => {
    const view = (await get()).attendance;
    const result = await Promise.allSettled([claim(view, "attendance-one"), claim(view, "attendance-two")]);
    assert.equal(result.filter((entry) => entry.status === "fulfilled").length, 1);
    assert.match(result.find((entry) => entry.status === "rejected").reason.message, /^AlreadyClaimed:/);
    assert.equal(documents.get(savePath).revision, 1);
    assert.equal(documents.get(walletPath).balances.Gold, 110);
  });
  await test("missed days preserve progress and client clock is ignored", async () => {
    await claim((await get()).attendance, "attendance-first");
    clock += 20 * 86400000;
    const view = (await get()).attendance;
    assert.equal(view.claimDay, 2);
    assert.equal(view.cycle, 1);
    const result = await claim(view, "attendance-later", {serverNowMs: clock + 1e12});
    assert.equal(result.attendance.serverNowMs, clock);
    assert.equal(result.attendance.claimedDays, 2);
  });
  await test("stale date cycle and day never consume another reward", async () => {
    const view = (await get()).attendance;
    for (const extra of [{dailyKey: "2099-01-01"}, {cycle: 2}, {day: 7}]) {
      await assert.rejects(claim(view, "attendance-invalid", extra), /AttendanceStale:/);
    }
    clock += 86400000;
    await assert.rejects(claim(view, "attendance-old-date"), /AttendanceStale:/);
    assert.equal(commits, 0);
  });
  await test("response lost across reset replays prior receipt without next-day grant", async () => {
    const view = (await get()).attendance;
    const first = await claim(view, "attendance-lost");
    clock += 86400000;
    assert.deepEqual(await claim(view, "attendance-lost"), first);
    assert.equal((await get()).attendance.claimDay, 2);
    assert.equal(commits, 1);
  });
  await test("seventh day completes then next day begins new cycle", async () => {
    for (let day = 1; day <= 7; day++) {
      const result = await claim((await get()).attendance, "attendance-day-" + day);
      assert.equal(result.attendance.claimedDays, day);
      assert.equal(result.attendance.canClaim, false);
      if (day === 7) {
        assert.equal(result.packs[0].packId, "NormalPack_TEST");
        assert.deepEqual(result.updatedSlots.ownership, {cardIds: [1]});
        assert.equal((await get()).attendance.claimedDays, 7);
      }
      clock += 86400000;
    }
    const next = (await get()).attendance;
    assert.equal(next.cycle, 2);
    assert.equal(next.claimedDays, 0);
    assert.equal(next.claimDay, 1);
    assert.equal(next.canClaim, true);
    assert.equal((await claim(next, "attendance-cycle2")).attendance.cycle, 2);
  });
  await test("pre-reset retry cannot overwrite newer committed date", async () => {
    const view = (await get()).attendance;
    beforeCommit = async () => {
      clock += 86400000;
      await claim((await get()).attendance, "attendance-new-day");
    };
    await assert.rejects(claim(view, "attendance-before-reset"), /AlreadyClaimed:/);
    assert.equal(documents.get(attendancePath).lastClaimDailyKey, "2026-09-16");
    assert.equal(documents.get(walletPath).balances.Gold, 110);
  });
  await test("missing reward or item grant failure never consumes attendance", async () => {
    const view = (await get()).attendance;
    authoredRows = rows.slice(0, 6);
    await assert.rejects(claim(view, "attendance-missing"), /missing or invalid/);
    authoredRows = rows;
    reset({cycle: 1, claimedDays: 6, lastClaimDailyKey: "2026-09-14"});
    failItemGrant = true;
    await assert.rejects(claim((await get()).attendance, "attendance-pack-fail"), /pack grant failed/);
    failItemGrant = false;
    assert.equal(documents.get(attendancePath).claimedDays, 6);
    assert.equal(commits, 0);
  });
  await test("cosmetic attendance response and receipt replay retain ownership and grant details", async () => {
    authoredRows = rows.map(row => row.ownerId === "day_1" ?
      {...row, rewardType: "Avatar", rewardId: "reward-avatar", amount: 1} : row);
    const view = (await get()).attendance;
    const result = await claim(view, "attendance-cosmetic");
    assert.deepEqual(result.cosmetics, [{itemType: "Avatar", itemId: "reward-avatar", isNew: true}]);
    assert.deepEqual(result.updatedSlots.profile.ownedAvatarIds, ["reward-avatar"]);
    assert.deepEqual(documents.get(savePath).profile.ownedAvatarIds, ["reward-avatar"]);
    assert.deepEqual(await claim(view, "attendance-cosmetic"), result);
    assert.equal(commits, 1);
    assert.equal(documents.get(walletPath).balances.Gold, 0);
    authoredRows = rows;
  });
  await test("invalid persisted state fails closed", async () => {
    assert.throws(() => readAttendance({cycle: 0, claimedDays: 0, lastClaimDailyKey: ""}));
    assert.throws(() => readAttendance({cycle: 1, claimedDays: 8, lastClaimDailyKey: "2026-09-14"}));
    assert.throws(() => readAttendance({cycle: 1, claimedDays: 3, lastClaimDailyKey: ""}));
    const future = {cycle: 1, claimedDays: 7, lastClaimDailyKey: "2026-09-16"};
    assert.equal(attendanceResponse(future, clock).canClaim, false);
    assert.equal(judgeAttendanceClaim(future, {dailyKey: "2026-09-15", cycle: 1, day: 7}, clock).allow, false);
  });
  console.log("Attendance: 12 focused regression scenarios passed.");
})().catch((error) => { console.error(error); process.exitCode = 1; });
