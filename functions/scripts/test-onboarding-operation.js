"use strict";

const assert = require("node:assert/strict");
const docs = new Map();
let writes = 0;
let reads = 0;
class Ref {
  constructor(path) { this.path = path; }
  get parent() { return new Ref(this.path.slice(0, this.path.lastIndexOf("/"))); }
  collection(id) { return new Ref(`${this.path}/${id}`); }
  doc(id) { return new Ref(`${this.path}/${id}`); }
}
async function runTransaction(callback, options) {
  const staged = [];
  const transaction = {
    async getAll(...refs) {
      assert.equal(staged.length, 0, "Firestore reads must precede writes");
      reads += refs.length;
      return refs.map((ref) => ({exists: docs.has(ref.path), data: () => docs.get(ref.path)}));
    },
    update(ref, value) { staged.push(() => docs.set(ref.path, {...docs.get(ref.path), ...value})); },
    set(ref, value) { staged.push(() => docs.set(ref.path, value)); },
    create(ref, value) {
      assert(!docs.has(ref.path));
      staged.push(() => docs.set(ref.path, value));
    },
  };
  const result = await callback(transaction);
  if (options?.readOnly) assert.equal(staged.length, 0);
  staged.forEach((commit) => commit());
  writes += staged.length;
  return result;
}
function inject(path, exports) {
  require.cache[require.resolve(path)] = {exports};
}
inject("../lib/firebaseApp", {db: {doc: (path) => new Ref(path), collection: (path) => new Ref(path), runTransaction}});
inject("../lib/observability/countedTransaction", {withCountedTransaction: (_, callback) => runTransaction(callback)});
inject("../lib/missions/missionSpec", {readMissionCatalog: async () => []});
const {mutateSave} = require("../lib/save/saveDocument");
const {onboardingReceipt, onboardingFingerprint} = require("../lib/save/onboardingOperation");
const {getOnboardingOperation} = require("../lib/commands/getOnboardingOperation");
const savePath = "envs/test/users/test-user/save/current";
const walletPath = "envs/test/users/test-user/wallet/current";
const operationPath = "envs/test/users/test-user/onboardingOperations/onboard-test-001";
const receiptPath = `${walletPath}/receipts/onboard-test-001`;
const data = {onboarding: true, txId: "onboard-test-001", packId: "starter"};
const receipt = {kind: "client", txId: data.txId, ...onboardingReceipt(data, "openPack")};
let executions = 0;
async function purchase(key = receipt, source = "openPack") {
  return mutateSave("test", "test-user", source, key, () => {
    executions++;
    return {slots: {ownership: {cardIds: [17]}}};
  }, (state) => ({...state, cards: [{cardId: 17}], missions: {stale: true}}));
}
async function recover(args = {packId: "starter"}, uid = "test-user") {
  return getOnboardingOperation.run({auth: {uid}, data: {env: "test", txId: data.txId, command: "openPack", args}});
}

(async () => {
  docs.set(savePath, {schemaVersion: 8, revision: 4, ownership: {cardIds: []}});
  docs.set(walletPath, {schemaVersion: 1, rev: 2, balances: {Gold: 100}});
  assert.deepEqual(onboardingReceipt({}, "openPack"), {});
  assert.throws(() => onboardingReceipt({onboarding: true}, "openPack"));
  assert.equal(onboardingFingerprint("enhanceCard", {cardId: 17}),
    onboardingFingerprint("enhanceCard", {cardId: 17, amount: 1, freeShot: false}));
  assert.notEqual(onboardingFingerprint("enhanceCard", {cardId: 17}),
    onboardingFingerprint("enhanceCard", {cardId: 17, amount: 2}));
  assert.throws(() => onboardingFingerprint("devResetSave", {}));
  const first = await purchase();
  assert.equal(first.revision, 5);
  assert(docs.has(operationPath));
  assert(!("expiresAt" in docs.get(operationPath)));
  assert.equal(JSON.parse(docs.get(operationPath).cachedJson).updatedSlots, undefined);
  const firstWrites = writes;
  assert.deepEqual(await purchase(), first);
  assert.equal(writes, firstWrites);
  docs.delete(receiptPath);
  assert.deepEqual(await purchase(), first, "TTL deletion cannot cause a second purchase");
  assert.equal(executions, 1);
  await assert.rejects(purchase({...receipt, ...onboardingReceipt({...data, packId: "other"}, "openPack")}), /TxIdReused/);
  await assert.rejects(purchase({kind: "client", txId: data.txId}), /TxIdReused/);
  await assert.rejects(purchase(receipt, "enhanceCard"), /TxIdReused/);
  docs.set(savePath, {...docs.get(savePath), revision: 9, ownership: {cardIds: [17, 22]}});
  docs.set(walletPath, {schemaVersion: 1, rev: 7, balances: {Gold: 45}});
  await assert.rejects(purchase(), /OnboardingRecoveryRequired/);
  assert.equal(executions, 1);
  const recovered = await recover();
  assert.equal(recovered.found, true);
  assert.equal(recovered.operation.result.revision, 5);
  assert.equal(recovered.operation.result.wallet, undefined);
  assert.equal(recovered.operation.result.updatedSlots, undefined);
  assert.equal(recovered.operation.result.missions, undefined);
  assert.equal(recovered.current.save.revision, 9);
  assert.equal(recovered.current.wallet.rev, 7);
  assert.deepEqual(recovered.current.save.ownership.cardIds, [17, 22]);
  assert(recovered.current.missions);
  assert.equal(writes, firstWrites, "recovery is read-only");
  await assert.rejects(recover({packId: "other"}), /TxIdReused/);
  await assert.rejects(recover({packId: "starter"}, "another-user"), /Account is not initialized/);
  await assert.rejects(getOnboardingOperation.run({data: {}}), /Sign-in/);
  docs.delete(operationPath);
  assert.equal((await recover()).found, false);
  const legacyKey = {kind: "client", txId: "legacy-test-001"};
  const legacy = await purchase(legacyKey);
  assert.deepEqual(await purchase(legacyKey), legacy, "legacy callers retain receipt replay");
  await assert.rejects(purchase({...legacyKey, onboarding: receipt.onboarding}), /TxIdReused/);
  const failedKey = {kind: "client", txId: "failed-test-001", onboarding: receipt.onboarding};
  const beforeFailure = writes;
  await assert.rejects(mutateSave("test", "test-user", "openPack", failedKey, () => {
    throw new Error("domain failure");
  }, (state) => state), /domain failure/);
  assert.equal(writes, beforeFailure);
  assert(!docs.has("envs/test/users/test-user/onboardingOperations/failed-test-001"));
  assert(reads > 0);
  console.log("onboarding operation: atomic record, TTL independence, argument binding, stale recovery, authentication PASS");
})().catch((error) => { console.error(error); process.exitCode = 1; });
