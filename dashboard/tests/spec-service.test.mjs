import test from "node:test";
import assert from "node:assert/strict";
import { createServer, request as httpRequest } from "node:http";
import {
  createSpecMiddleware,
  createSpecOperations,
} from "../server/spec-service.mjs";

const caller = { uid: "admin-one", token: "verified-token" };
const base = "projects/bm-cardbattle/databases/cardbattle/documents";
const stringValue = (value) => ({ stringValue: value });
function fixture() {
  let clock = 1000;
  let source = "original";
  let applied = 0;
  const snapshot = {
    names: ["Card"],
    major: 6,
    tables: {
      Card: {
        columns: ["id", "maxHp"],
        rows: [{ id: 1, maxHp: 7 }],
        matrix: [
          ["id", "maxHp"],
          ["1", "7"],
        ],
        payloadHash: "new-hash",
      },
    },
  };
  const plan = {
    previousVersion: "6.1",
    version: "6.2",
    sources: { csv: "original" },
    changes: [
      {
        table: "Card",
        oldRows: 1,
        rows: 1,
        revision: 2,
        payloadHash: "new-hash",
      },
    ],
    writes: [{ update: {} }],
    backups: [
      {
        name: `${base}/envs/test/specs/Card/blob/current`,
        fields: {
          payload: stringValue(
            JSON.stringify([
              ["id", "maxHp"],
              ["1", "3"],
            ]),
          ),
        },
      },
    ],
  };
  const publisher = {
    localSnapshot: () => snapshot,
    snapshotSources: () => ({ csv: source }),
    makePlan: async (env) => {
      assert.equal(env, "test");
      return plan;
    },
    applyPlan: async (actual) => {
      assert.equal(actual, plan);
      applied++;
      assert.equal(source, "original", "Local inputs changed since dry-run");
      return { published: "6.2", env: "test", verifiedTables: 1 };
    },
  };
  const calls = [];
  const remote = (token, writes = false) => {
    assert.equal(token, caller.token);
    calls.push({ writes });
    return async (resource) => {
      assert.match(resource, /envs\/(test|live)\/specs\/_index$/);
      return {
        fields: {
          contentVersion: stringValue("6.1"),
          tables: {
            mapValue: {
              fields: {
                Card: {
                  mapValue: {
                    fields: { payloadHash: stringValue("old-hash") },
                  },
                },
              },
            },
          },
        },
      };
    };
  };
  const store = { save: async () => ({ changedCells: 1 }) };
  const ops = createSpecOperations({
    root: ".",
    store,
    publisher,
    remote,
    now: () => clock,
  });
  return {
    ops,
    publisher,
    store,
    calls,
    snapshot,
    setSource: (value) => {
      source = value;
    },
    advance: (ms) => {
      clock += ms;
    },
    applied: () => applied,
  };
}

test("middleware rejects anonymous, non-admin, bad origin/host, method and JSON before operations", async (t) => {
  const seen = [];
  const middleware = createSpecMiddleware({
    verifyToken: async (token) => {
      if (token === "expired") throw new Error("expired");
      return {
        uid: "uid",
        admin:
          token === "admin" ? true : token === "string-admin" ? "true" : false,
      };
    },
    operations: async (...args) => {
      seen.push(args);
      return { ok: true };
    },
  });
  const server = createServer((req, res) =>
    middleware(req, res, () => {
      res.statusCode = 404;
      res.end();
    }),
  );
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  t.after(() => new Promise((resolve) => server.close(resolve)));
  const origin = `http://127.0.0.1:${server.address().port}`;
  async function request({
    token,
    headers,
    method = "POST",
    body = "{}",
    path = "list",
  } = {}) {
    return fetch(`${origin}/__spec/${path}`, {
      method,
      headers: {
        Origin: origin,
        "Content-Type": "application/json",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...headers,
      },
      body: method === "GET" ? undefined : body,
    });
  }
  for (const [options, status] of [
    [{}, 401],
    [{ token: "expired" }, 401],
    [{ token: "member" }, 403],
    [{ token: "string-admin" }, 403],
    [{ token: "admin", headers: { Origin: "https://foreign.test" } }, 403],
    [{ token: "admin", method: "GET" }, 400],
    [{ token: "admin", body: "[]" }, 400],
    [{ token: "admin", body: "{" }, 400],
    [{ token: "admin", path: "arbitrary" }, 404],
  ]) {
    const response = await request(options);
    assert.equal(response.status, status, JSON.stringify(options));
    await response.text();
  }
  const badHost = await new Promise((resolve, reject) => {
    const req = httpRequest(
      `${origin}/__spec/list`,
      {
        method: "POST",
        headers: {
          Host: "foreign.test",
          Origin: origin,
          "Content-Type": "application/json",
          Authorization: "Bearer admin",
        },
      },
      (response) => {
        response.resume();
        response.on("end", () => resolve(response.statusCode));
      },
    );
    req.on("error", reject);
    req.end("{}");
  });
  assert.equal(badHost, 403);
  assert.equal(seen.length, 0);
  const allowed = await request({ token: "admin" });
  assert.equal(allowed.status, 200);
  assert.equal(allowed.headers.get("cache-control"), "no-store");
  assert.deepEqual(await allowed.json(), { ok: true });
  assert.deepEqual(seen, [["list", {}, { uid: "uid", token: "admin" }]]);
});

