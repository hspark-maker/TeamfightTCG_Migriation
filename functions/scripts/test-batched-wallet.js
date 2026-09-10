// Real wallet codec, mutation helper and payout callable; only DB transport is mocked.
const assert = require("node:assert/strict");
const Module = require("node:module");
const documents = new Map();
const attempts = [];
let abortCommit = false;
let retryOnce;
const WALLET = "envs/test/users/me/wallet/current";
const GUARD = "envs/test/matches/guard";
const SOURCE = "batched-wallet";
const receipt = {kind: "client", txId: SOURCE};
const RECEIPT = `${WALLET}/receipts/${SOURCE}`;
const PAYOUTS = "envs/test/users/me/payouts";
const ids = ["a", "b", "c", "d", "e"].map(letter => letter.repeat(32));

function reference(path) {
  return {path, collection: name => reference(`${path}/${name}`),
    doc: name => reference(`${path}/${name}`)};
}
async function runTransaction(run) {
  const retry = retryOnce;
  retryOnce = undefined;
  if (retry) {
    await attempt(run, false);
    retry();
  }
  return attempt(run, true);
}
async function attempt(run, commit) {
  const state = {batches: [], singleReads: [], decoded: [], writes: []};
  attempts.push(state);
  const snapshot = ref => {
    assert.equal(state.writes.length, 0, "reads precede every write");
    const value = structuredClone(documents.get(ref.path));
    return {exists: value !== undefined, data: () => {
      state.decoded.push(ref.path);
      return value;
    }};
  };
  const write = kind => (ref, value, options) => {
    state.writes.push({kind, path: ref.path, value: structuredClone(value), options});
  };
  const result = await run({
    getAll: async (...refs) => {
      state.batches.push(refs.map(ref => ref.path));
      return refs.map(snapshot);
    },
    get: async ref => {state.singleReads.push(ref.path); return snapshot(ref);},
    set: write("set"), create: write("create"),
  });
  if (!commit) return result;
  if (abortCommit) {
    abortCommit = false;
    throw Error("injected commit failure");
  }
  for (const write of state.writes) {
    if (write.kind === "create") assert.equal(documents.has(write.path), false);
    const previous = write.options?.merge ? documents.get(write.path) : {};
    documents.set(write.path, {...previous, ...write.value});
  }
  return result;
}
const db = {doc: reference, collection: reference, runTransaction};
class HttpsError extends Error {
  constructor(code, message, details) {super(message); this.code = code; this.details = details;}
}
const originalLoad = Module._load;
Module._load = function(request, parent, isMain) {
  if (request === "firebase-functions/v2/https") return {
    HttpsError, onCall: (...args) => args.at(-1),
  };
  if (request === "firebase-functions/logger") return {info() {}, warn() {}, error() {}};
  if (request === "firebase-admin/firestore") return {
    FieldValue: {serverTimestamp: () => "server-time"}, Timestamp: class Timestamp {},
  };
  if (/walletTransaction\.js$|claimPayout\.js$/.test(parent?.filename ?? "")) {
    if (request === "../firebaseApp") return {db};
    if (request === "../observability/countedTransaction") return {
      withCountedTransaction: (_, run) => runTransaction(run),
    };
  }
  return originalLoad.call(this, request, parent, isMain);
};
let mutateWallet, claimPayout, nextWallet;
try {
  ({mutateWallet} = require("../lib/currency/walletTransaction"));
  ({claimPayout} = require("../lib/commands/claimPayout"));
  ({nextWallet} = require("../lib/currency/walletStore"));
} finally {
  Module._load = originalLoad;
}
const forbidden = () => assert.fail("replay/rejection must skip callbacks");
const invoke = (mutate, finalize = wallet => ({wallet}), guard) =>
  mutateWallet("test", "me", SOURCE, receipt, mutate, finalize, guard);
const credit = wallet => nextWallet(wallet, {...wallet.balances, Gold: wallet.balances.Gold + 20}, SOURCE);
const ack = (matchIds = ids, txId = "payout-batch") => claimPayout({auth: {uid: "me"},
  data: {env: "test", action: "ack", matchIds, txId}});
function reset() {
  documents.clear(); attempts.length = 0; abortCommit = false; retryOnce = undefined;
  documents.set(WALLET, {rev: 4, balances: {Gold: 100}, paidBalances: {Gold: 30}});
  documents.set(GUARD, {eligible: true});
}
function walletBatch() {
  assert.deepEqual(attempts.at(-1).batches, [[WALLET, RECEIPT]]);
}
function payoutBatch(matchIds = ids) {
  assert.deepEqual(attempts.at(-1).batches,
    [[...matchIds.map(id => `${PAYOUTS}/${id}`), WALLET, `${WALLET}/receipts/payout-batch`]]);
  assert.deepEqual(attempts.at(-1).singleReads, []);
}
function seedPayouts() {
  ids.forEach((id, i) => documents.set(`${PAYOUTS}/${id}`, {
    uid: "me", matchId: id, status: "ready", currency: {currency: "Gold", amount: (i + 1) * 10},
  }));
  documents.get(`${PAYOUTS}/${ids[1]}`).uid = "other";
  documents.get(`${PAYOUTS}/${ids[2]}`).matchId = ids[0];
  documents.get(`${PAYOUTS}/${ids[3]}`).status = "claimed";
  documents.get(`${PAYOUTS}/${ids[4]}`).currency.amount = -1;
}

