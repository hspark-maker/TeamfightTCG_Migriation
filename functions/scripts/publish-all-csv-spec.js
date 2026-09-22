// Publish the current CSV sources directly, without reading or generating SpecData.bytes.
// Test environment only. Plan is read-only; apply commits the exact plan once with CAS.
// node scripts/publish-all-csv-spec.js plan test <plan.json>
// node scripts/publish-all-csv-spec.js apply <plan.json>
const fs = require("node:fs");
const path = require("node:path");
const os = require("node:os");
const crypto = require("node:crypto");
const assert = require("node:assert/strict");
const {isDeepStrictEqual} = require("node:util");
const shared = require("./publish-local-spec");
const {csv, canonical, hash, valueOf, unpack, checkCommit, releaseHistory, client} = shared;

const ROOT = path.resolve(__dirname, "../..");
const PROJECT = "bm-cardbattle";
const DATABASE = `projects/${PROJECT}/databases/cardbattle/documents`;
const CODEC = "Assets/Scripts/OutGame/Spec/SpecPayloadCodec.cs";
const GENERATED = "Assets/Table/SpecDatas.cs";
const ACCOUNT = "Assets/Scripts/Editor/SpecFirestoreUploader.AccountCsv.cs";
const RANK = "Assets/Scripts/OutGame/Spec/RankAiEncounterRow.cs";
const VERSION = "Assets/Scripts/OutGame/Spec/ContentVersion.cs";
const SETTINGS = "ProjectSettings/ProjectSettings.asset";
const read = (file) => fs.readFileSync(path.join(ROOT, file), "utf8");
const fieldsOf = (value) => Object.fromEntries(Object.entries(value).map(([k, v]) => [k, valueOf(v)]));
const unpackFields = (fields) => Object.fromEntries(Object.entries(fields || {}).map(([k, v]) => [k, unpack(v)]));
const digest = (bytes) => crypto.createHash("sha256").update(bytes).digest("hex");
const update = (name, fields, condition) => ({update: {name, fields}, currentDocument: condition});
const blobFields = (local, table, revision) => fieldsOf({schemaVersion: local.major, major: local.major,
  revision, rowCount: table.rows.length, payloadHash: table.payloadHash, payload: table.payload});

function publishedNames(text = read(CODEC)) {
  const names = ["TableNames", "OptionalTableNames", "ServerOnlyTableNames"].flatMap((name) => {
    const match = text.match(new RegExp(`\\b${name}\\s*=\\s*\\{([\\s\\S]*?)\\}`));
    assert(match, `Missing published list: ${name}`);
    return [...match[1].matchAll(/"(\w+)"/g)].map((m) => m[1]);
  });
  assert.equal(new Set(names).size, names.length, "Duplicate published table");
  return names;
}

function snapshotSources(names = publishedNames()) {
  const sources = [CODEC, GENERATED, ACCOUNT, RANK, VERSION, SETTINGS,
    "functions/scripts/publish-local-spec.js", "functions/scripts/publish-all-csv-spec.js",
    ...names.map((name) => `docs/SpecData/${name}_sheet.csv`)];
  return Object.fromEntries(sources.map((file) => [file, digest(fs.readFileSync(path.join(ROOT, file)))]));
}

function typedRows(name, fields, text) {
  const parsed = csv(text);
  const header = parsed.findIndex((row) => row[0] === "id");
  assert(header >= 0 && header <= 1 && parsed.length > header + 2, `CSV header/data: ${name}`);
  const columns = parsed[header];
  const types = parsed[header + 1];
  assert.equal(new Set(columns).size, columns.length, `Duplicate CSV column: ${name}`);
  assert.equal(columns.length, types.length, `CSV type width: ${name}`);
  const schema = new Map(fields.map(([type, key]) => [key, type]));
  assert(fields.length && fields[0][0] === "int" && fields[0][1] === "id", `Invalid schema: ${name}`);
  for (let i = 0; i < columns.length; i++) {
    assert(schema.has(columns[i]) || columns[i].startsWith("#"), `Unknown CSV column: ${name}.${columns[i]}`);
    assert.equal(types[i], schema.get(columns[i]) || "string", `CSV type: ${name}.${columns[i]}`);
  }
  const indices = fields.map(([, key]) => {
    const index = columns.indexOf(key);
    assert(index >= 0, `Missing CSV column: ${name}.${key}`);
    return index;
  });
  const ids = new Set();
  const rows = parsed.slice(header + 2).filter((row) => row.some((cell) => cell.trim())).map((row) => {
    assert.equal(row.length, columns.length, `CSV row width: ${name}`);
    const result = Object.fromEntries(fields.map(([type, key], i) => {
      const cell = row[indices[i]];
      if (type === "string") return [key, cell];
      assert(["int", "long"].includes(type) && /^[+-]?\d+$/.test(cell.trim()), `CSV integer: ${name}.${key}`);
      const number = Number(cell);
      assert(Number.isSafeInteger(number) && (type === "long" ||
        (number >= -2147483648 && number <= 2147483647)), `CSV integer range: ${name}.${key}`);
      return [key, number];
    }));
    assert(result.id > 0 && !ids.has(result.id), `Invalid/duplicate ID: ${name}.${result.id}`);
    ids.add(result.id);
    return result;
  });
  assert(rows.length, `Empty table: ${name}`);
  return rows.sort((a, b) => a.id - b.id);
}

