import test from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { createSpecOperations } from "../server/spec-service.mjs";

const require = createRequire(import.meta.url);
const publisher = require("../../functions/scripts/publish-all-csv-spec.js");
const { valueOf } = require("../../functions/scripts/publish-local-spec.js");
const root = fileURLToPath(new URL("../../", import.meta.url));
const database = "projects/bm-cardbattle/databases/cardbattle/documents";
const prefix = `${database}/envs/test/specs`;

// Use the actual repository schema and CSV reader without writing any fixture or
// source file. Every remote request is served by this in-memory Firestore fixture.
function fixture({ orphan = false } = {}) {
  const local = publisher.localSnapshot();
  const documents = new Map();
  const pins = {};
  const encode = (data) => Object.fromEntries(
    Object.entries(data).map(([key, value]) => [key, valueOf(value)]),
  );
  const document = (name, data) => ({
    name,
    fields: encode(data),
    updateTime: "2026-09-22T00:00:00Z",
  });
  for (const name of local.names) {
    const table = local.tables[name];
    const blobPath = `envs/test/specs/_release_${local.major}_1_${name}`;
    pins[name] = { revision: 1, payloadHash: table.payloadHash, blobPath };
    const metadata = {
      major: local.major,
      schemaVersion: local.major,
      columns: table.columns,
      revision: 1,
      rowsRevision: name === "Card" ? 0 : 1,
      rowCount: table.rows.length,
      payloadHash: table.payloadHash,
    };
    const blob = {
      schemaVersion: local.major,
      major: local.major,
      revision: 1,
      rowCount: table.rows.length,
      payloadHash: table.payloadHash,
      payload: table.payload,
    };
    documents.set(`${prefix}/${name}`, document(`${prefix}/${name}`, metadata));
    documents.set(`${prefix}/${name}/blob/current`, document(`${prefix}/${name}/blob/current`, blob));
    documents.set(`${database}/${blobPath}`, document(`${database}/${blobPath}`, blob));
  }
  documents.set(`${prefix}/_index`, document(`${prefix}/_index`, {
    major: local.major, minor: 1, nextMinor: 2,
    contentVersion: `${local.major}.1`, tables: pins, history: [],
  }));
  const firstRow = local.tables.Card.rows[0];
  const incorrectHp = firstRow.maxHp === 999 ? 998 : 999;
  const mirrorRows = local.tables.Card.rows.map((row, index) => document(
    `${prefix}/Card/rows/${row.id}`,
    index === 0 ? { ...row, maxHp: incorrectHp } : row,
  ));
  const orphanId = String(Math.max(...local.tables.Card.rows.map((row) => row.id)) + 1);
  if (orphan) mirrorRows.push(document(`${prefix}/Card/rows/${orphanId}`, { id: Number(orphanId), name: "stale row" }));
  let reads = 0;
  const request = async (resource, body) => {
    assert.equal(body, undefined, "Integration fixture must never receive a remote mutation");
    assert.ok(!resource.endsWith(":commit"), "Integration fixture must never commit");
    reads++;
    if (resource === `${prefix}/Card/rows?pageSize=1000`) return { documents: mirrorRows };
    if (!documents.has(resource)) throw Object.assign(new Error("missing fixture document"), { status: 404 });
    return documents.get(resource);
  };
  const operations = createSpecOperations({
    root,
    store: {},
    publisher,
    remote: (_token, allowWrites = false) => {
      assert.equal(allowWrites, false);
      return request;
    },
  });
  return { operations, request, firstRow, incorrectHp, orphanId, reads: () => reads };
}

test("real publisher mirror repair appears in service preview even when runtime blob is unchanged", async () => {
  const sourceHashes = publisher.snapshotSources();
  const f = fixture();
  const rawPlan = await publisher.makePlan("test", f.request);
  const rowWrites = rawPlan.writes.filter((write) => write.update?.name.includes("/rows/"));
  assert.deepEqual(rawPlan.changes.map((change) => change.table), ["Card"]);
  assert.equal(rowWrites.length, 1);
  assert.equal(rowWrites[0].update.name, `${prefix}/Card/rows/${f.firstRow.id}`);

  const preview = await f.operations("plan", { env: "test" }, { uid: "integration-admin", token: "unused-fixture-token" });
  assert.deepEqual(preview.diffs, [{
    table: "Card / rows (콘솔 미러)",
    rowId: String(f.firstRow.id),
    column: "maxHp",
    before: String(f.incorrectHp),
    after: String(f.firstRow.maxHp),
  }]);
  assert.equal(preview.diffsTruncated, false);
  assert.ok(f.reads() > 0);
  assert.deepEqual(publisher.snapshotSources(), sourceHashes, "Repository sources must stay untouched");
});

test("real publisher stale mirror row deletions are included in the reviewed delta", async () => {
  const f = fixture({ orphan: true });
  const rawPlan = await publisher.makePlan("test", f.request);
  assert.ok(rawPlan.writes.some((write) => write.delete === `${prefix}/Card/rows/${f.orphanId}`));
  const preview = await f.operations("plan", { env: "test" }, { uid: "integration-admin", token: "unused-fixture-token" });
  assert.deepEqual(preview.diffs.filter((diff) => diff.rowId === f.orphanId), [
    { table: "Card / rows (콘솔 미러)", rowId: f.orphanId, column: "id", before: f.orphanId, after: null },
    { table: "Card / rows (콘솔 미러)", rowId: f.orphanId, column: "name", before: "stale row", after: null },
  ]);
  assert.equal(preview.diffs.length, 3);
});
