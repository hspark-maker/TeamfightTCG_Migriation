// 실제 outbox helper와 trigger를 실행한다. DB 커밋만 원자적인 메모리 모델로 대체한다.
const assert = require("node:assert/strict");
const Module = require("node:module");
const originalLoad = Module._load;
const originalNow = Date.now;
const documents = new Map();
const attempts = [];
let options;
let now = Date.parse("2026-09-10T23:59:59.000Z");
let failCommit;

const db = {
  doc: path => ({path}),
  runTransaction: async run => {
    const pending = [];
    const reads = [];
    attempts.push({reads, pending});
    const transaction = {
      get: async ref => {
        assert.equal(pending.length, 0, "transaction reads precede writes");
        reads.push(ref.path);
        const value = documents.get(ref.path);
        return {ref, exists: value !== undefined, data: () => value};
      },
      create: (ref, value) => pending.push({kind: "create", ref, value}),
      set: (ref, value, config) => pending.push({kind: "set", ref, value, config}),
      delete: ref => pending.push({kind: "delete", ref}),
    };
    const result = await run(transaction);
    // Build the next state without exposing any writes until all operations succeed.
    const next = new Map(documents);
    for (const operation of pending) {
      const {kind, ref, value, config} = operation;
      if (kind === "delete") {
        next.delete(ref.path);
        continue;
      }
      if (kind === "create") assert.equal(next.has(ref.path), false);
      const previous = next.get(ref.path) || {};
      const merged = config?.merge ? {...previous} : {};
      for (const [key, field] of Object.entries(value)) {
        merged[key] = field?.operation === "increment" ?
          (previous[key] || 0) + field.amount : field;
      }
      next.set(ref.path, merged);
    }
    if (failCommit) {
      const error = failCommit;
      failCommit = undefined;
      throw error;
    }
    documents.clear();
    for (const [path, value] of next) documents.set(path, value);
    return result;
  },
};

Module._load = function(request, parent, isMain) {
  if (request === "firebase-admin/firestore") return {
    FieldValue: {
      increment: amount => ({operation: "increment", amount}),
      serverTimestamp: () => ({operation: "serverTimestamp"}),
    },
    Timestamp: {now: () => {
      const value = now;
      return {toDate: () => new Date(value), toMillis: () => value};
    }},
  };
  if (request === "firebase-functions/v2/firestore") return {
    onDocumentCreated: (config, handler) => {options = config; return handler;},
  };
  if (request === "firebase-functions/logger") return {error() {}, warn() {}};
  if ((parent?.filename.endsWith("battleReplayTelemetry.js") && request === "./firebaseApp") ||
      (parent?.filename.endsWith("aggregateReplayDaily.js") && request === "../firebaseApp")) {
    return {db, DATABASE_ID: "test-database"};
  }
  return originalLoad.call(this, request, parent, isMain);
};
let enqueueReplayDaily, consumeReplayDaily, aggregateReplayDaily;
try {
  ({enqueueReplayDaily, consumeReplayDaily} = require("../lib/battleReplayTelemetry"));
  ({aggregateReplayDaily} = require("../lib/commands/aggregateReplayDaily"));
} finally {
  Module._load = originalLoad;
}

const delta = overrides => ({settled: 0, replayOk: 0, replayFailed: 0, unavailable: 0,
  divergent: 0, outcomeMismatch: 0, hashMismatch: 0, ...overrides});
const eventRef = (env, id) => db.doc(`envs/${env}/replayTelemetryEvents/${id}`);
const dayPath = (env, day) => `envs/${env}/telemetry/replayDaily/days/${day}`;
const delivery = (env, id) => ({params: {env, eventId: id}, data: {ref: eventRef(env, id)}});
const enqueue = (env, id, values) => db.runTransaction(tx => {
  assert.equal(enqueueReplayDaily(tx, env, id, delta(values)), undefined,
    "enqueue stages a write without awaiting another DB operation");
});