function localSnapshot() {
  const names = publishedNames();
  const classes = new Map([...read(GENERATED).matchAll(/public partial class (\w+)\s*\{([\s\S]*?)^\}/gm)]
    .map((m) => [m[1], m[2]]));
  const account = read(ACCOUNT);
  for (const name of ["Mission", "Achievement", "CardCraft"])
    classes.set(name, account.match(new RegExp(`sealed class ${name}UploadRow\\s*\\{([\\s\\S]*?)\\n    \\}`))?.[1]);
  classes.set("RankAiEncounter", read(RANK));
  const version = read(VERSION);
  const major = Number(version.match(/const int Major\s*=\s*(\d+)/)?.[1]);
  const minAppMajor = Number(version.match(/const int MinAppMajor\s*=\s*(\d+)/)?.[1]);
  assert(Number.isSafeInteger(major) && Number.isSafeInteger(minAppMajor));
  const tables = {};
  for (const name of names) {
    assert(classes.get(name), `Missing row schema: ${name}`);
    const fields = [...classes.get(name).matchAll(/^\s*public (int|long|string) (\w+);/gm)].map((m) => [m[1], m[2]]);
    const rows = typedRows(name, fields, read(`docs/SpecData/${name}_sheet.csv`));
    const columns = fields.map(([, key]) => key);
    const matrix = [columns, ...rows.map((row) => fields.map(([type, key]) => type === "string" ? row[key] : String(row[key])))];
    const payload = canonical(matrix);
    assert(Buffer.byteLength(payload) <= 1048576 - 89, `Oversized payload: ${name}`);
    for (const row of rows) assert(Buffer.byteLength(JSON.stringify(fieldsOf(row))) <= 900 * 1024, `Oversized row: ${name}`);
    tables[name] = {columns, rows, matrix, payload, payloadHash: hash(payload)};
  }
  return {names, major, minAppMajor, tables};
}

async function maybeGet(request, name) {
  try { return await request(name); } catch (error) {
    if (error.status === 404 || /^Firestore 404:/.test(error.message)) return null;
    throw error;
  }
}
async function listRows(request, name) {
  const rows = []; let token = "";
  do {
    const page = await request(`${name}/rows?pageSize=1000${token ? "&pageToken=" + encodeURIComponent(token) : ""}`);
    rows.push(...(page.documents || []));
    token = page.nextPageToken || "";
  } while (token);
  return rows;
}
function verifyBlob(doc, table, revision, payloadHash) {
  assert(doc, "Missing published blob");
  const blob = unpackFields(doc.fields);
  assert.equal(blob.payloadHash, payloadHash, `${table}: blob metadata hash`);
  assert.equal(hash(blob.payload), payloadHash, `${table}: blob payload hash`);
  assert.equal(blob.revision, revision, `${table}: blob revision`);
  const matrix = JSON.parse(blob.payload);
  assert.equal(matrix.length - 1, blob.rowCount, `${table}: blob row count`);
  return {blob, matrix};
}

