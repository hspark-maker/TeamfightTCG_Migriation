// Offline regression checks only. No credentials, Firestore, CSV or binary writes.
const assert = require("node:assert/strict");
const fs = require("node:fs");
const {isDeepStrictEqual} = require("node:util");
const publisher = require("./publish-all-csv-spec");
const {canonical, hash, valueOf, unpack, checkCommit} = require("./publish-local-spec");
const DB = "projects/bm-cardbattle/databases/cardbattle/documents";
const BASE = `${DB}/envs/test/specs`;
const fieldsOf = (obj) => Object.fromEntries(Object.entries(obj).map(([key, value]) => [key, valueOf(value)]));
const clone = (obj) => structuredClone(obj);
const dataOf = (doc) => Object.fromEntries(Object.entries(doc.fields).map(([key, value]) => [key, unpack(value)]));
let assertions = 0;
function check(value, message) { assert(value, message); assertions++; }

function fixture(local, {change = true, missingAchievement = true, missingCardCraft = false, orphan = false} = {}) {
  const docs = new Map(); let clock = 0; let commits = 0;
  const put = (name, value) => docs.set(name, {name, fields: fieldsOf(value), updateTime: `2026-09-21T00:00:${String(++clock).padStart(6, "0")}.000000Z`});
  const tables = {};
  for (const name of local.names) {
    if (name === "Achievement" && missingAchievement) continue;
    if (name === "CardCraft" && missingCardCraft) continue;
    const source = local.tables[name];
    const rows = clone(source.rows);
    if (name === "LoadingTip" && change) {
      rows[0].text = "Previous tip";
      rows.push({id: 999999, text: "Removed tip", enabled: 1});
    }
    const matrix = [source.columns, ...rows.map((row) => source.columns.map((key) => String(row[key])))];
    const payload = canonical(matrix); const payloadHash = hash(payload); const revision = 7;
    const blob = {schemaVersion: local.major, major: local.major, revision, rowCount: rows.length, payloadHash, payload};
    put(`${BASE}/${name}`, {table: name, schemaVersion: local.major, major: local.major, revision,
      rowsRevision: revision, rowCount: rows.length, columns: source.columns, idColumn: "id", payloadHash});
    put(`${BASE}/${name}/blob/current`, blob);
    const blobPath = `envs/test/specs/_release_${local.major}_58_${name}`;
    put(`${DB}/${blobPath}`, blob);
    tables[name] = {revision, payloadHash, blobPath};
    // Firestore REST may return map keys in any order. This must never inflate row writes.
    for (const row of rows) put(`${BASE}/${name}/rows/${row.id}`, Object.fromEntries(Object.entries(row).reverse()));
  }
  put(`${BASE}/_index`, {major: local.major, minor: 58, nextMinor: 59,
    contentVersion: `${local.major}.58`, history: [`${local.major}.58`], tables});
  put(`${BASE}/_release_index_${local.major}_58`, {major: local.major, minor: 58});
  if (orphan) put(`${BASE}/Achievement/rows/1`, {id: 1});
  const request = async (resource, body) => {
    assert(resource.startsWith(BASE) || resource === `${DB}:commit`, "Request escaped fake test environment");
    if (body) {
      assert.equal(resource, `${DB}:commit`); commits++;
      // Validate every precondition before changing any document, like one atomic commit.
      for (const write of body.writes) {
        const name = write.update?.name || write.delete;
        const prior = docs.get(name); const condition = write.currentDocument;
        assert(condition, "Every write needs CAS");
        if (condition.exists === false) assert(!prior, `CAS already exists: ${name}`);
        else assert.equal(prior?.updateTime, condition.updateTime, `CAS stale: ${name}`);
      }
      for (const write of body.writes) {
        if (write.delete) { docs.delete(write.delete); continue; }
        const fields = write.updateMask ? {...docs.get(write.update.name).fields, ...write.update.fields} : write.update.fields;
        docs.set(write.update.name, {name: write.update.name, fields: clone(fields), updateTime: `after-${++clock}`});
      }
      return {};
    }
    if (resource.includes("/rows?")) {
      const [base, query] = resource.split("?");
      const page = Number(new URLSearchParams(query).get("pageToken") || 0);
      const all = [...docs.values()].filter((doc) => doc.name.startsWith(`${base}/`));
      const documents = all.slice(page, page + 100);
      return {documents: clone(documents), ...(page + 100 < all.length ? {nextPageToken: String(page + 100)} : {})};
    }
    if (!docs.has(resource)) throw Object.assign(new Error(`Firestore 404: ${resource}`), {status: 404});
    return clone(docs.get(resource));
  };
  return {request, docs, get commits() { return commits; }};
}

