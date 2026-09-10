// Headless equivalent of SpecFirestoreUploader for an already imported SpecData.bytes.
// Never writes CSV/bytes. Plan first; apply the exact reviewed plan with Firestore CAS.
// node scripts/publish-local-spec.js plan test <plan.json>
// node scripts/publish-local-spec.js apply <plan.json>
const fs = require("node:fs");
const path = require("node:path");
const os = require("node:os");
const crypto = require("node:crypto");
const assert = require("node:assert/strict");

const ROOT = path.resolve(__dirname, "../..");
const PROJECT = "bm-cardbattle";
const DATABASE = `projects/${PROJECT}/databases/cardbattle/documents`;
const ALLOWED = ["Reward", "CardEnhance", "RouletteSlot", "PassLevel"];
const sourceFiles = ["Assets/Resources/SpecData.bytes", "Assets/Table/SpecDatas.cs",
  "Assets/Scripts/Editor/SpecLocalCsvImporter.cs", "Assets/Scripts/OutGame/Spec/SpecPayloadCodec.cs",
  "Assets/Scripts/OutGame/Spec/ContentVersion.cs", ...ALLOWED.map((t) => `docs/SpecData/${t}_sheet.csv`)];
const read = (file) => fs.readFileSync(path.join(ROOT, file), "utf8");
const digest = (bytes) => crypto.createHash("sha256").update(bytes).digest("hex");
const hash = (text) => crypto.createHash("md5").update(text, "utf8").digest("hex").slice(0, 16);

function quote(text) {
  const escapes = {'"': '\\"', "\\": "\\\\", "\n": "\\n", "\r": "\\r", "\t": "\\t"};
  return '"' + text.replace(/["\\\u0000-\u001f]/g,
    (c) => escapes[c] || `\\u${c.charCodeAt(0).toString(16).padStart(4, "0")}`) + '"';
}
const canonical = (matrix) => "[" + matrix.map((row) => "[" + row.map(quote).join(",") + "]").join(",") + "]";

function csv(text) {
  const rows = []; let row = []; let cell = ""; let quoted = false;
  text = text.replace(/^\ufeff/, "");
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (c === '"') {
      if (quoted && text[i + 1] === '"') { cell += '"'; i++; }
      else quoted = !quoted;
    } else if (!quoted && c === ",") { row.push(cell); cell = ""; }
    else if (!quoted && (c === "\r" || c === "\n")) {
      if (c === "\r" && text[i + 1] === "\n") i++;
      row.push(cell); rows.push(row); row = []; cell = "";
    } else cell += c;
  }
  assert(!quoted, "Unclosed CSV quote");
  if (cell || row.length) { row.push(cell); rows.push(row); }
  return rows;
}

function fieldsOf(value) {
  return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, valueOf(v)]));
}
function valueOf(v) {
  if (typeof v === "string") return {stringValue: v};
  if (typeof v === "number") { assert(Number.isSafeInteger(v)); return {integerValue: String(v)}; }
  if (Array.isArray(v)) return {arrayValue: {values: v.map(valueOf)}};
  return {mapValue: {fields: fieldsOf(v)}};
}
function unpack(v) {
  if (v.stringValue !== undefined) return v.stringValue;
  if (v.integerValue !== undefined) return Number(v.integerValue);
  if (v.timestampValue !== undefined) return v.timestampValue;
  if (v.arrayValue) return (v.arrayValue.values || []).map(unpack);
  if (v.mapValue) return unpackFields(v.mapValue.fields || {});
  throw new Error("Unsupported Firestore value");
}
const unpackFields = (fields) => Object.fromEntries(Object.entries(fields || {}).map(([k, v]) => [k, unpack(v)]));
const snapshotSources = () => Object.fromEntries(sourceFiles.map((f) => [f, digest(fs.readFileSync(path.join(ROOT, f)))]));
const releaseHistory = (history, version) => [...new Set(history.filter((v) => v !== version)), version].slice(-20);