async function main() {
  Date.now = () => now;
  assert.deepEqual(options, {
    document: "envs/{env}/replayTelemetryEvents/{eventId}", database: "test-database", retry: true,
  });
  const oldDay = "2026-09-10";
  const newDay = "2026-09-11";
  const original = {day: oldDay, settled: 40, replayOk: 20, divergent: 7, custom: "preserved"};
  documents.set(dayPath("test", oldDay), original);
  documents.set(dayPath("live", oldDay), {...original, settled: 900});

  await enqueue("test", "first", {settled: 1, replayOk: 1});
  const first = documents.get(eventRef("test", "first").path);
  assert.equal(first.day, oldDay);
  assert.deepEqual(first.delta, delta({settled: 1, replayOk: 1}));
  assert.ok(first.createdAt, "event includes creation time");
  assert.deepEqual(documents.get(dayPath("test", oldDay)), original,
    "request transaction must not synchronously update the shared daily counter");
  assert.deepEqual(attempts.at(-1).reads, []);
  assert.equal(attempts.at(-1).pending.length, 1);

  await enqueue("test", "second", {settled: 1, replayFailed: 1, hashMismatch: 1});
  now = Date.parse("2026-09-11T01:00:00Z");
  await aggregateReplayDaily(delivery("test", "second"));
  await aggregateReplayDaily(delivery("test", "first"));
  const settled = documents.get(dayPath("test", oldDay));
  assert.equal(settled.settled, 42);
  assert.equal(settled.replayOk, 21);
  assert.equal(settled.replayFailed, 1);
  assert.equal(settled.hashMismatch, 1);
  assert.equal(settled.divergent, 7, "zero delta leaves existing counters intact");
  assert.equal(settled.custom, "preserved");
  assert.equal(documents.has(dayPath("test", newDay)), false, "late delivery uses original UTC day");
  assert.equal(documents.get(dayPath("live", oldDay)).settled, 900, "environment isolation");
  assert.equal(documents.has(eventRef("test", "first").path), false);
  assert.equal(documents.has(eventRef("test", "second").path), false);
  const successfulConsume = attempts.at(-1);
  assert.deepEqual(successfulConsume.reads, [eventRef("test", "first").path]);
  assert.equal(successfulConsume.pending.length, 2, "increment and delete share one commit");
  assert.equal(successfulConsume.pending.filter(op => op.kind === "delete").length, 1);
  const incrementWrite = successfulConsume.pending.find(op => op.kind === "set");
  assert.deepEqual(incrementWrite.config, {merge: true});
  assert.equal(Object.hasOwn(incrementWrite.value, "divergent"), false, "skip zero increments");

  await aggregateReplayDaily(delivery("test", "second"));
  await consumeReplayDaily("test", eventRef("test", "first"));
  assert.equal(attempts.at(-1).pending.length, 0, "duplicate event is read-only");
  assert.deepEqual(documents.get(dayPath("test", oldDay)), settled);

  await enqueue("test", "retry", {settled: 1, unavailable: 1});
  const pendingEvent = documents.get(eventRef("test", "retry").path);
  const failure = new Error("atomic commit rejected");
  failCommit = failure;
  await assert.rejects(aggregateReplayDaily(delivery("test", "retry")), error => error === failure);
  assert.deepEqual(documents.get(eventRef("test", "retry").path), pendingEvent,
    "failed consumer commit retains event for retry");
  assert.equal(documents.has(dayPath("test", newDay)), false,
    "failed consumer commit cannot expose a partial increment");
  await aggregateReplayDaily(delivery("test", "retry"));
  await aggregateReplayDaily(delivery("test", "retry"));
  assert.equal(documents.get(dayPath("test", newDay)).settled, 1);
  assert.equal(documents.get(dayPath("test", newDay)).unavailable, 1);
  assert.equal(documents.has(eventRef("test", "retry").path), false);

  failCommit = failure;
  await assert.rejects(enqueue("test", "aborted-settlement", {settled: 1}), error => error === failure);
  assert.equal(documents.has(eventRef("test", "aborted-settlement").path), false,
    "aborted request transaction must not leave telemetry work behind");

  const beforeGuards = attempts.length;
  await aggregateReplayDaily(delivery("unknown", "ignored"));
  await aggregateReplayDaily({params: {env: "test", eventId: "missing"}});
  assert.equal(attempts.length, beforeGuards, "invalid environment and missing event data skip I/O");
  console.log("replay telemetry: atomic enqueue/consume, duplicate/reordered delivery, UTC day, isolation and retries passed");
}

main().catch(error => {console.error(error); process.exitCode = 1;})
  .finally(() => {Date.now = originalNow;});