async function main() {
  reset();
  const guard = {ref: reference(GUARD), verify: snapshot => {
    assert.equal(snapshot.data().eligible, true);
  }, stamp: {eligible: false}};
  const result = await invoke(credit, undefined, guard);
  walletBatch();
  assert.deepEqual(attempts[0].singleReads, [GUARD]);
  assert.equal(attempts[0].writes.length, 3, "wallet, receipt and eligibility stamp commit together");
  assert.equal(result.wallet.rev, 5);
  assert.equal(result.wallet.balances.Gold, 120);
  assert.equal(documents.get(GUARD).eligible, false);
  assert.deepEqual(documents.get(WALLET).paidBalances, {Gold: 30});
  assert.deepEqual(await invoke(forbidden, forbidden, {...guard, verify: forbidden}), result);
  walletBatch();
  assert.deepEqual(attempts.at(-1).singleReads, [], "replay skips eligibility read");
  assert.deepEqual(attempts.at(-1).writes, [], "replay writes nothing");

  documents.get(RECEIPT).source = "other";
  await assert.rejects(invoke(forbidden, forbidden, guard), error =>
    error.code === "permission-denied" && error.details.reason === "TxIdReused");
  assert.deepEqual(attempts.at(-1).singleReads, []);
  assert.deepEqual(attempts.at(-1).writes, []);
  documents.set(RECEIPT, {source: SOURCE, result: "malformed JSON"});
  documents.delete(WALLET);
  await assert.rejects(invoke(forbidden), {code: "failed-precondition"});
  assert.deepEqual(attempts.at(-1).decoded, [], "missing wallet wins over malformed receipt");

  reset();
  await assert.rejects(invoke(forbidden, forbidden, {...guard, verify: () => {
    throw new HttpsError("permission-denied", "ineligible");
  }}), {code: "permission-denied"});
  walletBatch();
  assert.deepEqual(attempts.at(-1).singleReads, [GUARD]);
  assert.deepEqual(attempts.at(-1).writes, []);
  const beforeWalletAbort = structuredClone(documents);
  abortCommit = true;
  await assert.rejects(invoke(credit, undefined, guard), /injected commit failure/);
  assert.deepEqual(documents, beforeWalletAbort, "guard and credit both roll back on commit failure");
  retryOnce = () => documents.set(WALLET, {rev: 8, balances: {Gold: 200}});
  const retried = await invoke(credit);
  assert.equal(retried.wallet.rev, 9);
  assert.equal(retried.wallet.balances.Gold, 220, "retry uses fresh batched snapshots");

  reset();
  assert.deepEqual(await ack([]), {acked: [], wallet: null});
  assert.equal(attempts.length, 0, "empty ack does not open a transaction");
  seedPayouts();
  const payoutResult = await ack([ids[0], ids[0], ...ids.slice(1)]);
  payoutBatch();
  assert.deepEqual(payoutResult.acked, [ids[0], ids[4]], "UID, match ID and ready status map to each snapshot");
  assert.equal(payoutResult.wallet.balances.Gold, 110, "only valid accepted gain is credited");
  assert.equal(payoutResult.wallet.rev, 5);
  assert.equal(attempts[0].writes.length, 4, "two payout stamps plus wallet and receipt");
  assert.equal(documents.get(`${PAYOUTS}/${ids[1]}`).status, "ready");
  assert.equal(documents.get(`${PAYOUTS}/${ids[2]}`).status, "ready");
  assert.deepEqual(await ack(), payoutResult);
  payoutBatch();
  assert.deepEqual(attempts.at(-1).writes, [], "ack replay does not stamp or credit twice");

  const payoutReceipt = `${WALLET}/receipts/payout-batch`;
  documents.get(payoutReceipt).source = "other";
  await assert.rejects(ack(), error => error.code === "permission-denied" && error.details.reason === "TxIdReused");
  assert.deepEqual(attempts.at(-1).writes, []);
  documents.set(payoutReceipt, {source: "claimPayout", result: "malformed JSON"});
  documents.delete(WALLET);
  await assert.rejects(ack(), {code: "failed-precondition"});
  assert.deepEqual(attempts.at(-1).decoded, [], "payout wallet validation precedes receipt decoding");

  reset(); seedPayouts();
  const beforePayoutAbort = structuredClone(documents);
  abortCommit = true;
  await assert.rejects(ack(), /injected commit failure/);
  assert.deepEqual(documents, beforePayoutAbort, "payout stamps and credit both roll back");
  await ack([ids[4]]);
  payoutBatch([ids[4]]);
  assert.equal(documents.get(WALLET).rev, 4, "invalid amount marks payout without changing wallet");
  assert.equal(attempts.at(-1).writes.length, 2, "invalid amount writes only payout stamp and receipt");
  console.log("PASS batched wallet/payout: read stages, replay, validation priority, guard, payout mapping, atomic abort and retry");
}
main().catch(error => {console.error(error); process.exitCode = 1;});
