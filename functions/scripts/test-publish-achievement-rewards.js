// Offline only: an in-memory Firestore REST fake proves the reviewed migration and CAS behavior.
const assert = require("node:assert/strict");
const {localSnapshot} = require("./publish-all-csv-spec");
const {canonical, hash, valueOf, unpack} = require("./publish-local-spec");
const {validateMigration, makePlan, applyPlan} = require("./publish-achievement-rewards");
const DATABASE = "projects/bm-cardbattle/databases/cardbattle/documents";
const BASE = `${DATABASE}/envs/test/specs`;
const fields = (value) => Object.fromEntries(Object.entries(value).map(([key, val]) => [key, valueOf(val)]));
const plain = (value) => Object.fromEntries(Object.entries(value).map(([key, val]) => [key, unpack(val)]));
const clone = (value) => JSON.parse(JSON.stringify(value));
const local = localSnapshot();
function fixture() {
  const achievements = local.tables.Achievement.rows.map((row) => {
    const reward = local.tables.Reward.rows.find((entry) => entry.ownerType === "Achievement" &&
      entry.ownerId === row.achievementId && entry.rewardType === "Currency");
    return {...row, rewardCurrency: reward.rewardId, rewardAmount: reward.amount};
  });
  const titles = local.tables.Title.rows.map((row) => {
    const reward = local.tables.Reward.rows.find((entry) => entry.rewardType === "Title" && entry.rewardId === row.titleId);
    const owner = achievements.find((entry) => entry.achievementId === reward.ownerId);
    return {...row, eventKey: owner.eventKey, synergyId: owner.synergyId,
      targetCount: owner.targetCount, description: owner.description};
  });
  const columns = {
    Achievement: ["id", "achievementId", "groupId", "stage", "eventKey", "synergyId", "targetCount", "title", "description",
      "rewardCurrency", "rewardAmount", "sortOrder", "enabled"],
    Title: ["id", "titleId", "eventKey", "synergyId", "targetCount", "description"],
    Reward: local.tables.Reward.columns,
    RemoteOnly: ["id", "value"],
  };
  const rows = {Achievement: achievements, Title: titles,
    Reward: local.tables.Reward.rows.filter((row) => row.ownerType !== "Achievement"), RemoteOnly: [{id: 1, value: "keep exact"}]};
  const docs = new Map(); let sequence = 1; let commits = 0;
  const put = (name, values) => docs.set(name, {name, fields: fields(values), updateTime: `v${sequence++}`});
  const tables = {}; const matrices = {};
  for (const name of Object.keys(rows)) {
    const matrix = [columns[name], ...rows[name].map((row) => columns[name].map((key) => String(row[key])))];
    matrices[name] = matrix;
    const payload = canonical(matrix); const payloadHash = hash(payload);
    const blob = {schemaVersion: local.major, major: local.major, revision: 4,
      rowCount: rows[name].length, payload, payloadHash};
    put(`${BASE}/${name}`, {table: name, schemaVersion: local.major, major: local.major, revision: 4,
      rowsRevision: 4, rowCount: rows[name].length, columns: columns[name], idColumn: "id", payloadHash});
    put(`${BASE}/${name}/blob/current`, blob);
    const blobPath = `envs/test/specs/_release_${local.major}_63_${name}`;
    put(`${DATABASE}/${blobPath}`, blob);
    for (const row of rows[name]) put(`${BASE}/${name}/rows/${row.id}`, row);
    tables[name] = {revision: 4, payloadHash, blobPath};
  }
  const index = {major: local.major, minor: 63, nextMinor: 64, minAppMajor: 6,
    contentVersion: `${local.major}.63`, tables, history: [`${local.major}.62`, `${local.major}.63`],
    retainedIndexField: "preserve"};
  put(`${BASE}/_index`, index);
  put(`${BASE}/_release_index_${local.major}_63`, {...index, tablesJson: JSON.stringify(valueOf(tables))});
  const request = async (resource, body) => {
    if (body) {
      assert.equal(resource, `${DATABASE}:commit`);
      commits++;
      // Check every precondition before applying any mutation.
      for (const write of body.writes) {
        const old = docs.get(write.update?.name || write.delete);
        if (write.currentDocument.exists === false) assert(!old, "CAS exists");
        else assert.equal(old?.updateTime, write.currentDocument.updateTime, "CAS updateTime");
      }
      for (const write of body.writes) {
        if (write.delete) { docs.delete(write.delete); continue; }
        const old = docs.get(write.update.name);
        const next = write.updateMask ? {...old.fields, ...write.update.fields} : write.update.fields;
        docs.set(write.update.name, {name: write.update.name, fields: clone(next), updateTime: `v${sequence++}`});
      }
      return {};
    }
    if (resource.includes("/rows?")) return {documents: [...docs.values()].filter((doc) =>
      doc.name.startsWith(resource.split("?")[0] + "/")).map(clone)};
    assert(docs.has(resource), `Missing fake document: ${resource}`);
    return clone(docs.get(resource));
  };
  return {docs, request, matrices, commits: () => commits};
}
async function main() {
  let count = 0;
  const run = async (name, body) => { await body(); count++; console.log(`PASS ${name}`); };
  await run("preserves all old currency/title mappings", () => {
    const proof = validateMigration(fixture().matrices, local.tables);
    assert.equal(proof.oldRewardRows, 213); assert.equal(proof.newRewardRows, 282); assert.equal(proof.mappings.length, 8);
  });
  await run("rejects changed existing reward", () => {
    const changed = clone(local.tables); changed.Reward.matrix[1][6] = "999";
    assert.throws(() => validateMigration(fixture().matrices, changed), /Existing Reward/);
  });
  await run("rejects changed achievement condition", () => {
    const changed = clone(local.tables); changed.Achievement.matrix[1][6] = "99";
    assert.throws(() => validateMigration(fixture().matrices, changed), /conditions/);
  });
  await run("rejects redirected title or missing currency", () => {
    const changed = clone(local.tables); changed.Reward.matrix.at(-1)[2] = "win.1";
    assert.throws(() => validateMigration(fixture().matrices, changed));
    const missing = clone(local.tables); missing.Reward.matrix.pop();
    assert.throws(() => validateMigration(fixture().matrices, missing), /Exactly/);
  });
  await run("rejects any other schema change", () => {
    const previous = fixture().matrices; previous.Title[0][5] = "different";
    assert.throws(() => validateMigration(previous, local.tables), /6-to-2/);
  });
  await run("live rejected before any request", async () => {
    await assert.rejects(makePlan("live", () => { throw new Error("must not request"); }), /test-only/);
  });
  await run("read-only plan preserves remote-only and excludes unpublished local tables", async () => {
    const fake = fixture(); const before = clone([...fake.docs]); const plan = await makePlan("test", fake.request);
    assert.deepEqual([...fake.docs], before); assert.equal(fake.commits(), 0);
    assert.deepEqual(Object.keys(plan.expectedTables), ["Achievement", "Title", "Reward", "RemoteOnly"]);
    assert(plan.writes.length <= 500);
    assert.equal(plan.expectedPayloads.RemoteOnly, canonical(fake.matrices.RemoteOnly));
    assert.equal(plan.changes.length, 3);
  });
  await run("one atomic commit, exact payloads, old snapshots retained, repeated apply no-op", async () => {
    const fake = fixture(); const oldRelease = clone([...fake.docs].filter(([name]) => name.includes("/_release_")));
    const plan = await makePlan("test", fake.request);
    const result = await applyPlan(plan, fake.request); assert.equal(result.verifiedTables, 4); assert.equal(fake.commits(), 1);
    for (const [name, doc] of oldRelease) assert.deepEqual(fake.docs.get(name), doc);
    assert.equal(plain(fake.docs.get(`${BASE}/_index`).fields).retainedIndexField, "preserve");
    assert.equal(plain(fake.docs.get(`${BASE}/Achievement/rows/1`).fields).rewardAmount, undefined);
    assert.equal(plain(fake.docs.get(`${BASE}/Title/rows/1`).fields).eventKey, undefined);
    const again = await applyPlan(plan, fake.request); assert.equal(again.alreadyApplied, true); assert.equal(fake.commits(), 1);
    await assert.rejects(makePlan("test", fake.request), /already applied/);
  });
  await run("CAS rejects concurrent unrelated metadata change without partial writes", async () => {
    const fake = fixture(); const plan = await makePlan("test", fake.request);
    fake.docs.get(`${BASE}/RemoteOnly`).updateTime = "concurrent";
    const before = clone([...fake.docs]);
    await assert.rejects(applyPlan(plan, fake.request), /CAS/); assert.deepEqual([...fake.docs], before);
  });
  await run("tampered plan cannot commit", async () => {
    const fake = fixture(); const plan = await makePlan("test", fake.request);
    plan.writes.pop(); await assert.rejects(applyPlan(plan, fake.request), /Plan content changed/);
    assert.equal(fake.commits(), 0);
  });
  console.log(`${count} offline migration checks passed; no remote requests.`);
}
main().catch((error) => { console.error(error); process.exitCode = 1; });
