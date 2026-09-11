// REST requests stay in memory; never connects to Firestore.
const assert = require("node:assert/strict");
const {backfillRankings} = require("./backfill-rankings");
const {valueOf} = require("./publish-local-spec");

const DATABASE = "projects/bm-cardbattle/databases/cardbattle/documents";
const rank = `${DATABASE}/envs/test/users/u1/rank/current`;
const save = `${DATABASE}/envs/test/users/u1/save/current`;
const ranking = `${DATABASE}/envs/test/rankings/u1`;
const profile = {nickname: "새 이름", avatarId: "avatar", frameId: "frame"};
const rankFields = {seasonId: {stringValue: "s1"}, points: {integerValue: "321"},
  updatedAt: {timestampValue: "2026-09-01T00:00:00Z"}};

function fixture(existing, saved = profile, conflict = false) {
  const docs = new Map([
    [rank, {name: rank, fields: structuredClone(rankFields)}],
    [save, {name: save, fields: {profile: valueOf(saved), privateInventory: valueOf("secret")}}],
  ]);
  if (existing) docs.set(ranking, {name: ranking, fields: existing});
  const calls = [];
  let queried = false;
  let transactions = 0;
  let committed = [];
  const request = async (url, body) => {
    const action = url.slice(DATABASE.length + 1);
    calls.push({action, body: structuredClone(body)});
    switch (action) {
    case "runQuery":
      if (queried) return [];
      queried = true;
      // Query snapshot is deliberately stale. Transaction reads must win.
      return [{document: {name: rank, fields: {seasonId: {stringValue: "old"}, points: {integerValue: "1"}}}}];
    case "beginTransaction": return {transaction: `tx${++transactions}`};
    case "batchGet":
      assert(body.transaction);
      assert.deepEqual(new Set(body.documents), new Set([rank, save, ranking]));
      return body.documents.map(name => docs.has(name) ? {found: structuredClone(docs.get(name))} : {missing: name});
    case "commit":
      if (conflict && transactions === 1) {
        docs.get(rank).fields.points = {integerValue: "450"};
        docs.get(save).fields.profile = valueOf({...profile, nickname: "동시 변경"});
        throw new Error("409 ABORTED");
      }
      committed = structuredClone(body.writes);
      for (const write of committed) {
        assert.equal(write.update.name, ranking, "Only rankings projection may be written");
        docs.set(ranking, write.update);
      }
      return {};
    case "rollback": return {};
    default: throw new Error(`Unexpected REST action: ${action}`);
    }
  };
  return {request, calls, docs, writes: () => committed};
}

async function main() {
  const legacy = fixture({...rankFields});
  assert.equal((await backfillRankings(legacy.request, true)).changed, 1);
  const updated = legacy.writes()[0].update.fields;
  assert.deepEqual(updated.profile, valueOf(profile));
  assert.deepEqual(Object.keys(updated).sort(), ["points", "profile", "seasonId", "updatedAt"]);
  assert.deepEqual(legacy.docs.get(rank).fields, rankFields);
  assert.equal(legacy.docs.get(save).fields.privateInventory.stringValue, "secret");

  const profileOnly = fixture({...rankFields, profile: valueOf({...profile, nickname: "이전 이름"})});
  assert.equal((await backfillRankings(profileOnly.request, true)).changed, 1);
  assert.deepEqual(profileOnly.writes()[0].update.fields.profile, valueOf(profile));

  const noop = fixture({...rankFields, profile: valueOf(profile)});
  assert.equal((await backfillRankings(noop.request, true)).changed, 0);
  assert.equal(noop.calls.filter(call => call.action === "commit").length, 0);
  assert.equal(noop.calls.filter(call => call.action === "rollback").length, 1);

  const normalized = fixture(undefined, {nickname: "123456789012345", avatarId: 3, frameId: 0,
    secret: "private"});
  await backfillRankings(normalized.request, true);
  assert.deepEqual(normalized.writes()[0].update.fields.profile,
    valueOf({nickname: "123456789012", avatarId: "", frameId: ""}));

  const missingSave = fixture();
  missingSave.docs.delete(save);
  await backfillRankings(missingSave.request, true);
  assert.deepEqual(missingSave.writes()[0].update.fields.profile,
    valueOf({nickname: "플레이어", avatarId: "", frameId: ""}));

  const retry = fixture(undefined, profile, true);
  assert.equal((await backfillRankings(retry.request, true)).changed, 1);
  assert.equal(retry.calls.filter(call => call.action === "batchGet").length, 2);
  assert.equal(retry.writes()[0].update.fields.points.integerValue, "450");
  assert.equal(retry.writes()[0].update.fields.profile.mapValue.fields.nickname.stringValue, "동시 변경");

  const invalid = fixture();
  invalid.docs.get(rank).fields.points = {integerValue: "-1"};
  await assert.rejects(backfillRankings(invalid.request, true), /Invalid rank source/);
  assert.equal(invalid.calls.filter(call => call.action === "commit").length, 0);

  const plan = fixture();
  const summary = await backfillRankings(plan.request, false);
  assert.equal(summary.changed, 0);
  assert(plan.calls.every(call => call.action === "runQuery"));
  console.log("backfill-rankings: 8 cases passed");
}
main().catch(error => {console.error(error); process.exitCode = 1;});
