const assert = require("node:assert/strict");
const Module = require("node:module");
const originalLoad = Module._load;
const documents = new Map();
let options;
let reads = [];
let writes = [];
let transactionFailure;

function snapshot(value) {
  const data = structuredClone(value);
  return {exists: data !== undefined, data: () => data};
}
const db = {
  doc: path => ({path}),
  runTransaction: async run => {
    if (transactionFailure) throw transactionFailure;
    const pending = [];
    await run({
      getAll: async (...refs) => {
        assert.equal(pending.length, 0, "reads must precede writes");
        reads.push(...refs.map(ref => ref.path));
        return refs.map(ref => snapshot(documents.get(ref.path)));
      },
      set: (ref, value, config) => pending.push({path: ref.path, value, config}),
    });
    for (const write of pending) {
      assert.deepEqual(write.config, {mergeFields: ["profile"]});
      const old = documents.get(write.path);
      documents.set(write.path, {...old, ...write.value});
      writes.push(write);
    }
  },
};
Module._load = function(request, parent, isMain) {
  if (parent?.filename.endsWith("syncRankProfile.js")) {
    if (request === "firebase-functions/v2/firestore") return {
      onDocumentWritten: (config, handler) => {options = config; return handler;},
    };
    if (request === "../firebaseApp") return {db, DATABASE_ID: "test-database"};
  }
  return originalLoad.call(this, request, parent, isMain);
};
let syncRankProfile;
try {
  ({syncRankProfile} = require("../lib/commands/syncRankProfile"));
} finally {
  Module._load = originalLoad;
}

function event(env, before, after) {
  return {params: {env, uid: "me"}, data: {before: snapshot(before), after: snapshot(after)}};
}
function resetCost() {reads = []; writes = [];}

async function main() {
  assert.deepEqual(options, {
    document: "envs/{env}/users/{uid}/save/current", database: "test-database", retry: true,
  });
  const oldProfile = {nickname: "old", avatarId: "avatar-old", frameId: "frame-old"};
  const latest = {profile: {nickname: "abcdefghijklmnop", avatarId: 42, frameId: "frame-new",
    privateToken: "never publish"}, ownership: {private: true}};
  const expected = {nickname: "abcdefghijkl", avatarId: "", frameId: "frame-new"};

  for (const env of ["live", "test"]) {
    documents.clear();
    resetCost();
    const savePath = `envs/${env}/users/me/save/current`;
    const boardPath = `envs/${env}/rankings/me`;
    const otherBoardPath = `envs/${env === "live" ? "test" : "live"}/rankings/me`;
    const rank = {points: 777, seasonId: "S1", updatedAt: "rank-time"};
    documents.set(savePath, latest);
    documents.set(boardPath, {...rank, profile: {...oldProfile, privateToken: "legacy secret"}});
    documents.set(otherBoardPath, {...rank, profile: oldProfile});
    const changed = event(env, {profile: oldProfile}, latest);
    await syncRankProfile(changed);
    assert.deepEqual(reads, [savePath, boardPath]);
    assert.equal(writes.length, 1);
    assert.deepEqual(writes[0].value, {profile: expected}, "write only sanitized public fields");
    assert.deepEqual(documents.get(boardPath), {...rank, profile: expected});
    assert.deepEqual(documents.get(otherBoardPath), {...rank, profile: oldProfile}, "environment isolation");

    resetCost();
    await syncRankProfile(changed);
    assert.equal(writes.length, 0, "duplicate delivery is read-only");

    resetCost();
    await syncRankProfile(event(env, latest, {profile: oldProfile}));
    assert.deepEqual(reads, [savePath, boardPath], "old event must reread latest save");
    assert.equal(writes.length, 0, "out-of-order event must not roll back latest profile");
    assert.deepEqual(documents.get(boardPath).profile, expected);

    resetCost();
    await syncRankProfile(event(env, latest, {...latest,
      profile: {...latest.profile, privateToken: "changed", nickname: "abcdefghijklZZZZ"},
      ownership: {private: false}}));
    assert.deepEqual(reads, [], "same normalized public profile needs no reads");
    assert.deepEqual(writes, []);

    resetCost();
    documents.delete(boardPath);
    await syncRankProfile(changed);
    assert.equal(writes.length, 0, "profile trigger must not create a rank entry");
    assert.equal(documents.has(boardPath), false);

    resetCost();
    documents.set(boardPath, {...rank, profile: expected});
    documents.delete(savePath);
    await syncRankProfile(event(env, latest, undefined));
    assert.deepEqual(documents.get(boardPath), {...rank,
      profile: {nickname: "플레이어", avatarId: "", frameId: ""}}, "deleted save resets profile only");
    assert.equal(writes.length, 1);

    resetCost();
    transactionFailure = new Error("transient transaction failure");
    await assert.rejects(syncRankProfile(changed), transactionFailure,
      "transaction failure must propagate so retry:true can redeliver");
    transactionFailure = undefined;
    assert.deepEqual(writes, []);
  }

  resetCost();
  await syncRankProfile(event("unknown", {profile: oldProfile}, latest));
  await syncRankProfile({params: {env: "live", uid: "me"}});
  assert.deepEqual(reads, []);
  assert.deepEqual(writes, []);
  console.log("PASS syncRankProfile: sanitized fields, latest-save ordering, idempotency, no-op, deletion, retry, environment isolation");
}

main().catch(error => {console.error(error); process.exitCode = 1;});
