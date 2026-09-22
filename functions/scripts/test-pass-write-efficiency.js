"use strict";
// Real pass store: distinguish no-op XP from season/schema initialization and reward claims.
const assert = require("node:assert/strict");
const path = require("node:path");
const lib = process.env.FUNCTIONS_TEST_LIB || path.resolve(__dirname, "../lib");
const {beginPassMutation, commitPassExp, commitPassClaim, commitPassRepeatClaim, passProgressResponse} =
  require(path.join(lib, "pass/passStore"));
const clone = (value) => value === undefined ? undefined : structuredClone(value);
const base = () => ({schemaVersion: 1, seasonId: "s1", exp: 70,
  claimed: {"1": true}, repeatClaimed: 2, premiumUnlocked: true, premiumClaimed: {"2": true}, updatedAt: "old"});
let passed = 0;
async function scenario(name, saved, amount, expectedWrites, check, season = "s1") {
  const writes = [];
  const ref = {path: "envs/test/users/pass-efficiency/pass/current"};
  const snapshot = {exists: saved !== undefined, data: () => clone(saved)};
  const tx = {get: async () => snapshot, set: (_ref, value) => writes.push(clone(value))};
  const mutation = await beginPassMutation(tx, {doc: () => ref}, "test", "pass-efficiency", season);
  commitPassExp(tx, mutation, amount, "now");
  assert.equal(writes.length, expectedWrites, name);
  check({mutation, writes, tx, progress: passProgressResponse(mutation.state)});
  passed++;
  console.log("PASS " + name);
}
(async () => {
  await scenario("zero XP preserves latest response without writing", base(), 0, 0, ({progress}) => {
    const {schemaVersion, updatedAt, ...expected} = base();
    assert.deepEqual(progress, expected);
  });
  await scenario("capped XP gain does not write", {...base(), exp: 100000000}, 50, 0,
    ({progress}) => assert.equal(progress.exp, 100000000));
  await scenario("positive XP still writes and preserves claims", base(), 30, 1, ({writes, mutation}) => {
    assert.equal(writes[0].exp, 100);
    assert.deepEqual(writes[0].claimed, {"1": true});
    assert.deepEqual(writes[0].premiumClaimed, {"2": true});
    assert.equal(mutation.original.exp, 70, "original snapshot must not alias mutable state");
  });
  await scenario("missing pass initializes even with zero XP", undefined, 0, 1, ({writes}) => {
    assert.equal(writes[0].seasonId, "s1");
    assert.equal(writes[0].exp, 0);
  });
  await scenario("season rollover persists reset even with zero XP", base(), 0, 1, ({writes}) => {
    assert.equal(writes[0].seasonId, "s2");
    assert.equal(writes[0].exp, 0);
    assert.deepEqual(writes[0].claimed, {});
    assert.deepEqual(writes[0].premiumClaimed, {});
    assert.equal(writes[0].premiumUnlocked, false);
    assert.equal(writes[0].repeatClaimed, 0);
  }, "s2");
  await scenario("old schema still upgrades with zero XP", {...base(), schemaVersion: 0}, 0, 1,
    ({writes}) => assert.equal(writes[0].schemaVersion, 1));
  await scenario("premium claims still persist after no-op XP", base(), 0, 0, ({mutation, writes, tx}) => {
    commitPassClaim(tx, mutation, 3, "now", "premium");
    assert.equal(writes.length, 1);
    assert.deepEqual(writes[0].premiumClaimed, {"2": true, "3": true});
    assert.deepEqual(mutation.original.premiumClaimed, {"2": true});
  });
  await scenario("repeat claims still persist after no-op XP", base(), 0, 0, ({mutation, writes, tx}) => {
    commitPassRepeatClaim(tx, mutation, 3, "now");
    assert.equal(writes.length, 1);
    assert.equal(writes[0].repeatClaimed, 3);
  });
  console.log(`Pass write efficiency: ${passed} focused regression scenarios passed.`);
})().catch((error) => { console.error(error); process.exitCode = 1; });
