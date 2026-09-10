const assert = require("node:assert/strict");
const {canonical, csv, hash, valueOf, unpack, localSnapshot, makePlan, applyPlan, checkCommit, releaseHistory} = require("./publish-local-spec");
const {parseRewardRows} = require("../lib/rewardTable");
const {duplicateGains} = require("../lib/rewards/itemGrant");

async function run() {
  const text = '한글"\\\n\r\t\b\f' + String.fromCharCode(1);
  const encoded = canonical([[text, "literal\\b"]]);
  assert.deepEqual(JSON.parse(encoded), [[text, "literal\\b"]]);
  assert(encoded.includes("\\u0008") && encoded.includes("\\u000c"), "Match C# control escaping");
  assert.deepEqual(csv('\ufeffid,text\r\n1,"a,b"\r\n2,"line1\r\nline2 and ""quote"""'),
    [["id", "text"], ["1", "a,b"], ["2", 'line1\r\nline2 and "quote"']]);
  assert.throws(() => csv('1,"unfinished'));
  assert.throws(() => checkCommit([{update: {name: "same"}}, {delete: "same"}]));
  assert.throws(() => checkCommit(Array.from({length: 501}, (_, i) => ({delete: String(i)}))));
  assert.throws(() => checkCommit([{verify: "unsupported"}]));
  assert.deepEqual(releaseHistory(["4.1", "4.2", "4.2"], "4.3"), ["4.1", "4.2", "4.3"]);
  assert.deepEqual(releaseHistory(Array.from({length: 25}, (_, i) => `4.${i}`), "4.25"),
    Array.from({length: 20}, (_, i) => `4.${i + 6}`), "Keep the newest 20 releases in ascending publication order");

  const local = localSnapshot();
  assert.equal(local.names.length, 22);
  assert(local.names.includes("Mission"), "Server-only Mission must be published");
  const rewards = parseRewardRows(local.tables.Reward.rows);
  for (const [grade, amount] of [["Common", 1], ["Rare", 2], ["Arcane", 5], ["Mythic", 10]]) {
    assert.deepEqual(duplicateGains([{cardId: 1, isNew: false, snack: 1}], new Map([[1, grade]]), rewards),
      [{currency: "Shard", amount}]);
  }
  const base = "projects/bm-cardbattle/databases/cardbattle/documents/envs/test/specs";
  const docs = new Map(); let commits = 0;
  const doc = (name, data) => ({name, fields: valueOf(data).mapValue.fields, updateTime: "2026-09-09T00:00:00Z"});
  docs.set(`${base}/_index`, doc(`${base}/_index`, {major: local.major, minor: 12, nextMinor: 13,
    contentVersion: "4.12", history: ["4.11", "4.12"]}));
  for (const name of local.names) {
    const table = local.tables[name];
    const owner = table.columns.indexOf("ownerType");
    const matrix = name === "Reward" ? [table.columns, ...table.matrix.slice(1).filter((r) => r[owner] !== "CardDuplicate")] : table.matrix;
    const payload = canonical(matrix);
    const data = {schemaVersion: local.major, major: local.major, revision: 7,
      rowCount: matrix.length - 1, payloadHash: hash(payload), payload};
    const metadata = {...data, columns: table.columns, rowsRevision: 7};
    if (name === "Card") delete metadata.major; // Legacy metadata uses schemaVersion as the alias.
    docs.set(`${base}/${name}`, doc(`${base}/${name}`, metadata));
    docs.set(`${base}/${name}/blob/current`, doc(`${base}/${name}/blob/current`, data));
  }
  const request = async (name, body) => {
    if (body) {
      assert.equal(name, "projects/bm-cardbattle/databases/cardbattle/documents:commit");
      // Model Firestore's all-or-nothing precondition phase before applying any writes.
      for (const w of body.writes) {
        const key = w.update?.name || w.delete;
        if (w.currentDocument?.exists === false) assert(!docs.has(key));
        if (w.currentDocument?.updateTime) assert.equal(docs.get(key)?.updateTime, w.currentDocument.updateTime);
      }
      for (const w of body.writes) {
        if (w.update) docs.set(w.update.name, {...w.update,
          fields: w.updateMask ? {...docs.get(w.update.name).fields, ...w.update.fields} : w.update.fields,
          updateTime: "2026-09-09T01:00:00Z"});
        if (w.delete) docs.delete(w.delete);
      }
      commits++; return {};
    }
    assert(docs.has(name), `Unexpected read ${name}`);
    return docs.get(name);
  };
  const plan = await makePlan("test", request);
  assert.deepEqual(plan.changes.map((c) => c.table), ["Reward"]);
  assert.equal(plan.version, "4.13");
  assert.equal(commits, 0, "Dry-run cannot mutate remote state");
  assert.equal(plan.writes.filter((w) => w.updateMask).length, 21);
  const immutable = plan.writes.filter((w) => w.update?.name.includes("/_release_"));
  assert.equal(immutable.length, 23);
  assert(immutable.every((w) => w.currentDocument?.exists === false));
  const release = plan.writes.find((w) => w.update?.name.endsWith("/_release_index_4_13"));
  const rollbackTables = JSON.parse(unpack(release.update.fields.tablesJson));
  assert.deepEqual(unpack(rollbackTables), plan.expectedTables, "Keep official rollback envelope");
  const index = plan.writes.find((w) => w.update?.name.endsWith("/_index"));
  assert.deepEqual(unpack(index.update.fields.history), ["4.11", "4.12", "4.13"]);
  await applyPlan(plan, request);
  assert.equal(commits, 1, "Tables and published vector must change atomically");
  await assert.rejects(() => applyPlan(plan, request), "Reusing a consumed plan must fail CAS");
  assert.equal(commits, 1);
  const stale = structuredClone(plan); stale.sources["Assets/Resources/SpecData.bytes"] = "changed";
  await assert.rejects(() => applyPlan(stale, request), /Local inputs changed/);
  assert.equal(commits, 1);
  console.log("publish-local-spec: CSV/bytes, duplicate rewards, canonical payload, CAS, immutable release and rollback tests passed");
}
run().catch((e) => { console.error(e); process.exitCode = 1; });