async function main() {
  // A direct CSV publisher must not touch the generated binary even while fingerprinting inputs.
  const originalRead = fs.readFileSync;
  fs.readFileSync = function(file, ...args) {
    assert(!String(file).endsWith("SpecData.bytes"), "Publisher read the generated binary");
    return originalRead.call(this, file, ...args);
  };
  try {
    const local = publisher.localSnapshot();
    check(local.names.length === 26 && local.names.includes("LoadingTip") && local.names.includes("Achievement") &&
      local.names.includes("CardCraft"), "26 published CSVs required");
    check(local.tables.Mission.columns.includes("accountExp") && local.tables.Achievement.rows.length === 61,
      "CSV-only DTO schema mismatch");
    const schema = [["int", "id"], ["string", "text"], ["long", "amount"]];
    check(isDeepStrictEqual(publisher.typedRows("Fixture", schema,
      'id,amount,text,#memo\nint,long,string,string\n2,3,"A,B",note\n1,4,"line\nnext",note\n'),
    [{id: 1, text: "line\nnext", amount: 4}, {id: 2, text: "A,B", amount: 3}]), "CSV header mapping/order/quoted fields");
    for (const [label, csv] of [
      ["duplicate", "id,text,amount\nint,string,long\n1,a,1\n1,b,2\n"],
      ["int range", "id,text,amount\nint,string,long\n2147483648,a,1\n"],
      ["unsafe long", "id,text,amount\nint,string,long\n1,a,9007199254740992\n"],
      ["type", "id,text,amount\nint,string,int\n1,a,1\n"],
      ["unknown column", "id,text,amount,extra\nint,string,long,string\n1,a,1,x\n"],
      ["width", "id,text,amount\nint,string,long\n1,a\n"],
      ["quote", "id,text,amount\nint,string,long\n1,\"a,1\n"],
    ]) { assert.throws(() => publisher.typedRows("Fixture", schema, csv), undefined, label); assertions++; }
    const fake = fixture(local);
    const plan = await publisher.makePlan("test", fake.request);
    check(fake.commits === 0, "Plan must be read-only");
    check(plan.changes.length === 2 && plan.changes.some((row) => row.table === "Achievement"), "Changed + new tables");
    const rowWrites = plan.writes.filter((write) => (write.update?.name || write.delete).includes("/rows/"));
    check(rowWrites.length === 63, "Reordered field maps caused redundant row writes");
    check(plan.writes.filter((write) => write.update?.name.includes("/Achievement/") || write.update?.name.endsWith("/Achievement"))
      .every((write) => write.currentDocument.exists === false), "New table CAS must require absence");
    check(plan.backups.some((doc) => doc.name.endsWith("/LoadingTip/rows/999999")) &&
      plan.backups.some((doc) => doc.name.endsWith("_release_index_6_58")), "Previous rows/release backups missing");
    check(plan.writes.length <= 500 && Object.keys(plan.expectedTables).length === local.names.length, "One complete atomic release");
    const result = await publisher.applyPlan(plan, fake.request);
    check(fake.commits === 1 && result.verifiedTables === local.names.length, "Apply must issue one commit and verify every release blob");
    check(!fake.docs.has(`${BASE}/LoadingTip/rows/999999`), "Removed row survived");
    for (const name of local.names) for (const row of local.tables[name].rows)
      assert.deepEqual(dataOf(fake.docs.get(`${BASE}/${name}/rows/${row.id}`)), row);
    assertions++;

    const noChange = fixture(local, {change: false, missingAchievement: false});
    await assert.rejects(publisher.makePlan("test", noChange.request), /No unpublished table change/); assertions++;
    const staged = fixture(local, {change: false, missingAchievement: false});
    delete staged.docs.get(`${BASE}/_index`).fields.tables.mapValue.fields.Achievement;
    const stagedPlan = await publisher.makePlan("test", staged.request);
    check(stagedPlan.changes.length === 1 && stagedPlan.changes[0].newIncluded === true &&
      stagedPlan.changes[0].table === "Achievement", "Staged Achievement must become a newly published table");
    check(stagedPlan.writes.every((write) => !(write.update?.name || write.delete).includes("/rows/")),
      "Identical staged rows must not be rewritten");
    const stagedResult = await publisher.applyPlan(stagedPlan, staged.request);
    check(stagedResult.verifiedTables === local.names.length && staged.commits === 1, "Staged publish must remain one atomic commit");
    const crafting = fixture(local, {change: false, missingAchievement: false, missingCardCraft: true});
    const craftingPlan = await publisher.makePlan("test", crafting.request);
    check(craftingPlan.changes.length === 1 && craftingPlan.changes[0].table === "CardCraft",
      "Existing release must allow adding the server-only crafting table");
    check(craftingPlan.writes.filter((write) => write.update?.name.startsWith(`${BASE}/CardCraft`))
      .every((write) => write.currentDocument.exists === false), "Crafting table creation must require absence");
    await publisher.applyPlan(craftingPlan, crafting.request);
    check(isDeepStrictEqual(dataOf(crafting.docs.get(`${BASE}/CardCraft`)).columns, ["id", "grade", "cost", "enabled"]),
      "Crafting CSV comment column must not enter the published schema");
    for (const row of local.tables.CardCraft.rows)
      assert.deepEqual(dataOf(crafting.docs.get(`${BASE}/CardCraft/rows/${row.id}`)), row);
    assertions++;
    await assert.rejects(publisher.makePlan("live", fake.request), /only supports test/); assertions++;
    await assert.rejects(publisher.makePlan("test", fixture(local, {orphan: true}).request), /orphan rows/); assertions++;

    const mismatch = fixture(local);
    mismatch.docs.get(`${BASE}/Card`).fields.revision = valueOf(8);
    await assert.rejects(publisher.makePlan("test", mismatch.request), /differs from index pin/); assertions++;
    const blobMismatch = fixture(local);
    blobMismatch.docs.get(`${BASE}/Card/blob/current`).fields.payload = valueOf("[]");
    await assert.rejects(publisher.makePlan("test", blobMismatch.request), /blob payload hash/); assertions++;
    const schemaMismatch = fixture(local);
    schemaMismatch.docs.get(`${BASE}/Card`).fields.columns = valueOf(["id"]);
    await assert.rejects(publisher.makePlan("test", schemaMismatch.request), /column contract changed/); assertions++;

    const stale = fixture(local); const stalePlan = await publisher.makePlan("test", stale.request);
    stale.docs.get(`${BASE}/_index`).updateTime = "concurrent-update";
    const prior = clone([...stale.docs]);
    await assert.rejects(publisher.applyPlan(stalePlan, stale.request), /CAS stale/); assertions++;
    check(isDeepStrictEqual([...stale.docs], prior), "CAS failure partially modified fake Firestore");
    const sourceChanged = clone(stalePlan); sourceChanged.sources[Object.keys(sourceChanged.sources)[0]] = "changed";
    const commits = stale.commits;
    await assert.rejects(publisher.applyPlan(sourceChanged, stale.request), /Local inputs changed/); assertions++;
    check(stale.commits === commits, "Changed source reached commit");
    const escaped = clone(stalePlan); escaped.writes[0].update.name = `${DB}/envs/live/specs/Card`;
    await assert.rejects(publisher.applyPlan(escaped, stale.request), /escaped test/); assertions++;
    assert.throws(() => checkCommit(Array.from({length: 501}, (_, i) => ({delete: `${BASE}/${i}`}))), /500 writes/); assertions++;
    console.log(`PASS: ${assertions} offline assertions; ${local.names.length} CSVs; ${plan.writes.length} atomic fake writes; no binary or remote access.`);
  } finally { fs.readFileSync = originalRead; }
}
main().catch((error) => { console.error(error); process.exitCode = 1; });
