// Run the real callable and mission-store logic against an in-memory transaction.
const assert = require("node:assert/strict");
const Module = require("node:module");
const {HttpsError} = require("firebase-functions/v2/https");
const {missionPeriod} = require("../lib/missions/period");
const {readMissions} = require("../lib/missions/missionStore");
const period = missionPeriod(Date.now());
const catalog = [
  {id: "daily.pack", period: "daily", event: "OpenPack", target: 1, enabled: true},
  {id: "weekly.pack", period: "weekly", event: "OpenPack", target: 5, enabled: true},
];
let stored, reads = [], writes = [];
const db = {
  doc: path => ({path}),
  runTransaction: async action => action({
    get: async ref => {
      reads.push(ref.path);
      return {exists: stored !== undefined, data: () => structuredClone(stored)};
    },
    set: (ref, data) => {
      writes.push(ref.path);
      stored = structuredClone(data);
    },
  }),
};
const originalLoad = Module._load;
Module._load = function(request, parent, isMain) {
  if (parent?.filename.endsWith("devResetDailyMissions.js")) {
    if (request === "firebase-functions/v2/https") return {HttpsError, onCall: handler => handler};
    if (request === "firebase-functions/logger") return {info() {}};
    if (request === "firebase-admin/firestore") return {FieldValue: {serverTimestamp: () => "server-time"}};
    if (request === "../firebaseApp") return {db};
    if (request === "../missions/missionSpec") return {readMissionCatalog: async () => catalog};
    if (request === "../save/saveDocument") return {
      requireUid: auth => {
        if (!auth?.uid) throw new HttpsError("unauthenticated", "Sign in required");
        return auth.uid;
      },
    };
  }
  return originalLoad.call(this, request, parent, isMain);
};
let reset;
try {
  reset = require("../lib/commands/devResetDailyMissions").devResetDailyMissions;
} finally {
  Module._load = originalLoad;
}

(async () => {
  for (const env of ["live", "", "other"]) {
    await assert.rejects(reset({auth: {uid: "caller"}, data: {env}}), error => error.code === "permission-denied");
  }
  await assert.rejects(reset({data: {env: "test"}}), error => error.code === "unauthenticated");
  assert.equal(reads.length, 0, "Rejected calls must not reach Firestore");

  stored = {
    dailyKey: period.daily, weeklyKey: period.weekly,
    progress: {"daily.OpenPack": 3, "daily.CompleteDailyMissions": 1, "weekly.OpenPack": 8, "guide.Enhance": 2},
    claimed: {"daily.pack": true, "daily.removed-from-catalog": true, "weekly.pack": true, "guide.step1": true},
    passExp: 70,
  };
  const result = await reset({auth: {uid: "caller"}, data: {env: "test", uid: "other-user"}});
  assert.deepEqual(reads, ["envs/test/users/caller/missions/current"]);
  assert.deepEqual(writes, reads, "Only the caller's mission document may be written");
  assert.deepEqual(stored.progress, {"weekly.OpenPack": 8, "guide.Enhance": 2});
  assert.deepEqual(stored.claimed, {"weekly.pack": true, "guide.step1": true});
  assert.equal(stored.passExp, 70);
  assert.equal(stored.dailyKey, period.daily);
  assert.equal(stored.weeklyKey, period.weekly);
  assert.equal(result.missions.progress["daily.CompleteDailyMissions"], 0);
  assert.equal(result.missions.progress["weekly.CompleteWeeklyMissions"], 1);
  assert.equal(result.missions.claimed["daily.pack"], undefined);
  const first = structuredClone(stored);
  await reset({auth: {uid: "caller"}, data: {env: "test"}});
  assert.deepEqual(stored, first, "Repeated reset without new progress must be stable");

  // Legacy counters must not be resurrected by the next read.
  stored = {dailyPeriod: period.daily, weeklyPeriod: period.weekly,
    daily: {OpenPack: 3}, weekly: {OpenPack: 5}, claimed: ["daily.pack", "weekly.pack"], battlePassXp: 42};
  await reset({auth: {uid: "caller"}, data: {env: "test"}});
  const reread = readMissions({exists: true, data: () => stored});
  assert.deepEqual(reread.progress, {"weekly.OpenPack": 5});
  assert.deepEqual(reread.claimed, {"weekly.pack": true});
  assert.equal(reread.passExp, 42);

  stored = undefined;
  reads = []; writes = [];
  await reset({auth: {uid: "caller"}, data: {env: "test"}});
  assert.deepEqual(stored.progress, {});
  assert.deepEqual(stored.claimed, {});
  assert.equal(stored.passExp, 0);
  assert.equal(writes.length, 1);
  console.log("test-reset-daily-missions: ok (scope, ownership, reset, preserved tracks, legacy, missing document)");
})().catch(error => {console.error(error); process.exitCode = 1;});
