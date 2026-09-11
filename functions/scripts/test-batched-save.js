// mutateSave의 실제 코덱과 쓰기 경로를 사용한다. DB I/O만 메모리로 대체한다.
const assert = require("node:assert/strict");
const Module = require("node:module");
const originalLoad = Module._load;
const documents = new Map();
const attempts = [];
let retryAfterFirst;

const SAVE = "envs/test/users/me/save/current";
const WALLET = "envs/test/users/me/wallet/current";
const RECEIPT = `${WALLET}/receipts/batched-save`;
const EXTRA = "envs/test/users/me/missions/current";
const SOURCE = "batched-save";
const receipt = {kind: "client", txId: SOURCE};

function reference(path) {
  return {path, collection: name => reference(`${path}/${name}`),
    doc: name => reference(`${path}/${name}`)};
}
const db = {doc: reference, collection: reference};

class HttpsError extends Error {
  constructor(code, message, details) {
    super(message);
    this.code = code;
    this.details = details;
  }
}

async function runTransaction(source, run) {
  assert.equal(source, SOURCE);
  for (let index = 0; index < 2; index++) {
    const state = {reads: [], batches: [], singleReads: [], writes: [], decoded: []};
    attempts.push(state);
    const snapshot = ref => {
      assert.equal(state.writes.length, 0, "all transaction reads precede writes");
      state.reads.push(ref.path);
      const value = structuredClone(documents.get(ref.path));
      return {exists: value !== undefined, data: () => {
        state.decoded.push(ref.path);
        return value;
      }};
    };
    const write = kind => (ref, value) => {
      state.writes.push({kind, path: ref.path, value: structuredClone(value)});
    };
    const transaction = {
      getAll: async (...refs) => {
        state.batches.push(refs.map(ref => ref.path));
        return refs.map(snapshot);
      },
      get: async ref => {
        state.singleReads.push(ref.path);
        return snapshot(ref);
      },
      create: write("create"), set: write("set"), update: write("update"),
    };
    const result = await run(transaction);
    if (index === 0 && retryAfterFirst) {
      retryAfterFirst();
      continue; // aborted attempt must not persist staged writes
    }
    for (const entry of state.writes) {
      if (entry.kind === "create") assert.equal(documents.has(entry.path), false);
      if (entry.kind === "update") assert.equal(documents.has(entry.path), true);
      const previous = entry.kind === "update" ? documents.get(entry.path) : {};
      documents.set(entry.path, {...previous, ...entry.value});
    }
    return result;
  }
  assert.fail("retry did not finish");
}

Module._load = function(request, parent, isMain) {
  if (request === "firebase-functions/v2/https") return {HttpsError};
  if (request === "firebase-functions/logger") return {error() {}, warn() {}};
  if (request === "firebase-admin/firestore") return {
    FieldValue: {serverTimestamp: () => "server-time"},
  };
  if (parent?.filename.endsWith("saveDocument.js")) {
    if (request === "../firebaseApp") return {db};
    if (request === "../observability/countedTransaction") return {
      withCountedTransaction: runTransaction,
    };
  }
  return originalLoad.call(this, request, parent, isMain);
};
let mutateSave, SCHEMA_VERSION, nextWallet;
try {
  ({mutateSave, SCHEMA_VERSION} = require("../lib/save/saveDocument"));
  ({nextWallet} = require("../lib/currency/walletStore"));
} finally {
  Module._load = originalLoad;
}

function reset() {
  documents.clear();
  attempts.length = 0;
  retryAfterFirst = undefined;
  documents.set(SAVE, {schemaVersion: SCHEMA_VERSION, revision: 10,
    ownership: {ownedIds: [1]}});
  documents.set(WALLET, {rev: 4, balances: {Gold: 100}, paidBalances: {Gold: 50}});
}

function assertInitialBatch(attempt = attempts.at(-1)) {
  assert.deepEqual(attempt.batches, [[SAVE, WALLET, RECEIPT]],
    "save, wallet and receipt share one initial read round trip");
  assert.deepEqual(attempt.reads.slice(0, 3), [SAVE, WALLET, RECEIPT]);
}

const invoke = (mutate, finalize = result => result, legacy) =>
  mutateSave("test", "me", SOURCE, receipt, mutate, finalize, legacy);
const forbidden = () => assert.fail("receipt/error path must skip callbacks");

