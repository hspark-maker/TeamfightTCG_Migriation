"use strict";
const assert = require("node:assert/strict");
const {test, after} = require("node:test");
const {resolve} = require("node:path");
const output = process.env.TITLE_TEST_BUILD || "../lib";
const built = name => require(resolve(__dirname, output, name));
const {db} = built("firebaseApp");
const {clearSpecCache, readOptionalSpecRows, readSpecRows, specPayloadHash} = built("specs/specBlobReader");
after(() => db.terminate());
const indexPath = "envs/test/specs/_index";
const blobPath = "envs/test/specs/_release_6_99_Title";
const matrix = [["id", "titleId"], ["1", "title"]];
function blob(rows = matrix) {
  const payload = JSON.stringify(rows);
  return {major: 6, payload, payloadHash: specPayloadHash(payload), rowCount: rows.length - 1};
}
function install(t, docs) {
  clearSpecCache();
  const seen = [];
  t.mock.method(db, "doc", path => ({get: async () => {
    seen.push(path);
    const value = docs[path];
    if (value instanceof Error) throw value;
    return {exists: value !== undefined, data: () => value};
  }}));
  return seen;
}
function index(data) {return {major: 6, minor: 99, tables: {Title: {blobPath, payloadHash: data.payloadHash}}};}

test("only an absent entry in a valid index returns null", async t => {
  const seen = install(t, {[indexPath]: {major: 6, minor: 99, tables: {}}});
  assert.equal(await readOptionalSpecRows("test", "Title"), null);
  assert.deepEqual(seen, [indexPath]);
});

test("missing, unreadable and malformed indices fail rather than disabling titles", async t => {
  for (const value of [undefined, new Error("permission denied"), {major: 6, minor: 99, tables: []},
    {major: 6, minor: 99, tables: {Title: {}}}]) {
    install(t, {[indexPath]: value});
    await assert.rejects(readOptionalSpecRows("test", "Title"));
    t.mock.restoreAll();
  }
});

test("published missing or hash-damaged blobs fail; valid blobs are read and cached", async t => {
  const data = blob();
  install(t, {[indexPath]: index(data)});
  await assert.rejects(readOptionalSpecRows("test", "Title"), /missing/);
  t.mock.restoreAll();
  install(t, {[indexPath]: index(data), [blobPath]: {...data, payload: "broken"}});
  await assert.rejects(readOptionalSpecRows("test", "Title"), /hash mismatch/);
  t.mock.restoreAll();
  const seen = install(t, {[indexPath]: index(data), [blobPath]: data});
  const first = await readOptionalSpecRows("test", "Title");
  assert.equal(first[0].titleId, "title");
  assert.deepEqual(await readOptionalSpecRows("test", "Title"), first);
  assert.equal(seen.length, 2);
});

test("optional title reads reject invalid IDs even when a non-strict caller already cached filtered rows", async t => {
  const data = blob([...matrix, ["bad", "other"]]);
  install(t, {[indexPath]: index(data), [blobPath]: data});
  assert.equal((await readSpecRows("test", "Title")).length, 1);
  await assert.rejects(readOptionalSpecRows("test", "Title"), /Invalid Title row identifiers/);
});
