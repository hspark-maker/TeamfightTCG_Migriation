// Real metrics, spec cache, transaction proxy and replay client; no remote I/O.
const assert = require("node:assert/strict");
const {major: CONTENT_MAJOR} = require("../../content-version.json");
const Module = require("node:module");
const logs = [];
const reads = [];
const db = {
  doc(path) {
    return {get: () => new Promise(resolve => reads.push({path, resolve}))};
  },
  runTransaction() { throw new Error("Injected transaction database was ignored"); },
};
const originalLoad = Module._load;
let metrics, specs, counted, replay;
try {
  Module._load = function(request, parent, isMain) {
    if (request === "firebase-functions/logger") return {
      info(event, fields) { logs.push({event, ...fields}); }, warn() {}, error() {},
    };
    if (request === "../firebaseApp") return {db};
    return originalLoad.call(this, request, parent, isMain);
  };
  metrics = require("../lib/observability/requestMetrics.js");
  specs = require("../lib/specs/specBlobReader.js");
  counted = require("../lib/observability/countedTransaction.js");
  replay = require("../lib/battleReplayService.js");
} finally {
  Module._load = originalLoad;
}
const {withRequestMetrics, measuredCallable, recordMetric, measurePhase} = metrics;
const drain = () => new Promise(resolve => setImmediate(resolve));
const report = command => logs.find(row => row.event === "request_cost" && row.command === command);

async function main() {
  let release;
  const gate = new Promise(resolve => { release = resolve; });
  const first = withRequestMetrics("first", async () => {
    recordMetric("work", 2);
    await measurePhase("waiting", () => gate);
    recordMetric("work", 3);
    return 42;
  });
  const error = new Error("secret payload must never enter metrics");
  await assert.rejects(withRequestMetrics("second", async () => {
    recordMetric("work", 9);
    throw error;
  }), actual => actual === error);
  release();
  assert.equal(await first, 42);
  assert.equal(report("first").counts.work, 5);
  assert.equal(report("second").counts.work, 9);
  assert.equal(report("second").succeeded, 0);
  assert.ok(report("first").phasesMs.waiting >= 0);
  assert.ok(!JSON.stringify(report("second")).includes(error.message));
  const stream = {};
  const wrapped = measuredCallable("wrapped", (request, response) => {
    assert.equal(response, stream);
    return request.data;
  });
  assert.equal(await wrapped({data: "preserved"}, stream), "preserved");

  // A shared in-flight load is charged once to its creator, never to both callers.
  specs.clearSpecCache();
  const payload = JSON.stringify([["id"], ["1"]]);
  const payloadHash = specs.specPayloadHash(payload);
  const blobPath = "envs/test/specs/Card/releases/one";
  const a = withRequestMetrics("specCreator", () => specs.readSpecRows("test", "Card"));
  const b = withRequestMetrics("specJoiner", () => specs.readSpecRows("test", "Card"));
  assert.equal(reads.length, 1);
  reads[0].resolve({exists: true, data: () => ({major: CONTENT_MAJOR, minor: 0,
    tables: {Card: {blobPath, payloadHash}}})});
  await drain();
  assert.equal(reads.length, 2);
  reads[1].resolve({exists: true, data: () => ({major: CONTENT_MAJOR, payload, payloadHash, rowCount: 1})});
  assert.deepEqual(await a, [{id: 1}]);
  assert.deepEqual(await b, [{id: 1}]);
  assert.equal(report("specCreator").counts.specIndexReads, 1);
  assert.equal(report("specCreator").counts.specBlobReads, 1);
  assert.equal(report("specJoiner").counts.specIndexReads, undefined);
  assert.equal(report("specJoiner").counts.specBlobReads, undefined);
  assert.equal(report("specJoiner").counts.specIndexSharedLoads, 1);
  assert.equal(report("specJoiner").counts.specBlobSharedLoads, 1);
  await withRequestMetrics("specCached", () => specs.readSpecRows("test", "Card"));
  assert.equal(reads.length, 2);
  assert.equal(report("specCached").counts.specIndexCacheHits, 1);
  assert.equal(report("specCached").counts.specBlobCacheHits, 1);

  // Both attempts read/queue writes, only the last successful attempt commits.
  const raw = {async getAll(...refs) { return refs.map(() => ({exists: true})); }, set() {}};
  const injectedDb = {async runTransaction(run) { await run(raw); return run(raw); }};
  await withRequestMetrics("retriedTx", () => counted.withCountedTransaction("testTx", async tx => {
    await tx.getAll({}, {});
    tx.set({}, {});
  }, {}, injectedDb));
  assert.deepEqual(report("retriedTx").counts, {
    txAttempts: 2, txReadCalls: 2, txReadDocuments: 4, txQueuedWrites: 2, txCommittedWrites: 1,
  });
  await assert.rejects(withRequestMetrics("failedTx", () => counted.withCountedTransaction("testTx", async tx => {
    await tx.getAll({});
    tx.set({}, {});
    throw error;
  }, {}, {runTransaction: run => run(raw)})), actual => actual === error);
  assert.equal(report("failedTx").counts.txQueuedWrites, 1);
  assert.equal(report("failedTx").counts.txCommittedWrites, undefined);

  const savedFetch = global.fetch;
  const savedEnv = {...process.env};
  try {
    process.env.BATTLE_REPLAY_URL = "http://local.invalid";
    process.env.FUNCTIONS_EMULATOR = "true";
    process.env.BATTLE_REPLAY_BEARER_TOKEN = "test-token";
    let calls = 0;
    global.fetch = async () => {
      calls++;
      if (calls === 1) throw new Error("mock transport failure");
      return {ok: false, status: 422, json: async () => ({reason: "mock rejection"})};
    };
    const result = await withRequestMetrics("replayRetry", () => replay.callBattleReplay({}));
    assert.equal(result.kind, "rejected");
    assert.equal(report("replayRetry").counts.replayAttempts, 2);
    assert.equal(report("replayRetry").counts.replayHttpCalls, 2);
    assert.equal(report("replayRetry").counts.replayTransportFailures, 1);
    assert.ok(report("replayRetry").phasesMs.replayTotal >= 900, "Retry delay excluded from total");
  } finally {
    global.fetch = savedFetch;
    for (const key of ["BATTLE_REPLAY_URL", "FUNCTIONS_EMULATOR", "BATTLE_REPLAY_BEARER_TOKEN"]) {
      if (savedEnv[key] === undefined) delete process.env[key];
      else process.env[key] = savedEnv[key];
    }
  }
  console.log("request metrics: isolation, failure, callable, spec sharing/cache, retries, replay passed");
}
main().catch(error => { console.error(error); process.exitCode = 1; });