test("compare reports local/remote hashes; live and test comparisons never enable writes", async () => {
  const f = fixture();
  for (const env of ["test", "live"]) {
    const result = await f.ops("compare", { env }, caller);
    assert.equal(result.contentVersion, "6.1");
    assert.equal(result.tables[0].state, "different");
    assert.equal(result.tables[0].remoteHash, "old-hash");
  }
  assert.deepEqual(f.calls, [{ writes: false }, { writes: false }]);
  await assert.rejects(f.ops("compare", { env: "../live" }, caller), {
    code: "INVALID_INPUT",
  });
  for (const action of ["plan", "publish"])
    await assert.rejects(f.ops(action, { env: "live" }, caller), {
      code: "READ_ONLY",
    });
  assert.equal(f.applied(), 0);
});

test("publication returns exact cell delta; plan is UID-bound, version-confirmed and single-use", async () => {
  const f = fixture();
  const preview = await f.ops("plan", { env: "test" }, caller);
  assert.deepEqual(preview.diffs, [
    { table: "Card", rowId: "1", column: "maxHp", before: "3", after: "7" },
  ]);
  assert.equal(preview.writes, 1);
  assert.equal(preview.plan, undefined);
  const input = { env: "test", planId: preview.planId, confirmation: "6.2" };
  await assert.rejects(
    f.ops("publish", input, { ...caller, uid: "another-admin" }),
    { code: "CONFLICT" },
  );
  await assert.rejects(
    f.ops("publish", { ...input, confirmation: "6.3" }, caller),
    { code: "INVALID_INPUT" },
  );
  assert.equal(f.applied(), 0);
  assert.deepEqual(await f.ops("publish", input, caller), {
    published: "6.2",
    env: "test",
    verifiedTables: 1,
  });
  await assert.rejects(f.ops("publish", input, caller), { code: "CONFLICT" });
  assert.equal(f.applied(), 1);
  assert.deepEqual(f.calls, [{ writes: false }, { writes: true }]);
});

test("row repairs and deletions are reviewed even when the runtime blob is unchanged", async () => {
  const f = fixture();
  const plan = await f.publisher.makePlan("test");
  const rowPath = `${base}/envs/test/specs/Card/rows/1`;
  const deletedPath = `${base}/envs/test/specs/Card/rows/2`;
  plan.backups[0].fields.payload = stringValue(
    JSON.stringify(f.snapshot.tables.Card.matrix),
  );
  plan.backups.push({
    name: rowPath,
    fields: { id: { integerValue: "1" }, maxHp: { integerValue: "999" } },
  });
  plan.backups.push({
    name: deletedPath,
    fields: { id: { integerValue: "2" }, maxHp: { integerValue: "4" } },
  });
  plan.writes = [
    {
      update: {
        name: rowPath,
        fields: { id: { integerValue: "1" }, maxHp: { integerValue: "7" } },
      },
    },
    { delete: deletedPath },
  ];
  const preview = await f.ops("plan", { env: "test" }, caller);
  assert.deepEqual(preview.diffs, [
    {
      table: "Card / rows (콘솔 미러)",
      rowId: "1",
      column: "maxHp",
      before: "999",
      after: "7",
    },
    {
      table: "Card / rows (콘솔 미러)",
      rowId: "2",
      column: "id",
      before: "2",
      after: null,
    },
    {
      table: "Card / rows (콘솔 미러)",
      rowId: "2",
      column: "maxHp",
      before: "4",
      after: null,
    },
  ]);
});

test("expired and replaced plans cannot publish", async () => {
  const f = fixture();
  const first = await f.ops("plan", { env: "test" }, caller);
  const second = await f.ops("plan", { env: "test" }, caller);
  await assert.rejects(
    f.ops(
      "publish",
      { env: "test", planId: first.planId, confirmation: first.to },
      caller,
    ),
    { code: "CONFLICT" },
  );
  f.advance(600001);
  await assert.rejects(
    f.ops(
      "publish",
      { env: "test", planId: second.planId, confirmation: second.to },
      caller,
    ),
    { code: "CONFLICT" },
  );
  assert.equal(f.applied(), 0);
});

test("source changes invalidate preview and apply; uncertain results consume plan without retry", async () => {
  const f = fixture();
  f.setSource("changed");
  await assert.rejects(f.ops("plan", { env: "test" }, caller), {
    code: "CONFLICT",
  });
  f.setSource("original");
  const plan = await f.ops("plan", { env: "test" }, caller);
  f.setSource("changed");
  const input = { env: "test", planId: plan.planId, confirmation: plan.to };
  await assert.rejects(f.ops("publish", input, caller), {
    code: "PUBLISH_UNCONFIRMED",
  });
  await assert.rejects(f.ops("publish", input, caller), { code: "CONFLICT" });
  assert.equal(f.applied(), 1);
});

test("in-flight publish blocks CSV save and other publishes until completion", async () => {
  const f = fixture();
  let release;
  f.publisher.applyPlan = () =>
    new Promise((resolve) => {
      release = resolve;
    });
  const first = await f.ops("plan", { env: "test" }, caller);
  const other = { ...caller, uid: "other" };
  const second = await f.ops("plan", { env: "test" }, other);
  const active = f.ops(
    "publish",
    { env: "test", planId: first.planId, confirmation: first.to },
    caller,
  );
  await assert.rejects(f.ops("save", {}, caller), { code: "CONFLICT" });
  await assert.rejects(
    f.ops(
      "publish",
      { env: "test", planId: second.planId, confirmation: second.to },
      other,
    ),
    { code: "CONFLICT" },
  );
  release({ published: "6.2" });
  await active;
  assert.deepEqual(await f.ops("save", {}, caller), { changedCells: 1 });
});

test("large publication is rejected when every delta cannot be displayed", async () => {
  const f = fixture();
  for (let i = 2; i < 1100; i++)
    f.snapshot.tables.Card.matrix.push([String(i), "7"]);
  await assert.rejects(f.ops("plan", { env: "test" }, caller), {
    code: "INVALID_INPUT",
  });
  assert.equal(f.applied(), 0);
});
