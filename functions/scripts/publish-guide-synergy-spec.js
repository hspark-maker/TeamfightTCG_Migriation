// Publish the four reviewed guide-synergy CSV rows; preserve all other published tables.
// Never imports, reads, or writes SpecData.bytes. Plans include backups and CAS preconditions.
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const os = require("node:os");
const crypto = require("node:crypto");
const {client, csv, canonical, hash, unpack, valueOf, checkCommit, releaseHistory} = require("./publish-local-spec");
const ROOT = path.resolve(__dirname, "../..");
const DATABASE = "projects/bm-cardbattle/databases/cardbattle/documents";
const TABLES = ["Mission", "Reward", "CardPackDrop"];
const CHANGED = {Mission: [21, 22], Reward: [268], CardPackDrop: [1409]};
const fields = (doc) => Object.fromEntries(Object.entries(doc.fields || {}).map(([k, v]) => [k, unpack(v)]));
const update = (name, data, condition) => ({update: {name, fields: valueOf(data).mapValue.fields}, currentDocument: condition});
const create = (name, data) => update(name, data, {exists: false});
const sources = () => Object.fromEntries([__filename, ...TABLES.map((t) => path.join(ROOT, `docs/SpecData/${t}_sheet.csv`))]
  .map((file) => [path.relative(ROOT, file), crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex")]));
function baseOf(env) {
  assert(["test", "live"].includes(env));
  return `${DATABASE}/envs/${env}/specs`;
}
function localTable(name, columns) {
  const data = csv(fs.readFileSync(path.join(ROOT, `docs/SpecData/${name}_sheet.csv`), "utf8"));
  const h = data.findIndex((r) => r[0] === "id");
  assert(h === 0 || h === 1);
  const headers = data[h], types = data[h + 1];
  assert.deepEqual([...headers.filter((c) => !c.startsWith("#"))].sort(), [...columns].sort());
  const rows = data.slice(h + 2).filter((r) => r.some((c) => c.trim())).map((r) => {
    assert.equal(r.length, headers.length);
    return Object.fromEntries(columns.map((column) => {
      const i = headers.indexOf(column), type = types[i], cell = r[i];
      if (type === "string") return [column, cell];
      assert(["int", "long"].includes(type) && /^[+-]?\d+$/.test(cell));
      const n = Number(cell);
      assert(Number.isSafeInteger(n));
      if (type === "int") assert(n >= -2147483648 && n <= 2147483647);
      return [column, n];
    }));
  }).sort((a, b) => a.id - b.id);
  assert(rows.every((r) => r.id > 0));
  assert.equal(new Set(rows.map((r) => r.id)).size, rows.length);
  const matrix = [columns, ...rows.map((r) => columns.map((c) => String(r[c])))];
  const payload = canonical(matrix);
  return {rows, matrix, payload, payloadHash: hash(payload)};
}
function validateBlob(doc, pin) {
  const b = fields(doc);
  assert.equal(hash(b.payload), pin.payloadHash);
  assert.equal(b.payloadHash, pin.payloadHash);
  assert.equal(b.revision, pin.revision);
  assert.equal(JSON.parse(b.payload).length - 1, b.rowCount);
  return b;
}
function validateDelta(name, previous, local) {
  const columns = previous[0];
  const old = new Map(previous.slice(1).map((r) => [Number(r[0]), r]));
  const changed = [];
  for (const row of local.rows) {
    const before = old.get(row.id), after = columns.map((c) => String(row[c]));
    old.delete(row.id);
    if (JSON.stringify(before) === JSON.stringify(after)) continue;
    changed.push(row.id);
    if (name === "Mission") {
      assert(before && [21, 22].includes(row.id));
      const allowed = row.id === 21 ? ["title", "description"] : ["description"];
      columns.forEach((c, i) => { if (!allowed.includes(c)) assert.equal(after[i], before[i], `${name}.${row.id}.${c}`); });
      assert.equal(row.missionId, row.id === 21 ? "guide.06" : "guide.07");
    } else if (name === "Reward") {
      assert.equal(before, undefined);
      assert.deepEqual(row, {id: 268, ownerType: "Guide", ownerId: "guide.07", order: 3,
        rewardType: "Card", rewardId: "8", amount: 1});
    } else {
      assert.equal(before, undefined);
      assert.deepEqual(row, {id: 1409, packId: "NormalPack_TEST", minGrade: "Bronze", cardId: 8, weight: 400});
    }
  }
  assert.equal(old.size, 0, "Row removal is not authorized");
  assert.deepEqual(changed, CHANGED[name], `${name}: unexpected delta`);
}
function buildPlan(env, backups, publishedAt, publishedBy) {
  const base = baseOf(env), {indexDoc, tables: saved} = backups;
  assert.equal(indexDoc.name, `${base}/_index`);
  const index = fields(indexDoc), minor = index.nextMinor;
  assert.equal(index.major, 6, "Schema migration is out of scope");
  assert(Number.isSafeInteger(minor) && minor > index.minor);
  const version = `${index.major}.${minor}`, pins = {...index.tables}, writes = [];
  for (const name of TABLES) {
    const {metaDoc, currentDoc, publishedDoc, rowDocs} = saved[name];
    assert.equal(metaDoc.name, `${base}/${name}`);
    assert.equal(currentDoc.name, `${base}/${name}/blob/current`);
    const meta = fields(metaDoc), pin = index.tables[name];
    assert(pin.blobPath.startsWith(`envs/${env}/specs/_release_`));
    assert.equal(publishedDoc.name, `${DATABASE}/${pin.blobPath}`);
    const previous = validateBlob(publishedDoc, pin);
    assert.deepEqual(fields(currentDoc), previous, "Unpublished current blob exists");
    assert.equal(meta.schemaVersion, index.major);
    assert.equal(meta.major ?? meta.schemaVersion, index.major);
    assert.equal(meta.revision, pin.revision);
    assert.equal(meta.rowsRevision, meta.revision);
    assert.equal(meta.payloadHash, pin.payloadHash);
    assert.equal(meta.rowCount, previous.rowCount);
    const matrix = JSON.parse(previous.payload);
    assert.deepEqual(meta.columns, matrix[0]);
    const local = localTable(name, meta.columns);
    validateDelta(name, matrix, local);
    const revision = meta.revision + 1;
    assert(Number.isSafeInteger(revision));
    for (const id of CHANGED[name]) {
      const row = local.rows.find((r) => r.id === id), doc = rowDocs[id];
      const destination = `${base}/${name}/rows/${id}`;
      if (name === "Mission") {
        assert.equal(doc.name, destination);
        const old = matrix.find((r) => r[0] === String(id));
        const mirrored = fields(doc);
        assert.deepEqual(meta.columns.map((c) => String(mirrored[c])), old);
        writes.push(update(destination, row, {updateTime: doc.updateTime}));
      } else {
        assert.equal(doc, null);
        writes.push(create(destination, row));
      }
    }
    const blob = {...previous, revision, rowCount: local.rows.length, payload: local.payload, payloadHash: local.payloadHash};
    const blobPath = `envs/${env}/specs/_release_${index.major}_${minor}_${name}`;
    const metaWrite = update(metaDoc.name, {...meta, revision, rowsRevision: revision,
      rowCount: local.rows.length, payloadHash: local.payloadHash, uploadedBy: publishedBy}, {updateTime: metaDoc.updateTime});
    metaWrite.update.fields.updatedAt = {timestampValue: publishedAt};
    writes.push(metaWrite, update(currentDoc.name, blob, {updateTime: currentDoc.updateTime}), create(`${DATABASE}/${blobPath}`, blob));
    pins[name] = {revision, payloadHash: local.payloadHash, blobPath};
  }
  const release = {major: index.major, minor, minAppMajor: index.minAppMajor, contentVersion: version,
    noticeTitle: index.noticeTitle || "", noticeBody: index.noticeBody || ""};
  writes.push(create(`${base}/_release_index_${index.major}_${minor}`, {...release, publishedAt, publishedBy,
    tablesJson: JSON.stringify(valueOf(pins))}),
  update(indexDoc.name, {...index, ...release, nextMinor: minor + 1,
    history: releaseHistory(index.history || [], version), tables: pins}, {updateTime: indexDoc.updateTime}));
  checkCommit(writes);
  return {kind: "guide-synergy-four-rows-v1", env, previousVersion: index.contentVersion, version,
    publishedAt, publishedBy, sources: sources(), backups, expectedTables: pins, writes};
}
async function readPins(request, env, tables) {
  await Promise.all(Object.entries(tables).map(async ([name, pin]) => {
    assert(pin.blobPath.startsWith(`envs/${env}/specs/_release_`), name);
    validateBlob(await request(`${DATABASE}/${pin.blobPath}`), pin);
  }));
}
async function makePlan(env, request) {
  const base = baseOf(env), indexDoc = await request(`${base}/_index`), index = fields(indexDoc);
  await readPins(request, env, index.tables);
  const tables = {};
  await Promise.all(TABLES.map(async (name) => {
    const [metaDoc, currentDoc, publishedDoc] = await Promise.all([
      request(`${base}/${name}`), request(`${base}/${name}/blob/current`), request(`${DATABASE}/${index.tables[name].blobPath}`)]);
    const rowDocs = {};
    for (const id of CHANGED[name]) {
      try { rowDocs[id] = await request(`${base}/${name}/rows/${id}`); }
      catch (error) { if (String(error.message).startsWith("Firestore 404:")) rowDocs[id] = null; else throw error; }
    }
    tables[name] = {metaDoc, currentDoc, publishedDoc, rowDocs};
  }));
  return buildPlan(env, {indexDoc, tables}, new Date().toISOString(), os.userInfo().username);
}
async function applyPlan(plan, request) {
  assert.equal(plan.kind, "guide-synergy-four-rows-v1");
  assert.deepEqual(plan.sources, sources(), "Plan inputs changed");
  assert.deepEqual(plan, buildPlan(plan.env, plan.backups, plan.publishedAt, plan.publishedBy));
  await request(`${DATABASE}:commit`, {writes: plan.writes});
  // An ambiguous commit is never automatically retried; compare every written document.
  await Promise.all(plan.writes.map(async (write) => {
    assert.deepEqual(fields(await request(write.update.name)), fields(write.update), write.update.name);
  }));
  await readPins(request, plan.env, plan.expectedTables);
  return {env: plan.env, published: plan.version, changed: CHANGED, preservedPins: Object.keys(plan.expectedTables).length - TABLES.length};
}
async function main() {
  const [mode, arg, output] = process.argv.slice(2);
  if (mode === "plan") {
    assert(output);
    const plan = await makePlan(arg, await client());
    fs.writeFileSync(output, JSON.stringify(plan, null, 2), {flag: "wx"});
    console.log(JSON.stringify({env: plan.env, from: plan.previousVersion, to: plan.version, writes: plan.writes.length, changed: CHANGED}));
  } else if (mode === "apply") console.log(JSON.stringify(await applyPlan(JSON.parse(fs.readFileSync(arg, "utf8")), await client())));
  else throw new Error("Use: plan <test|live> <new-plan.json> | apply <reviewed-plan.json>");
}
module.exports = {localTable, validateDelta, buildPlan, makePlan, applyPlan};
if (require.main === module) main().catch((error) => { console.error(error.message); process.exitCode = 1; });
