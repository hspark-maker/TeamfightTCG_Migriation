// Narrow, test-only schema migration. Plan is read-only; apply commits one reviewed CAS plan.
// Never reads or regenerates SpecData.bytes. All unrelated published payloads remain identical.
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");
const assert = require("node:assert/strict");
const {isDeepStrictEqual} = require("node:util");
const all = require("./publish-all-csv-spec");
const {canonical, hash, valueOf, unpack, checkCommit, releaseHistory, client} = require("./publish-local-spec");
const ROOT = path.resolve(__dirname, "../..");
const DATABASE = "projects/bm-cardbattle/databases/cardbattle/documents";
const BASE = `${DATABASE}/envs/test/specs`;
const TARGETS = ["Achievement", "Title", "Reward"];
const OLD_ACHIEVEMENT = ["id", "achievementId", "groupId", "stage", "eventKey", "synergyId", "targetCount",
  "title", "description", "rewardCurrency", "rewardAmount", "sortOrder", "enabled"];
const NEW_ACHIEVEMENT = OLD_ACHIEVEMENT.filter((key) => !["rewardCurrency", "rewardAmount"].includes(key));
const OLD_TITLE = ["id", "titleId", "eventKey", "synergyId", "targetCount", "description"];
const NEW_TITLE = ["id", "titleId"];
const REWARD = ["id", "ownerType", "ownerId", "order", "rewardType", "rewardId", "amount"];
const fieldsOf = (value) => Object.fromEntries(Object.entries(value).map(([key, val]) => [key, valueOf(val)]));
const unpackFields = (fields) => Object.fromEntries(Object.entries(fields || {}).map(([key, val]) => [key, unpack(val)]));
const fingerprint = (bytes) => crypto.createHash("sha256").update(bytes).digest("hex");
const update = (name, fields, currentDocument) => ({update: {name, fields}, currentDocument});
const objects = (matrix) => matrix.slice(1).map((row) => Object.fromEntries(matrix[0].map((key, i) => [key, row[i]])));
const project = (row, columns) => Object.fromEntries(columns.map((key) => [key, row[key]]));
function snapshotSources() {
  return {...all.snapshotSources(), "functions/scripts/publish-achievement-rewards.js": fingerprint(fs.readFileSync(__filename))};
}
function verifyBlob(doc, name, pin) {
  const blob = unpackFields(doc.fields);
  assert.equal(blob.payloadHash, pin.payloadHash, `${name}: blob hash metadata`);
  assert.equal(hash(blob.payload), pin.payloadHash, `${name}: blob payload hash`);
  assert.equal(blob.revision, pin.revision, `${name}: blob revision`);
  const matrix = JSON.parse(blob.payload);
  assert(Array.isArray(matrix) && matrix.length > 1, `${name}: empty table`);
  assert.equal(matrix.length - 1, blob.rowCount, `${name}: row count`);
  assert(matrix.every((row) => Array.isArray(row) && row.length === matrix[0].length &&
    row.every((cell) => typeof cell === "string")), `${name}: invalid matrix`);
  assert.equal(canonical(matrix), blob.payload, `${name}: noncanonical payload`);
  return {blob, matrix};
}
function validateMigration(previous, local) {
  assert.deepEqual(local.Achievement.matrix[0], NEW_ACHIEVEMENT);
  assert.deepEqual(local.Title.matrix[0], NEW_TITLE);
  assert.deepEqual(local.Reward.matrix[0], REWARD);
  if (isDeepStrictEqual(previous.Achievement[0], NEW_ACHIEVEMENT) &&
      isDeepStrictEqual(previous.Title[0], NEW_TITLE)) {
    for (const name of TARGETS) assert.deepEqual(previous[name], local[name].matrix, `${name}: already migrated but differs`);
    throw new Error("No unpublished table change; migration already applied");
  }
  assert.deepEqual(previous.Achievement[0], OLD_ACHIEVEMENT, "Only the reviewed Achievement 13-to-11-column migration is allowed");
  assert.deepEqual(previous.Title[0], OLD_TITLE, "Only the reviewed Title 6-to-2-column migration is allowed");
  assert.deepEqual(previous.Reward[0], REWARD, "Reward schema must not change");
  const oldAchievements = objects(previous.Achievement);
  const oldTitles = objects(previous.Title);
  const oldRewards = objects(previous.Reward);
  const newAchievements = objects(local.Achievement.matrix);
  const newTitles = objects(local.Title.matrix);
  const newRewards = objects(local.Reward.matrix);
  assert.equal(oldAchievements.length, 61); assert.equal(oldTitles.length, 8);
  assert.deepEqual(newAchievements, oldAchievements.map((row) => project(row, NEW_ACHIEVEMENT)), "Achievement conditions/IDs changed");
  assert.deepEqual(newTitles, oldTitles.map((row) => project(row, NEW_TITLE)), "Title IDs changed");
  const oldIds = new Set(oldRewards.map((row) => row.id));
  assert.equal(oldIds.size, oldRewards.length);
  assert.equal(new Set(newRewards.map((row) => row.id)).size, newRewards.length);
  assert.equal(new Set(newRewards.map((row) => `${row.ownerType}:${row.ownerId}:${row.order}`)).size, newRewards.length);
  assert.deepEqual(newRewards.filter((row) => oldIds.has(row.id)), oldRewards, "Existing Reward rows/order changed");
  const added = newRewards.filter((row) => !oldIds.has(row.id));
  assert.equal(added.length, 69, "Exactly 61 currency and 8 title rewards required");
  assert(added.every((row) => row.ownerType === "Achievement" && Number(row.id) >= 276), "Unexpected reward additions/ID reuse");
  const expected = [];
  for (const row of oldAchievements) expected.push({ownerType: "Achievement", ownerId: row.achievementId,
    order: "1", rewardType: "Currency", rewardId: row.rewardCurrency, amount: row.rewardAmount});
  const mappings = oldTitles.map((title) => {
    const owners = oldAchievements.filter((entry) => ["eventKey", "synergyId", "targetCount"].every((key) => entry[key] === title[key]));
    assert.equal(owners.length, 1, `Ambiguous/missing old title condition: ${title.titleId}`);
    expected.push({ownerType: "Achievement", ownerId: owners[0].achievementId,
      order: "2", rewardType: "Title", rewardId: title.titleId, amount: "1"});
    return {titleId: title.titleId, achievementId: owners[0].achievementId};
  });
  assert.deepEqual(added.map((row) => project(row, REWARD.slice(1))), expected, "Rewards were not preserved exactly");
  return {oldRewardRows: oldRewards.length, newRewardRows: newRewards.length, achievements: 61, titles: 8, mappings};
}
async function listRows(request, name) {
  const rows = []; let token = "";
  do {
    const page = await request(`${name}/rows?pageSize=1000${token ? "&pageToken=" + encodeURIComponent(token) : ""}`);
    rows.push(...(page.documents || [])); token = page.nextPageToken || "";
  } while (token);
  return rows;
}
async function makePlan(env, request) {
  assert.equal(env, "test", "This migration is test-only");
  const sources = snapshotSources(); const local = all.localSnapshot();
  const indexDoc = await request(`${BASE}/_index`); const index = unpackFields(indexDoc.fields);
  assert.equal(index.major, local.major, "Major must not change");
  const names = Object.keys(index.tables);
  assert(TARGETS.every((name) => names.includes(name)), "All three migration tables must already be published");
  assert(names.every((name) => /^\w+$/.test(name)), "Invalid table name");
  const minor = index.nextMinor;
  assert(Number.isSafeInteger(minor) && minor > index.minor, "Invalid next minor");
  const version = `${index.major}.${minor}`;
  const backups = [indexDoc]; const previous = {}; const state = {};
  const oldReleaseIndex = await request(`${BASE}/_release_index_${index.major}_${index.minor}`);
  backups.push(oldReleaseIndex);
  for (const name of names) {
    const pin = index.tables[name];
    assert(typeof pin.blobPath === "string" && new RegExp(`^envs/test/specs/_release_\\d+_\\d+_${name}$`).test(pin.blobPath), "Invalid immutable pin");
    const [metaDoc, currentDoc, releaseDoc] = await Promise.all([
      request(`${BASE}/${name}`), request(`${BASE}/${name}/blob/current`), request(`${DATABASE}/${pin.blobPath}`),
    ]);
    backups.push(metaDoc, currentDoc, releaseDoc);
    const meta = unpackFields(metaDoc.fields);
    assert.equal(meta.major ?? meta.schemaVersion, local.major);
    assert.equal(meta.schemaVersion, local.major);
    assert.equal(meta.revision, pin.revision, `${name}: unpublished metadata revision`);
    assert.equal(meta.payloadHash, pin.payloadHash, `${name}: unpublished metadata hash`);
    const current = verifyBlob(currentDoc, name, pin); const release = verifyBlob(releaseDoc, name, pin);
    assert.equal(current.blob.payload, release.blob.payload, `${name}: staged payload differs from pin`);
    assert.deepEqual(meta.columns, current.matrix[0], `${name}: metadata columns`);
    assert.equal(meta.rowCount, current.blob.rowCount);
    state[name] = {meta, metaDoc, currentDoc, blob: current.blob};
    if (TARGETS.includes(name)) previous[name] = current.matrix;
  }
  const preservation = validateMigration(previous, local.tables);
  const writes = []; const changes = []; const expectedTables = {}; const expectedPayloads = {};
  for (const name of names) {
    const {meta, metaDoc, currentDoc, blob} = state[name];
    let nextBlob = blob;
    if (TARGETS.includes(name)) {
      const table = local.tables[name]; const revision = meta.revision + 1;
      assert(Number.isSafeInteger(revision));
      const rows = await listRows(request, `${BASE}/${name}`); backups.push(...rows);
      const existing = new Map(rows.map((doc) => [doc.name.split("/").pop(), doc]));
      assert.equal(existing.size, rows.length);
      for (const row of table.rows) {
        const old = existing.get(String(row.id)); existing.delete(String(row.id));
        if (!old || !isDeepStrictEqual(unpackFields(old.fields), row))
          writes.push(update(`${BASE}/${name}/rows/${row.id}`, fieldsOf(row), old ? {updateTime: old.updateTime} : {exists: false}));
      }
      for (const old of existing.values()) writes.push({delete: old.name, currentDocument: {updateTime: old.updateTime}});
      const nextMeta = {...meta, revision, rowsRevision: revision, rowCount: table.rows.length,
        columns: table.columns, payloadHash: table.payloadHash};
      const fields = fieldsOf(nextMeta); fields.updatedAt = {timestampValue: new Date().toISOString()};
      writes.push(update(metaDoc.name, fields, {updateTime: metaDoc.updateTime}));
      nextBlob = {...blob, revision, rowCount: table.rows.length, payload: table.payload, payloadHash: table.payloadHash};
      writes.push(update(currentDoc.name, fieldsOf(nextBlob), {updateTime: currentDoc.updateTime}));
      changes.push({table: name, oldRows: blob.rowCount, rows: nextBlob.rowCount, revision, payloadHash: nextBlob.payloadHash});
    } else {
      for (const doc of [metaDoc, currentDoc]) writes.push({...update(doc.name,
        {payloadHash: valueOf(meta.payloadHash)}, {updateTime: doc.updateTime}), updateMask: {fieldPaths: ["payloadHash"]}});
    }
    const blobPath = `envs/test/specs/_release_${index.major}_${minor}_${name}`;
    writes.push(update(`${DATABASE}/${blobPath}`, fieldsOf(nextBlob), {exists: false}));
    expectedTables[name] = {revision: nextBlob.revision, payloadHash: nextBlob.payloadHash, blobPath};
    expectedPayloads[name] = nextBlob.payload;
  }
  const release = {major: index.major, minor, minAppMajor: index.minAppMajor, contentVersion: version,
    noticeTitle: "업적 보상 데이터 업데이트", noticeBody: "업적 재화와 칭호 보상을 통합했습니다."};
  writes.push(update(`${BASE}/_release_index_${index.major}_${minor}`, fieldsOf({...release,
    publishedAt: new Date().toISOString(), publishedBy: "achievement-reward-migration",
    tablesJson: JSON.stringify(valueOf(expectedTables))}), {exists: false}));
  writes.push(update(`${BASE}/_index`, fieldsOf({...index, ...release, nextMinor: minor + 1,
    history: releaseHistory(index.history || [], version), tables: expectedTables}), {updateTime: indexDoc.updateTime}));
  checkCommit(writes);
  assert.deepEqual(snapshotSources(), sources, "Local sources changed during planning");
  const plan = {format: "achievement-rewards-test-v1", project: "bm-cardbattle", env, previousVersion: index.contentVersion,
    version, sources, changes, preservation, writes, backups, expectedTables, expectedPayloads};
  return {...plan, planHash: fingerprint(JSON.stringify(plan))};
}
async function verifyPublished(plan, request) {
  const index = unpackFields((await request(`${BASE}/_index`)).fields);
  assert.equal(index.contentVersion, plan.version);
  assert.deepEqual(index.tables, plan.expectedTables);
  const releaseDoc = await request(`${BASE}/_release_index_${index.major}_${index.minor}`);
  const release = unpackFields(releaseDoc.fields);
  assert.deepEqual(unpack(JSON.parse(release.tablesJson)), plan.expectedTables);
  for (const [name, pin] of Object.entries(index.tables)) {
    const {blob} = verifyBlob(await request(`${DATABASE}/${pin.blobPath}`), name, pin);
    assert.equal(blob.payload, plan.expectedPayloads[name], `${name}: published payload mismatch`);
  }
  return {published: plan.version, env: "test", verifiedTables: Object.keys(index.tables).length};
}
async function applyPlan(plan, request) {
  const {planHash, ...content} = plan;
  assert.equal(fingerprint(JSON.stringify(content)), planHash, "Plan content changed");
  assert.equal(plan.format, "achievement-rewards-test-v1"); assert.equal(plan.project, "bm-cardbattle");
  assert.equal(plan.env, "test", "This migration is test-only");
  assert.deepEqual(snapshotSources(), plan.sources, "Local inputs changed since plan");
  const local = all.localSnapshot();
  for (const name of TARGETS) assert.equal(plan.expectedPayloads[name], local.tables[name].payload);
  const current = unpackFields((await request(`${BASE}/_index`)).fields);
  if (current.contentVersion === plan.version) return {...await verifyPublished(plan, request), alreadyApplied: true};
  assert.equal(current.contentVersion, plan.previousVersion, "Published version changed since plan");
  checkCommit(plan.writes);
  const newPrefix = `_release_${plan.version.replace(".", "_")}_`;
  for (const write of plan.writes) {
    const name = write.update?.name || write.delete;
    assert(name.startsWith(`${BASE}/`), "Write escaped test specs");
    const suffix = name.slice(BASE.length + 1);
    const newRelease = suffix.startsWith(newPrefix) || suffix === `_release_index_${plan.version.replace(".", "_")}`;
    const target = TARGETS.some((table) => suffix === table || suffix === `${table}/blob/current` ||
      new RegExp(`^${table}/rows/[1-9][0-9]*$`).test(suffix));
    const guard = write.updateMask && isDeepStrictEqual(write.updateMask.fieldPaths, ["payloadHash"]) &&
      Object.keys(plan.expectedTables).some((table) => suffix === table || suffix === `${table}/blob/current`);
    assert(suffix === "_index" || newRelease || target || guard, `Unreviewed write: ${suffix}`);
    assert(write.currentDocument && (write.currentDocument.updateTime || write.currentDocument.exists === false), "Missing CAS");
    if (newRelease) assert.equal(write.currentDocument.exists, false, "Immutable release overwrite forbidden");
  }
  // Exactly one commit. Never automatically retry an ambiguous result.
  await request(`${DATABASE}:commit`, {writes: plan.writes});
  return verifyPublished(plan, request);
}
async function main() {
  const [mode, arg, output] = process.argv.slice(2);
  if (mode === "plan") {
    assert(output, "Plan output required");
    const plan = await makePlan(arg, await client());
    fs.writeFileSync(output, JSON.stringify(plan, null, 2), {flag: "wx"});
    console.log(JSON.stringify({from: plan.previousVersion, to: plan.version, changes: plan.changes,
      preservation: plan.preservation, writes: plan.writes.length, plan: output}));
  } else if (mode === "apply") console.log(JSON.stringify(await applyPlan(JSON.parse(fs.readFileSync(arg, "utf8")), await client())));
  else throw new Error("Use: plan test <plan.json> | apply <plan.json>");
}
module.exports = {validateMigration, makePlan, applyPlan, verifyPublished};
if (require.main === module) main().catch((error) => { console.error(error.message); process.exitCode = 1; });