function localSnapshot() {
  const keyMatch = read(sourceFiles[2]).match(/ENCRYPT_KEY\s*=\s*"([^"]+)"/);
  assert(keyMatch, "Importer key declaration changed");
  const key = Buffer.from(keyMatch[1]);
  const decipher = crypto.createDecipheriv("aes-128-cbc", key, Buffer.from(key).reverse());
  const data = JSON.parse(Buffer.concat([decipher.update(fs.readFileSync(path.join(ROOT, sourceFiles[0]))),
    decipher.final()]).toString("utf8"));
  const codec = read(sourceFiles[3]).match(/TableNames\s*=\s*\{([\s\S]*?)\}/);
  assert(codec, "Table list declaration changed");
  const names = [...codec[1].matchAll(/"(\w+)"/g)].map((m) => m[1]);
  const serverOnly = read(sourceFiles[3]).match(/ServerOnlyTableNames\s*=\s*\{([\s\S]*?)\}/);
  assert(serverOnly, "Server-only table list declaration changed");
  names.push(...[...serverOnly[1].matchAll(/"(\w+)"/g)].map((m) => m[1]));
  assert.equal(new Set(names).size, names.length, "Duplicate published table");
  const version = read(sourceFiles[4]);
  const major = Number(version.match(/const int Major\s*=\s*(\d+)/)[1]);
  const minAppMajor = Number(version.match(/const int MinAppMajor\s*=\s*(\d+)/)[1]);
  const classes = new Map([...read(sourceFiles[1]).matchAll(/public partial class (\w+)\s*\{([\s\S]*?)^\}/gm)]
    .map((m) => [m[1], [...m[2].matchAll(/^\s*public (int|long|string) (\w+);/gm)].map((f) => [f[1], f[2]])]));
  const tables = {};
  for (const name of names) {
    const fields = classes.get(name);
    assert(fields?.length && fields.some(([t, k]) => t === "int" && k === "id"), `Invalid schema: ${name}`);
    const rows = data[name];
    assert(Array.isArray(rows) && rows.length, `Empty table: ${name}`);
    rows.sort((a, b) => a.id - b.id);
    assert.equal(new Set(rows.map((r) => r.id)).size, rows.length, `Duplicate ID: ${name}`);
    const columns = fields.map(([, k]) => k);
    const matrix = [columns, ...rows.map((r) => fields.map(([type, k]) => {
      if (type === "string") { assert(r[k] == null || typeof r[k] === "string"); return r[k] ?? ""; }
      assert(Number.isSafeInteger(r[k]), `Unsafe integer: ${name}.${k}`);
      if (type === "int") assert(r[k] >= -2147483648 && r[k] <= 2147483647);
      return String(r[k]);
    }))];
    if (ALLOWED.includes(name)) {
      const parsed = csv(read(`docs/SpecData/${name}_sheet.csv`));
      const headerIndex = parsed.findIndex((r) => columns.every((c) => r.includes(c)));
      assert(headerIndex >= 0 && headerIndex <= 1, `CSV header: ${name}`);
      const indices = columns.map((c) => parsed[headerIndex].indexOf(c));
      const csvRows = parsed.slice(headerIndex + 2).filter((r) => r.some((c) => c.trim())).map((r) =>
        fields.map(([type], i) => {
          const cell = r[indices[i]] ?? "";
          if (type === "string") return cell;
          assert(/^[+-]?\d+$/.test(cell.trim()), `Invalid CSV integer: ${name}`);
          const n = Number(cell.trim()); assert(Number.isSafeInteger(n)); return String(n);
        }));
      const idIndex = columns.indexOf("id");
      csvRows.sort((a, b) => Number(a[idIndex]) - Number(b[idIndex]));
      assert.deepEqual(matrix.slice(1), csvRows, `${name}: CSV and imported bytes differ; run official importer`);
    }
    const payload = canonical(matrix);
    assert(Buffer.byteLength(payload) <= 1048576 - 89, `Oversized payload: ${name}`);
    for (const r of rows) assert(Buffer.byteLength(JSON.stringify(fieldsOf(r))) <= 900 * 1024);
    tables[name] = {columns, rows, matrix, payload, payloadHash: hash(payload)};
  }
  return {names, major, minAppMajor, tables};
}

async function client() {
  const toolsRoot = process.env.FIREBASE_TOOLS_ROOT || path.join(process.env.APPDATA, "npm/node_modules/firebase-tools/lib");
  const config = require(path.join(toolsRoot, "configstore.js")).configstore;
  const tokens = config.get("tokens");
  assert(tokens?.refresh_token, "Firebase CLI login required");
  const scopes = [...(config.get("loginScopes") || tokens.scopes || [require(path.join(toolsRoot, "scopes.js")).CLOUD_PLATFORM])];
  const auth = await require(path.join(toolsRoot, "auth.js")).getAccessToken(tokens.refresh_token, scopes);
  return async (resource, body) => {
    const response = await fetch(`https://firestore.googleapis.com/v1/${resource}`, {
      method: body ? "POST" : "GET", headers: {Authorization: `Bearer ${auth.access_token}`, "Content-Type": "application/json"},
      body: body ? JSON.stringify(body) : undefined,
    });
    const result = await response.json();
    if (!response.ok) throw new Error(`Firestore ${response.status}: ${result.error?.message || result[0]?.error?.message || response.statusText}`);
    return result;
  };
}
const update = (name, fields, condition) => ({update: {name, fields}, ...(condition ? {currentDocument: condition} : {})});
const blobFields = (local, table, revision) => fieldsOf({schemaVersion: local.major, major: local.major,
  revision, rowCount: table.rows.length, payloadHash: table.payloadHash, payload: table.payload});