async function makePlan(env, request) {
  assert.equal(env, "test", "This publisher only supports test");
  const sources = snapshotSources();
  const local = localSnapshot();
  const base = `${DATABASE}/envs/test/specs`;
  const indexDoc = await request(`${base}/_index`);
  const index = unpackFields(indexDoc.fields);
  assert.equal(index.major, local.major, "Schema migration requires official release workflow");
  assert(Object.keys(index.tables).every((name) => local.names.includes(name)), "Unknown table in current index");
  const minor = index.nextMinor;
  assert(Number.isSafeInteger(minor) && minor > index.minor);
  const version = `${local.major}.${minor}`;
  const writes = []; const tableEntries = {}; const changes = []; const backups = [indexDoc];
  const oldReleaseIndex = await maybeGet(request, `${base}/_release_index_${index.major}_${index.minor}`);
  if (oldReleaseIndex) backups.push(oldReleaseIndex);
  for (const name of local.names) {
    const table = local.tables[name];
    const tablePath = `${base}/${name}`;
    const metaDoc = await maybeGet(request, tablePath);
    const oldBlobDoc = await maybeGet(request, `${tablePath}/blob/current`);
    const pin = index.tables[name];
    let revision = 0; let meta;
    if (metaDoc) {
      meta = unpackFields(metaDoc.fields);
      assert.equal(meta.major ?? meta.schemaVersion, local.major, `${name}: major mismatch`);
      assert.equal(meta.schemaVersion, local.major, `${name}: schema mismatch`);
      assert.deepEqual(meta.columns, table.columns, `${name}: formal column contract changed`);
      if (pin) {
        assert.equal(meta.revision, pin.revision, `${name}: metadata revision differs from index pin`);
        assert.equal(meta.payloadHash, pin.payloadHash, `${name}: metadata hash differs from index pin`);
      } else {
        // An editor may already have staged a new table without publishing an index pin.
        // Include it only when its exact staged payload matches today's CSV sources.
        assert.equal(meta.payloadHash, table.payloadHash, `${name}: unpublished staged table differs from CSV`);
      }
      assert(Number.isSafeInteger(meta.revision) && meta.revision >= 0);
      revision = meta.revision;
      const old = verifyBlob(oldBlobDoc, name, revision, meta.payloadHash);
      assert.deepEqual(old.matrix[0], table.columns, `${name}: blob column contract changed`);
      assert.equal(old.blob.rowCount, meta.rowCount, `${name}: metadata row count differs from blob`);
      backups.push(metaDoc, oldBlobDoc);
      if (pin) {
        assert(typeof pin.blobPath === "string" && pin.blobPath.startsWith("envs/test/specs/_release_"), `${name}: invalid release pin`);
        const releaseDoc = await request(`${DATABASE}/${pin.blobPath}`);
        const release = verifyBlob(releaseDoc, name, revision, meta.payloadHash);
        assert.equal(release.blob.payload, old.blob.payload, `${name}: current blob differs from release pin`);
        backups.push(releaseDoc);
      }
    } else {
      assert(!pin && !oldBlobDoc, `${name}: missing metadata has existing published state`);
    }
    if (meta && !pin && meta.rowsRevision === revision)
      changes.push({table: name, newIncluded: true, oldRows: meta.rowCount, rows: table.rows.length,
        revision, payloadHash: table.payloadHash});
    if (!meta || meta.payloadHash !== table.payloadHash || meta.rowsRevision !== revision) {
      const previousRows = await listRows(request, tablePath);
      assert(meta || previousRows.length === 0, `${name}: orphan rows exist for a new table`);
      const previous = new Map(previousRows.map((doc) => [doc.name.split("/").pop(), doc]));
      backups.push(...previousRows);
      revision++;
      assert(Number.isSafeInteger(revision));
      for (const row of table.rows) {
        const id = String(row.id); const old = previous.get(id); previous.delete(id);
        if (!old || !isDeepStrictEqual(unpackFields(old.fields), row))
          writes.push(update(`${tablePath}/rows/${id}`, fieldsOf(row), old ? {updateTime: old.updateTime} : {exists: false}));
      }
      for (const old of previous.values()) writes.push({delete: old.name, currentDocument: {updateTime: old.updateTime}});
      const metadata = fieldsOf({table: name, schemaVersion: local.major, major: local.major, revision,
        rowsRevision: revision, rowCount: table.rows.length, columns: table.columns, idColumn: "id",
        payloadHash: table.payloadHash, uploadedBy: os.userInfo().username,
        appVersion: read(SETTINGS).match(/^\s*bundleVersion:\s*(.+)$/m)[1].trim()});
      metadata.updatedAt = {timestampValue: new Date().toISOString()};
      writes.push(update(tablePath, metadata, metaDoc ? {updateTime: metaDoc.updateTime} : {exists: false}));
      writes.push(update(`${tablePath}/blob/current`, blobFields(local, table, revision),
        oldBlobDoc ? {updateTime: oldBlobDoc.updateTime} : {exists: false}));
      changes.push({table: name, oldRows: meta?.rowCount ?? 0, rows: table.rows.length, revision, payloadHash: table.payloadHash});
    } else {
      writes.push({...update(tablePath, {payloadHash: valueOf(meta.payloadHash)}, {updateTime: metaDoc.updateTime}),
        updateMask: {fieldPaths: ["payloadHash"]}});
      // Also guard the current blob against an independent edit after planning.
      writes.push({...update(`${tablePath}/blob/current`, {payloadHash: valueOf(meta.payloadHash)}, {updateTime: oldBlobDoc.updateTime}),
        updateMask: {fieldPaths: ["payloadHash"]}});
    }
    const blobPath = `envs/test/specs/_release_${local.major}_${minor}_${name}`;
    writes.push(update(`${DATABASE}/${blobPath}`, blobFields(local, table, revision), {exists: false}));
    tableEntries[name] = {revision, payloadHash: table.payloadHash, blobPath};
  }
  assert(changes.length, "No unpublished table change; refusing an empty release");
  const release = {major: local.major, minor, minAppMajor: local.minAppMajor, contentVersion: version,
    noticeTitle: "테스트 스펙 업데이트", noticeBody: "현재 CSV 스펙 데이터를 반영했습니다."};
  writes.push(update(`${base}/_release_index_${local.major}_${minor}`, fieldsOf({...release,
    publishedAt: new Date().toISOString(), publishedBy: os.userInfo().username,
    tablesJson: JSON.stringify(valueOf(tableEntries))}), {exists: false}));
  writes.push(update(`${base}/_index`, fieldsOf({...release, nextMinor: minor + 1,
    history: releaseHistory(index.history || [], version), tables: tableEntries}), {updateTime: indexDoc.updateTime}));
  checkCommit(writes);
  assert.deepEqual(snapshotSources(local.names), sources, "Local inputs changed during planning");
  return {format: "all-csv-test-v1", project: PROJECT, env, previousVersion: index.contentVersion,
    version, changes, sources, writes, backups, expectedTables: tableEntries};
}

