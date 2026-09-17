// Publish only the three reviewed CardPackDrop cardId changes. Never reads/writes SpecData.bytes.
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const os = require("node:os");
const crypto = require("node:crypto");
const {client, csv, canonical, hash, valueOf, unpack, checkCommit, releaseHistory} = require("./publish-local-spec");
const ROOT = path.resolve(__dirname, "../..");
const DATABASE = "projects/bm-cardbattle/databases/cardbattle/documents";
const TABLE = "CardPackDrop";
const CSV = path.join(ROOT, `docs/SpecData/${TABLE}_sheet.csv`);
const CHANGES = {1062: "StarterPack", 1367: "RangePack", 1372: "TutorialStarterGrant"};
const COLUMNS = ["id", "packId", "minGrade", "cardId", "weight"];
const TYPES = ["int", "string", "string", "int", "int"];
const fields = (doc) => Object.fromEntries(Object.entries(doc.fields || {}).map(([k, v]) => [k, unpack(v)]));
const wire = (data) => valueOf(data).mapValue.fields;
const update = (name, data, condition) => ({update: {name, fields: wire(data)}, currentDocument: condition});
const create = (name, data) => update(name, data, {exists: false});
const digest = (file) => crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex");
const sources = () => ({csv: digest(CSV), script: digest(__filename)});
function baseOf(env) {
  assert(["test", "live"].includes(env), "Explicit test/live environment required");
  return `${DATABASE}/envs/${env}/specs`;
}
function localTable() {
  const parsed = csv(fs.readFileSync(CSV, "utf8"));
  const header = parsed.findIndex((row) => row[0] === "id");
  assert(header >= 0 && header <= 1, "CSV header missing");
  const columns = parsed[header], types = parsed[header + 1];
  assert.equal(columns.length, types.length);
  assert.deepEqual(columns.slice(0, COLUMNS.length), COLUMNS);
  assert.deepEqual(types.slice(0, TYPES.length), TYPES);
  assert(columns.slice(COLUMNS.length).every((column, i) => column.startsWith("#") && types[COLUMNS.length + i] === "string"));
  const rows = parsed.slice(header + 2).filter((row) => row.some((cell) => cell.trim())).map((row) => {
    assert.equal(row.length, columns.length, "CSV row width");
    return Object.fromEntries(COLUMNS.map((column, i) => {
      if (TYPES[i] === "string") return [column, row[i]];
      assert(/^[+-]?\d+$/.test(row[i].trim()), `Invalid integer: ${column}`);
      const n = Number(row[i]);
      assert(Number.isSafeInteger(n) && n >= -2147483648 && n <= 2147483647);
      return [column, n];
    }));
  }).sort((a, b) => a.id - b.id);
  assert(rows.length > 0 && rows.every((row) => row.id > 0));
  assert.equal(new Set(rows.map((row) => row.id)).size, rows.length, "Duplicate row id");
  const matrix = [COLUMNS, ...rows.map((row) => COLUMNS.map((column) => String(row[column])))];
  const payload = canonical(matrix);
  return {rows, matrix, payload, payloadHash: hash(payload)};
}
function validateBlob(doc, pin) {
  const blob = fields(doc);
  assert.equal(hash(blob.payload), pin.payloadHash, `Blob hash: ${doc.name}`);
  assert.equal(blob.payloadHash, pin.payloadHash);
  assert.equal(blob.revision, pin.revision);
  assert.equal(JSON.parse(blob.payload).length - 1, blob.rowCount);
  return blob;
}
function buildPlan(env, backups, publishedAt, publishedBy) {
  const base = baseOf(env), local = localTable();
  const {indexDoc, metaDoc, currentDoc, publishedDoc, rowDocs} = backups;
  assert.equal(indexDoc.name, `${base}/_index`);
  assert.equal(metaDoc.name, `${base}/${TABLE}`);
  assert.equal(currentDoc.name, `${base}/${TABLE}/blob/current`);
  const index = fields(indexDoc), meta = fields(metaDoc), pin = index.tables[TABLE];
  assert(pin && pin.blobPath.startsWith(`envs/${env}/specs/_release_`));
  assert.equal(publishedDoc.name, `${DATABASE}/${pin.blobPath}`);
  const old = validateBlob(publishedDoc, pin);
  assert.deepEqual(fields(currentDoc), old, "Unpublished current blob exists");
  assert.equal(index.major, 6, "Schema migration is out of scope");
  assert.equal(meta.major ?? meta.schemaVersion, index.major);
  assert.equal(meta.schemaVersion, index.major);
  assert.equal(old.major ?? old.schemaVersion, index.major);
  assert.deepEqual(meta.columns, COLUMNS);
  assert.equal(meta.payloadHash, pin.payloadHash);
  assert.equal(meta.revision, pin.revision);
  assert.equal(meta.rowsRevision, meta.revision, "Row mirror is not current");
  assert.equal(meta.rowCount, old.rowCount);
  assert(Number.isSafeInteger(meta.revision) && meta.revision >= 0);
  const previous = JSON.parse(old.payload);
  assert.deepEqual(previous[0], COLUMNS);
  assert.equal(previous.length, local.matrix.length);
  const expected = previous.map((row) => [...row]);
  const found = [];
  for (const row of expected.slice(1)) {
    const packId = CHANGES[row[0]];
    if (!packId) continue;
    assert.equal(row[1], packId);
    assert.equal(row[3], "35", `Unexpected previous cardId for ${row[0]}`);
    row[3] = "11";
    found.push(row[0]);
  }
  assert.deepEqual(found.sort(), Object.keys(CHANGES).sort());
  assert.deepEqual(local.matrix, expected, "CSV contains changes outside the three approved cardId replacements");
  const minor = index.nextMinor, revision = meta.revision + 1;
  assert(Number.isSafeInteger(minor) && minor > index.minor);
  assert(Number.isSafeInteger(revision));
  const version = `${index.major}.${minor}`;
  const blobPath = `envs/${env}/specs/_release_${index.major}_${minor}_${TABLE}`;
  const blob = {...old, revision, rowCount: local.rows.length, payload: local.payload, payloadHash: local.payloadHash};
  const tables = {...index.tables, [TABLE]: {revision, payloadHash: local.payloadHash, blobPath}};
  const release = {major: index.major, minor, minAppMajor: index.minAppMajor, contentVersion: version,
    noticeTitle: index.noticeTitle || "", noticeBody: index.noticeBody || ""};
  assert.deepEqual(Object.keys(rowDocs).sort(), Object.keys(CHANGES).sort());
  const writes = Object.keys(CHANGES).map((id) => {
    const doc = rowDocs[id], row = local.rows.find((entry) => entry.id === Number(id));
    assert.equal(doc.name, `${base}/${TABLE}/rows/${id}`);
    assert.deepEqual(fields(doc), {...row, cardId: 35}, `Row mirror mismatch: ${id}`);
    return update(doc.name, row, {updateTime: doc.updateTime});
  });
  const metaWrite = update(metaDoc.name, {...meta, revision, rowsRevision: revision,
    rowCount: local.rows.length, payloadHash: local.payloadHash, uploadedBy: publishedBy}, {updateTime: metaDoc.updateTime});
  metaWrite.update.fields.updatedAt = {timestampValue: publishedAt};
  writes.push(metaWrite, update(currentDoc.name, blob, {updateTime: currentDoc.updateTime}),
    create(`${DATABASE}/${blobPath}`, blob),
    create(`${base}/_release_index_${index.major}_${minor}`, {...release, publishedAt, publishedBy,
      tablesJson: JSON.stringify(valueOf(tables))}),
    update(indexDoc.name, {...index, ...release, nextMinor: minor + 1,
      history: releaseHistory(index.history || [], version), tables}, {updateTime: indexDoc.updateTime}));
  assert(writes.every((write) => write.currentDocument.exists === false || typeof write.currentDocument.updateTime === "string"));
  checkCommit(writes);
  return {kind: "starter-woodhorn-three-rows-v1", project: "bm-cardbattle", env,
    previousVersion: index.contentVersion, version, publishedAt, publishedBy,
    sources: sources(), backups, expectedTables: tables, writes};
}
async function readPins(request, env, tables) {
  await Promise.all(Object.entries(tables).map(async ([name, pin]) => {
    assert(pin.blobPath.startsWith(`envs/${env}/specs/_release_`), `Unsafe pin: ${name}`);
    validateBlob(await request(`${DATABASE}/${pin.blobPath}`), pin);
  }));
}
async function makePlan(env, request) {
  const base = baseOf(env), indexDoc = await request(`${base}/_index`);
  const index = fields(indexDoc);
  await readPins(request, env, index.tables);
  const [metaDoc, currentDoc, publishedDoc, rowEntries] = await Promise.all([
    request(`${base}/${TABLE}`), request(`${base}/${TABLE}/blob/current`),
    request(`${DATABASE}/${index.tables[TABLE].blobPath}`),
    Promise.all(Object.keys(CHANGES).map(async (id) => [id, await request(`${base}/${TABLE}/rows/${id}`)])),
  ]);
  return buildPlan(env, {indexDoc, metaDoc, currentDoc, publishedDoc, rowDocs: Object.fromEntries(rowEntries)},
    new Date().toISOString(), os.userInfo().username);
}
async function applyPlan(plan, request) {
  assert.equal(plan.kind, "starter-woodhorn-three-rows-v1");
  assert.deepEqual(plan.sources, sources(), "Plan source changed; create a new plan");
  assert.deepEqual(plan, buildPlan(plan.env, plan.backups, plan.publishedAt, plan.publishedBy), "Plan differs from constrained generator");
  // One atomic commit; an ambiguous failure must be inspected, never automatically retried.
  await request(`${DATABASE}:commit`, {writes: plan.writes});
  const base = baseOf(plan.env), index = fields(await request(`${base}/_index`));
  const expectedIndex = fields(plan.writes[plan.writes.length - 1].update);
  assert.deepEqual(index, expectedIndex);
  await readPins(request, plan.env, index.tables);
  for (const write of plan.writes.slice(0, -1)) {
    const actual = fields(await request(write.update.name));
    assert.deepEqual(actual, fields(write.update), `Readback mismatch: ${write.update.name}`);
  }
  return {env: plan.env, published: plan.version, changedRows: Object.keys(CHANGES).map(Number),
    preservedPins: Object.keys(index.tables).length - 1};
}
async function main() {
  const [mode, arg, output] = process.argv.slice(2);
  if (mode === "plan") {
    assert(output, "Plan output path required");
    const plan = await makePlan(arg, await client());
    fs.writeFileSync(output, JSON.stringify(plan, null, 2), {flag: "wx"});
    console.log(JSON.stringify({env: plan.env, from: plan.previousVersion, to: plan.version,
      writes: plan.writes.length, changedRows: Object.keys(CHANGES).map(Number), plan: path.resolve(output)}, null, 2));
  } else if (mode === "apply") {
    assert(arg, "Reviewed plan path required");
    console.log(JSON.stringify(await applyPlan(JSON.parse(fs.readFileSync(arg, "utf8")), await client())));
  } else throw new Error("Use: plan <test|live> <new-plan.json> | apply <reviewed-plan.json>");
}
module.exports = {localTable, buildPlan, makePlan, applyPlan};
if (require.main === module) main().catch((error) => {console.error(error.message); process.exitCode = 1;});