function checkCommit(writes) {
  assert(writes.length <= 500, "Commit exceeds 500 writes");
  assert(Buffer.byteLength(JSON.stringify({writes})) <= 10 * 1024 * 1024, "Commit exceeds 10 MiB");
  assert(writes.every((w) => Boolean(w.update) !== Boolean(w.delete)), "Only REST update/delete operations are supported");
  const names = writes.map((w) => w.update?.name || w.delete);
  assert.equal(new Set(names).size, names.length, "Duplicate document writes");
}

async function makePlan(env, request) {
  assert(["test", "live"].includes(env), "Explicit test/live environment required");
  const local = localSnapshot();
  const base = `${DATABASE}/envs/${env}/specs`;
  const indexDoc = await request(`${base}/_index`);
  const index = unpackFields(indexDoc.fields);
  assert.equal(index.major, local.major, "Schema migration requires official release workflow");
  const minor = index.nextMinor;
  assert(Number.isSafeInteger(minor) && minor > index.minor);
  const version = `${local.major}.${minor}`;
  const writes = []; const tableEntries = {}; const changes = []; const backups = [indexDoc];
  for (const name of local.names) {
    const table = local.tables[name];
    const metaDoc = await request(`${base}/${name}`);
    const meta = unpackFields(metaDoc.fields);
    // Older metadata stores this compatibility generation only as schemaVersion.
    assert.equal(meta.major ?? meta.schemaVersion, local.major, `${name}: remote major mismatch`);
    assert.equal(meta.schemaVersion, local.major, `${name}: remote schema mismatch`);
    assert.deepEqual(meta.columns, table.columns, `${name}: column contract changed`);
    assert(Number.isSafeInteger(meta.revision) && meta.revision >= 0);
    let revision = meta.revision;
    if (meta.payloadHash !== table.payloadHash) {
      assert(ALLOWED.includes(name), `Unreviewed table change: ${name}`);
      const oldBlob = await request(`${base}/${name}/blob/current`);
      const old = unpackFields(oldBlob.fields);
      assert.equal(old.payloadHash, meta.payloadHash); assert.equal(hash(old.payload), meta.payloadHash);
      assert.equal(old.revision, meta.revision);
      const matrix = JSON.parse(old.payload);
      assert.deepEqual(matrix[0], table.columns);
      assert.equal(matrix.length - 1, old.rowCount);
      const idIndex = table.columns.indexOf("id");
      const previous = new Map(matrix.slice(1).map((r) => [r[idIndex], r]));
      const mirrored = meta.rowsRevision === meta.revision;
      const stale = new Set(previous.keys());
      if (!mirrored) {
        stale.clear(); let pageToken = "";
        do {
          const page = await request(`${base}/${name}/rows?pageSize=1000${pageToken ? "&pageToken=" + encodeURIComponent(pageToken) : ""}`);
          for (const doc of page.documents || []) stale.add(doc.name.split("/").pop());
          pageToken = page.nextPageToken || "";
        } while (pageToken);
      }
      revision++; assert(Number.isSafeInteger(revision));
      for (let i = 0; i < table.rows.length; i++) {
        const row = table.rows[i]; const id = String(row.id); stale.delete(id);
        if (!mirrored || canonical([previous.get(id) || []]) !== canonical([table.matrix[i + 1]]))
          writes.push(update(`${base}/${name}/rows/${id}`, fieldsOf(row)));
      }
      for (const id of stale) writes.push({delete: `${base}/${name}/rows/${id}`});
      const metaFields = fieldsOf({table: name, schemaVersion: local.major, major: local.major, revision,
        rowsRevision: revision, rowCount: table.rows.length, columns: table.columns, idColumn: "id",
        payloadHash: table.payloadHash, uploadedBy: os.userInfo().username,
        appVersion: read("ProjectSettings/ProjectSettings.asset").match(/^\s*bundleVersion:\s*(.+)$/m)[1].trim()});
      metaFields.updatedAt = {timestampValue: new Date().toISOString()};
      writes.push(update(`${base}/${name}`, metaFields, {updateTime: metaDoc.updateTime}));
      writes.push(update(`${base}/${name}/blob/current`, blobFields(local, table, revision), {updateTime: oldBlob.updateTime}));
      changes.push({table: name, oldRows: meta.rowCount, rows: table.rows.length, revision, payloadHash: table.payloadHash});
      backups.push(metaDoc, oldBlob);
    } else {
      // The official publish gate covers every table, including unchanged metadata.
      writes.push({...update(`${base}/${name}`, {payloadHash: valueOf(meta.payloadHash)}, {updateTime: metaDoc.updateTime}),
        updateMask: {fieldPaths: ["payloadHash"]}});
    }
    const blobPath = `envs/${env}/specs/_release_${local.major}_${minor}_${name}`;
    writes.push(update(`${DATABASE}/${blobPath}`, blobFields(local, table, revision), {exists: false}));
    tableEntries[name] = {revision, payloadHash: table.payloadHash, blobPath};
  }
  assert(changes.length, "No unpublished table change; refusing an empty release");
  const title = "보상 데이터 업데이트";
  const body = "카드 중복 보상과 강화·룰렛·패스 데이터를 업데이트했습니다.";
  const release = {major: local.major, minor, minAppMajor: local.minAppMajor, contentVersion: version,
    noticeTitle: title, noticeBody: body};
  writes.push(update(`${base}/_release_index_${local.major}_${minor}`, fieldsOf({...release,
    publishedAt: new Date().toISOString(), publishedBy: os.userInfo().username,
    tablesJson: JSON.stringify(valueOf(tableEntries))}), {exists: false}));
  const history = releaseHistory(index.history || [], version);
  writes.push(update(`${base}/_index`, fieldsOf({...release, nextMinor: minor + 1, history, tables: tableEntries}),
    {updateTime: indexDoc.updateTime}));
  checkCommit(writes);
  return {project: PROJECT, env, previousVersion: index.contentVersion, version, changes, sources: snapshotSources(),
    writes, backups, expectedTables: tableEntries};
}