async function applyPlan(plan, request) {
  assert.equal(plan.format, "all-csv-test-v1");
  assert.equal(plan.project, PROJECT);
  assert.equal(plan.env, "test", "This publisher only supports test");
  assert.deepEqual(snapshotSources(), plan.sources, "Local inputs changed since dry-run");
  const local = localSnapshot();
  assert.deepEqual(Object.keys(plan.expectedTables), local.names);
  for (const name of local.names) assert.equal(plan.expectedTables[name].payloadHash, local.tables[name].payloadHash);
  checkCommit(plan.writes);
  const prefix = `${DATABASE}/envs/test/specs/`;
  for (const write of plan.writes) {
    assert((write.update?.name || write.delete).startsWith(prefix), "Write escaped test specs");
    assert(write.currentDocument && (write.currentDocument.updateTime || write.currentDocument.exists === false), "Missing CAS");
  }
  // Exactly one atomic commit. Do not retry an ambiguous result; inspect the index first.
  await request(`${DATABASE}:commit`, {writes: plan.writes});
  const index = unpackFields((await request(`${prefix}_index`)).fields);
  assert.equal(index.contentVersion, plan.version);
  assert.deepEqual(index.tables, plan.expectedTables);
  for (const name of local.names) {
    const doc = await request(`${DATABASE}/${index.tables[name].blobPath}`);
    const {blob} = verifyBlob(doc, name, index.tables[name].revision, index.tables[name].payloadHash);
    assert.equal(blob.payload, local.tables[name].payload);
    assert.equal(blob.rowCount, local.tables[name].rows.length);
  }
  return {published: plan.version, env: plan.env, verifiedTables: local.names.length};
}

async function main() {
  const [mode, arg, output] = process.argv.slice(2);
  if (mode === "snapshot") {
    const local = localSnapshot();
    console.log(JSON.stringify({major: local.major, tables: local.names.map((name) => ({name,
      rows: local.tables[name].rows.length, payloadHash: local.tables[name].payloadHash}))}, null, 2));
  } else if (mode === "plan") {
    assert(output, "Plan output path required");
    const plan = await makePlan(arg, await client());
    fs.writeFileSync(output, JSON.stringify(plan, null, 2), {flag: "wx"});
    console.log(JSON.stringify({env: plan.env, from: plan.previousVersion, to: plan.version,
      changes: plan.changes, writes: plan.writes.length, plan: output}));
  } else if (mode === "apply") {
    const plan = JSON.parse(fs.readFileSync(arg, "utf8"));
    console.log(JSON.stringify(await applyPlan(plan, await client())));
  } else throw new Error("Use: snapshot | plan test <plan.json> | apply <plan.json>");
}
module.exports = {publishedNames, typedRows, snapshotSources, localSnapshot, makePlan, applyPlan};
if (require.main === module) main().catch((error) => { console.error(error.message); process.exitCode = 1; });