async function main() {
  reset();
  let finalized = 0;
  const response = await invoke((current, transaction, wallet) => {
    assert.equal(current.revision, 10);
    assert.equal(wallet.rev, 4);
    return {slots: {ownership: {ownedIds: [1, 2]}},
      wallet: nextWallet(wallet, {...wallet.balances, Gold: 80}, SOURCE)};
  }, result => {finalized++; return {...result, item: 2};});
  assertInitialBatch();
  assert.deepEqual(attempts[0].singleReads, []);
  assert.equal(attempts[0].reads.length, 3);
  assert.equal(attempts[0].writes.length, 3);
  assert.equal(finalized, 1);
  assert.equal(response.revision, 11);
  assert.equal(response.wallet.rev, 5);
  assert.equal(response.wallet.balances.Gold, 80);
  assert.deepEqual(documents.get(SAVE).ownership, {ownedIds: [1, 2]});
  assert.deepEqual(documents.get(WALLET).paidBalances, {Gold: 50});

  const replay = await invoke(forbidden, forbidden);
  assert.deepEqual(replay, response, "actual receipt codec round trip");
  assertInitialBatch();
  assert.equal(attempts.at(-1).reads.length, 3);
  assert.deepEqual(attempts.at(-1).writes, [], "receipt replay is read-only");
  assert.deepEqual(attempts.at(-1).decoded, [SAVE, WALLET, RECEIPT],
    "batched fetching preserves validation/decode order");

  documents.get(RECEIPT).source = "another-command";
  await assert.rejects(invoke(forbidden, forbidden), error =>
    error.code === "permission-denied" && error.details.reason === "TxIdReused");
  assert.deepEqual(attempts.at(-1).writes, []);
  documents.get(RECEIPT).source = SOURCE;
  documents.get(SAVE).revision++;
  await assert.rejects(invoke(forbidden, forbidden), error =>
    error.code === "failed-precondition" && /cached revision/.test(error.message));
  assert.deepEqual(attempts.at(-1).writes, []);

  reset();
  documents.set(RECEIPT, {source: SOURCE, result: "invalid JSON"});
  const validSave = documents.get(SAVE);
  documents.delete(SAVE);
  await assert.rejects(invoke(forbidden), error =>
    error.code === "failed-precondition" && /does not exist/.test(error.message));
  assertInitialBatch();
  assert.deepEqual(attempts.at(-1).decoded, []);
  documents.set(SAVE, {...validSave, schemaVersion: SCHEMA_VERSION + 1});
  await assert.rejects(invoke(forbidden), {code: "out-of-range"});
  assert.deepEqual(attempts.at(-1).decoded, [SAVE], "schema error precedes malformed receipt");
  documents.set(SAVE, validSave);
  await assert.rejects(invoke(forbidden), SyntaxError, "bad receipt must never rerun mutation");
  assert.deepEqual(attempts.at(-1).decoded, [SAVE, WALLET, RECEIPT]);
  assert.deepEqual(attempts.at(-1).writes, []);

  reset();
  const legacy = {wallet: {rev: 3, balances: {Gold: 99}}, legacy: true};
  documents.set(RECEIPT, {source: SOURCE, result: JSON.stringify(legacy)});
  assert.deepEqual(await invoke(forbidden, forbidden, cached => cached?.legacy === true), legacy);
  assertInitialBatch();
  assert.deepEqual(attempts.at(-1).writes, [], "validated legacy receipt remains read-only");

  for (const grantsGold of [false, true]) {
    reset();
    documents.delete(WALLET);
    const created = await invoke((current, transaction, wallet) => {
      assert.equal(wallet.rev, 0);
      assert.equal(wallet.balances.Gold, 0);
      return {slots: {}, ...(grantsGold ? {
        wallet: nextWallet(wallet, {Gold: 25}, SOURCE),
      } : {})};
    });
    assertInitialBatch();
    assert.equal(created.wallet.rev, 1);
    assert.equal(created.wallet.balances.Gold, grantsGold ? 25 : 0);
    assert.equal(documents.get(WALLET).rev, 1);
    assert.equal(attempts[0].writes.find(entry => entry.path === WALLET).kind, "create");
    assert.deepEqual(await invoke(forbidden), created, "new wallet and receipt commit together");
  }

  reset();
  documents.set(EXTRA, {progress: 2});
  retryAfterFirst = () => {
    documents.set(SAVE, {...documents.get(SAVE), revision: 20});
    documents.set(WALLET, {...documents.get(WALLET), rev: 9, balances: {Gold: 200}});
    documents.set(EXTRA, {progress: 8});
  };
  const seen = [];
  const retried = await invoke(async (current, transaction, wallet) => {
    const extra = (await transaction.get(reference(EXTRA))).data();
    seen.push([current.revision, wallet.rev, wallet.balances.Gold, extra.progress]);
    transaction.set(reference(EXTRA), {progress: extra.progress + 1});
    return {slots: {ownership: {ownedIds: [extra.progress]}}};
  });
  assert.deepEqual(seen, [[10, 4, 100, 2], [20, 9, 200, 8]],
    "transaction retries must refetch every snapshot, including callback reads");
  assert.equal(attempts.length, 2);
  for (const attempt of attempts) {
    assertInitialBatch(attempt);
    assert.deepEqual(attempt.singleReads, [EXTRA]);
    assert.equal(attempt.reads.length, 4);
  }
  assert.equal(retried.revision, 21);
  assert.equal(retried.wallet.rev, 9);
  assert.equal(documents.get(EXTRA).progress, 9);
  assert.deepEqual(documents.get(SAVE).ownership, {ownedIds: [8]});
  assert.equal(documents.get(WALLET).balances.Gold, 200, "slot-only mutation keeps wallet unchanged");
  console.log("PASS batched mutateSave: 1 batch/3 reads, replay, validation order, wallet creation, callback reads, retry snapshots");
}

main().catch(error => {console.error(error); process.exitCode = 1;});