async function applyPlan(plan, request) {
  assert.equal(plan.project, PROJECT);
  assert(["test", "live"].includes(plan.env));
  assert.deepEqual(snapshotSources(), plan.sources, "Local inputs changed since dry-run");
  const local = localSnapshot();
  assert.deepEqual(Object.keys(plan.expectedTables), local.names);
  for (const name of local.names) assert.equal(plan.expectedTables[name].payloadHash, local.tables[name].payloadHash);
  checkCommit(plan.writes);
  const prefix = `${DATABASE}/envs/${plan.env}/specs/`;
  for (const w of plan.writes) assert((w.update?.name || w.delete).startsWith(prefix));
  // One atomic commit: mirrors + metadata + immutable snapshots + index pointer.
  // Never retry an ambiguous commit; inspect the index before any new plan.
  await request(`${DATABASE}:commit`, {writes: plan.writes});
  const index = unpackFields((await request(`${prefix}_index`)).fields);
  assert.equal(index.contentVersion, plan.version);
  assert.deepEqual(index.tables, plan.expectedTables);
  for (const name of local.names) {
    const blob = unpackFields((await request(`${DATABASE}/${index.tables[name].blobPath}`)).fields);
    assert.equal(blob.payload, local.tables[name].payload);
    assert.equal(blob.payloadHash, hash(blob.payload));
    assert.equal(blob.rowCount, local.tables[name].rows.length);
    assert.equal(blob.revision, index.tables[name].revision);
  }
  console.log(JSON.stringify({published: plan.version, env: plan.env, verifiedTables: local.names.length}));
}

async function main() {
  const [mode, arg, output] = process.argv.slice(2);
  if (mode === "plan") {
    assert(output, "Plan output path required");
    const plan = await makePlan(arg, await client());
    fs.writeFileSync(output, JSON.stringify(plan, null, 2), {flag: "wx"});
    console.log(JSON.stringify({env: plan.env, from: plan.previousVersion, to: plan.version,
      changes: plan.changes, writes: plan.writes.length, plan: output}));
  } else if (mode === "apply") await applyPlan(JSON.parse(fs.readFileSync(arg, "utf8")), await client());
  else throw new Error("Use: plan <test|live> <plan.json> | apply <plan.json>");
}
module.exports = {canonical, csv, hash, valueOf, unpack, localSnapshot, makePlan, applyPlan, checkCommit, releaseHistory, client};
if (require.main === module) main().catch((e) => {
  console.error(e.message, e.cause?.code || ""); process.exitCode = 1;
});
