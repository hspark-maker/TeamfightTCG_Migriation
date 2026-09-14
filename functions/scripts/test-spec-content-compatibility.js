"use strict";
const assert = require("node:assert/strict");
const {test} = require("node:test");
const {db} = require("../lib/firebaseApp");
const {clearSpecCache, readSpecRowsWithContentMajor, readPinnedSpecRows, specPayloadHash} =
  require("../lib/specs/specBlobReader");

test("published tables retain legacy majors, pair rows with their index, and reject unsupported content", async () => {
  const originalDoc = db.doc;
  try {
    for (const major of [4, 5, 6, 7]) {
      clearSpecCache();
      const payload = JSON.stringify([["id", "title"], ["1", "generation " + major]]);
      const payloadHash = specPayloadHash(payload);
      const blobPath = "envs/test/specs/Test/releases/" + major;
      let indexReads = 0;
      db.doc = (document) => ({get: async () => {
        if (document.endsWith("/_index")) {
          indexReads++;
          return {exists: true, data: () => ({major, minor: 1, tables: {Test: {blobPath, payloadHash}}})};
        }
        assert.equal(document, blobPath);
        return {exists: true, data: () => ({major, payload, payloadHash, rowCount: 1})};
      }});
      if (major === 7) {
        await assert.rejects(readSpecRowsWithContentMajor("test", "Test"), /incompatible/);
        await assert.rejects(readPinnedSpecRows("test", "Test", {blobPath, payloadHash}), /not in/);
      } else {
        const result = await readSpecRowsWithContentMajor("test", "Test");
        assert.deepEqual(result, {major, rows: [{id: 1, title: "generation " + major}]});
        await readSpecRowsWithContentMajor("test", "Test");
        assert.equal(indexReads, 1);
        clearSpecCache();
        assert.deepEqual(await readPinnedSpecRows("test", "Test", {blobPath, payloadHash}), result.rows);
      }
    }
  } finally {
    db.doc = originalDoc;
    clearSpecCache();
  }
});
