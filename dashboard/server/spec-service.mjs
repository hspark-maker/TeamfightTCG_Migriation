import { createHash, randomUUID } from "node:crypto";
import { readFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { join, resolve, relative, isAbsolute } from "node:path";
import { tmpdir } from "node:os";
import { createCsvStore } from "./csv-store.mjs";

const PROJECT = "bm-cardbattle";
const DATABASE = `projects/${PROJECT}/databases/cardbattle/documents`;
const MAX_BODY = 2 * 1024 * 1024;
const PLAN_TTL = 10 * 60 * 1000;
const MAX_DIFFS = 2000;
const actions = new Set([
  "list",
  "read",
  "preview",
  "save",
  "compare",
  "plan",
  "publish",
]);
const fail = (code, message) => Object.assign(new Error(message), { code });

function unpack(value) {
  if (value == null || value.nullValue !== undefined) return null;
  if (value.stringValue !== undefined) return value.stringValue;
  if (value.integerValue !== undefined) return Number(value.integerValue);
  if (value.doubleValue !== undefined) return value.doubleValue;
  if (value.booleanValue !== undefined) return value.booleanValue;
  if (value.timestampValue !== undefined) return value.timestampValue;
  if (value.arrayValue) return (value.arrayValue.values || []).map(unpack);
  if (value.mapValue) return fields(value.mapValue.fields);
  return null;
}
const fields = (value = {}) =>
  Object.fromEntries(
    Object.entries(value).map(([key, item]) => [key, unpack(item)]),
  );

function requestClient(token, allowWrites = false) {
  return async (resource, body) => {
    if (
      !resource.startsWith(`${DATABASE}/envs/`) &&
      resource !== `${DATABASE}:commit`
    ) {
      throw fail("INVALID_INPUT", "허용되지 않은 Firestore 경로입니다.");
    }
    if (body && (!allowWrites || resource !== `${DATABASE}:commit`)) {
      throw fail("READ_ONLY", "이 작업은 서버를 변경할 수 없습니다.");
    }
    const response = await fetch(
      `https://firestore.googleapis.com/v1/${resource}`,
      {
        method: body ? "POST" : "GET",
        headers: {
          Authorization: `Bearer ${token}`,
          "Content-Type": "application/json",
        },
        body: body ? JSON.stringify(body) : undefined,
        signal: AbortSignal.timeout(30_000),
      },
    );
    const result = await response.json();
    if (!response.ok) {
      const error = fail(
        response.status === 409 || response.status === 412
          ? "CONFLICT"
          : "REMOTE_ERROR",
        `Firestore ${response.status}: ${result.error?.message || response.statusText}`,
      );
      error.status = response.status;
      throw error;
    }
    return result;
  };
}

/** The saved plan stays on this process. The browser only receives its review summary and opaque ID. */
export function createSpecOperations({
  root,
  store,
  publisher,
  remote = requestClient,
  now = Date.now,
  remoteEnabled = true,
}) {
  const plans = new Map();
  let publishing = false;
  const requireEnv = (env) => {
    if (env !== "test" && env !== "live")
      throw fail("INVALID_INPUT", "test 또는 live 환경을 선택하세요.");
    if (!remoteEnabled)
      throw fail(
        "READ_ONLY",
        "로컬 에뮬레이터에서는 실제 서버 비교·발행을 실행하지 않습니다.",
      );
  };
  return async (action, input, caller) => {
    if (action === "list") {
      let bytes = { exists: false, hash: null };
      try {
        bytes = {
          exists: true,
          hash: createHash("sha256")
            .update(
              await readFile(join(root, "Assets/Resources/SpecData.bytes")),
            )
            .digest("hex"),
        };
      } catch (error) {
        if (error.code !== "ENOENT") throw error;
      }
      const listing = await store.list();
      const published = new Set(publisher.publishedNames());
      return {
        tables: listing.tables.map((table) => ({
          ...table,
          published: published.has(table.name),
        })),
        generated: { bytes, csvOnly: true },
      };
    }
    if (action === "read") return store.read(input.table);
    if (action === "preview") return store.preview(input);
    if (action === "save") {
      if (publishing)
        throw fail("CONFLICT", "서버 발행 중입니다. 완료 후 CSV를 저장하세요.");
      publishing = true;
      try {
        return await store.save(input);
      } finally {
        publishing = false;
      }
    }
    requireEnv(input.env);
    if (action === "compare") {
      const snapshot = publisher.localSnapshot();
      let index = null;
      try {
        index = fields(
          (
            await remote(caller.token)(
              `${DATABASE}/envs/${input.env}/specs/_index`,
            )
          ).fields,
        );
      } catch (error) {
        if (error.status !== 404) throw error;
      }
      return {
        env: input.env,
        contentVersion: index?.contentVersion ?? null,
        localMajor: snapshot.major,
        tables: snapshot.names.map((name) => {
          const local = snapshot.tables[name];
          const remoteHash = index?.tables?.[name]?.payloadHash ?? null;
          return {
            name,
            localRows: local.rows.length,
            localHash: local.payloadHash,
            remoteHash,
            state:
              remoteHash == null
                ? "unpublished"
                : remoteHash === local.payloadHash
                  ? "same"
                  : "different",
          };
        }),
      };
    }
    if (input.env !== "test")
      throw fail(
        "READ_ONLY",
        "발행은 test 환경만 지원합니다. live는 정식 릴리즈 절차를 사용하세요.",
      );
    if (action === "plan") {
      for (const [id, entry] of plans)
        if (entry.expiresAtMs <= now() || entry.uid === caller.uid)
          plans.delete(id);
      if (plans.size >= 20)
        throw fail(
          "BUSY",
          "대기 중인 발행 계획이 많습니다. 잠시 뒤 다시 시도하세요.",
        );
      const plan = await publisher.makePlan("test", remote(caller.token));
      const snapshot = publisher.localSnapshot();
      // makePlan protects its own source snapshot. A change after that must also invalidate its preview.
      if (
        JSON.stringify(publisher.snapshotSources()) !==
        JSON.stringify(plan.sources)
      ) {
        throw fail("CONFLICT", "비교 중 CSV가 바뀌었습니다. 다시 비교하세요.");
      }
      const diffs = [];
      let diffCount = 0;
      for (const change of plan.changes) {
        const table = snapshot.tables[change.table];
        const oldDoc = plan.backups.find(
          (doc) =>
            doc.name ===
            `${DATABASE}/envs/test/specs/${change.table}/blob/current`,
        );
        const oldPayload = oldDoc
          ? JSON.parse(fields(oldDoc.fields).payload)
          : [table.columns];
        const oldRows = new Map(
          oldPayload
            .slice(1)
            .map((row) => [
              String(row[0]),
              Object.fromEntries(
                oldPayload[0].map((col, i) => [col, String(row[i])]),
              ),
            ]),
        );
        const newRows = new Map(
          table.matrix
            .slice(1)
            .map((row) => [
              String(row[0]),
              Object.fromEntries(
                table.columns.map((col, i) => [col, String(row[i])]),
              ),
            ]),
        );
        for (const id of new Set([...oldRows.keys(), ...newRows.keys()])) {
          for (const column of table.columns) {
            const before = oldRows.get(id)?.[column] ?? null;
            const after = newRows.get(id)?.[column] ?? null;
            if (before === after) continue;
            diffCount++;
            if (diffs.length < MAX_DIFFS)
              diffs.push({
                table: change.table,
                rowId: id,
                column,
                before,
                after,
              });
          }
        }
      }
      // Console rows can drift independently of the runtime blob. Review their actual
      // mutations too, including repairs whose runtime payload has not changed.
      for (const write of plan.writes) {
        const document = write.update?.name || write.delete;
        const prefix = `${DATABASE}/envs/test/specs/`;
        if (!document?.startsWith(prefix)) continue;
        const match = document
          .slice(prefix.length)
          .match(/^([^/]+)\/rows\/([^/]+)$/);
        if (!match) continue;
        const [, table, rowId] = match;
        const backup = plan.backups.find((doc) => doc.name === document);
        const beforeFields = backup ? fields(backup.fields) : {};
        const afterFields = write.update ? fields(write.update.fields) : {};
        const display = (value) =>
          value == null
            ? null
            : typeof value === "object"
              ? JSON.stringify(value)
              : String(value);
        for (const column of new Set([
          ...Object.keys(beforeFields),
          ...Object.keys(afterFields),
        ])) {
          const before = display(beforeFields[column]);
          const after = display(afterFields[column]);
          if (before === after) continue;
          diffCount++;
          if (diffs.length < MAX_DIFFS)
            diffs.push({
              table: `${table} / rows (콘솔 미러)`,
              rowId,
              column,
              before,
              after,
            });
        }
      }
      // Do not authorize writes whose complete cell delta could not be reviewed here.
      if (diffCount > MAX_DIFFS)
        throw fail(
          "INVALID_INPUT",
          "변경 셀이 2,000개를 초과합니다. 이번 발행은 기존 릴리즈 도구에서 검토하세요.",
        );
      const planId = randomUUID();
      const expiresAtMs = now() + PLAN_TTL;
      plans.set(planId, { plan, uid: caller.uid, expiresAtMs });
      return {
        planId,
        expiresAtMs,
        env: "test",
        from: plan.previousVersion,
        to: plan.version,
        writes: plan.writes.length,
        changes: plan.changes,
        diffs,
        diffsTruncated: false,
      };
    }
    if (action === "publish") {
      const entry = plans.get(input.planId);
      if (!entry || entry.uid !== caller.uid || entry.expiresAtMs <= now()) {
        throw fail(
          "CONFLICT",
          "발행 미리보기가 만료되었거나 없습니다. 다시 비교하세요.",
        );
      }
      if (input.confirmation !== entry.plan.version)
        throw fail("INVALID_INPUT", "확인한 발행 버전이 일치하지 않습니다.");
      if (publishing) throw fail("CONFLICT", "다른 발행이 진행 중입니다.");
      plans.delete(input.planId);
      publishing = true;
      try {
        return await publisher.applyPlan(
          entry.plan,
          remote(caller.token, true),
        );
      } catch (error) {
        throw fail(
          "PUBLISH_UNCONFIRMED",
          `발행 결과를 확인하지 못했습니다. 자동 재시도하지 말고 서버 버전을 새로 조회하세요. ${String(error.message).slice(0, 250)}`,
        );
      } finally {
        publishing = false;
      }
    }
    throw fail("NOT_FOUND", "지원하지 않는 작업입니다.");
  };
}

function checkRequest(req) {
  const host = req.headers.host;
  const loopback = new Set(["127.0.0.1", "::1", "::ffff:127.0.0.1"]);
  if (
    !loopback.has(req.socket.remoteAddress) ||
    !/^(localhost|127\.0\.0\.1|\[::1\]):\d+$/.test(host || "")
  ) {
    throw fail("FORBIDDEN", "로컬 대시보드에서만 사용할 수 있습니다.");
  }
  if (req.headers.origin !== `http://${host}`)
    throw fail("FORBIDDEN", "대시보드와 요청 출처가 일치하지 않습니다.");
  if (
    req.method !== "POST" ||
    req.headers["content-type"]?.split(";")[0].trim() !== "application/json"
  ) {
    throw fail("INVALID_INPUT", "JSON POST 요청이 필요합니다.");
  }
}

async function jsonBody(req) {
  const chunks = [];
  let size = 0;
  for await (const chunk of req) {
    size += chunk.length;
    if (size > MAX_BODY)
      throw fail("INVALID_INPUT", "요청 크기가 너무 큽니다.");
    chunks.push(chunk);
  }
  try {
    const value = JSON.parse(Buffer.concat(chunks).toString("utf8"));
    if (!value || typeof value !== "object" || Array.isArray(value))
      throw new Error();
    return value;
  } catch {
    throw fail("INVALID_INPUT", "올바른 JSON 요청이 필요합니다.");
  }
}

export function createSpecMiddleware({ operations, verifyToken }) {
  return async (req, res, next) => {
    if (!req.url?.startsWith("/__spec/")) return next();
    res.setHeader("Cache-Control", "no-store");
    res.setHeader("X-Content-Type-Options", "nosniff");
    res.setHeader("Content-Type", "application/json; charset=utf-8");
    try {
      checkRequest(req);
      const action = req.url.slice("/__spec/".length);
      if (!actions.has(action))
        throw fail("NOT_FOUND", "지원하지 않는 작업입니다.");
      const bearer = req.headers.authorization?.match(/^Bearer (\S+)$/);
      if (!bearer) throw fail("UNAUTHENTICATED", "관리자 로그인이 필요합니다.");
      let caller;
      try {
        caller = await verifyToken(bearer[1]);
      } catch {
        throw fail(
          "UNAUTHENTICATED",
          "로그인이 만료되었거나 유효하지 않습니다. 다시 로그인하세요.",
        );
      }
      if (!caller?.uid || caller.admin !== true)
        throw fail("FORBIDDEN", "관리자 권한이 필요합니다.");
      const input = await jsonBody(req);
      const result = await operations(action, input, {
        uid: caller.uid,
        token: bearer[1],
      });
      res.end(JSON.stringify(result));
    } catch (error) {
      let code = error.code || "INTERNAL";
      let message = error.message || "작업을 완료하지 못했습니다.";
      if (code === "ERR_ASSERTION") {
        code = "VALIDATION_FAILED";
        if (message.includes("No unpublished table change"))
          message = "모든 CSV가 이미 서버에 반영되어 있습니다.";
        else if (message.includes("Schema migration"))
          message =
            "서버와 CSV의 콘텐츠 세대가 다릅니다. 정식 릴리즈 절차가 필요합니다.";
        else if (message.includes("Local inputs changed")) {
          code = "CONFLICT";
          message = "미리보기 이후 CSV가 바뀌었습니다. 다시 비교하세요.";
        }
      }
      const status =
        {
          UNAUTHENTICATED: 401,
          FORBIDDEN: 403,
          READ_ONLY: 403,
          NOT_FOUND: 404,
          CONFLICT: 409,
          INVALID_INPUT: 400,
          VALIDATION_FAILED: 400,
          BUSY: 429,
        }[code] || 500;
      res.statusCode = status;
      res.end(
        JSON.stringify({ error: { code, message: message.slice(0, 1000) } }),
      );
    }
  };
}

export function specPlugin({ root, projectId, emulator, fixtureDirectory }) {
  const require = createRequire(join(root, "functions/package.json"));
  const { initializeApp, getApps } = require("firebase-admin/app");
  const { getAuth } = require("firebase-admin/auth");
  const publisher = require(
    join(root, "functions/scripts/publish-all-csv-spec.js"),
  );
  if (emulator) {
    const rel = fixtureDirectory
      ? relative(resolve(tmpdir()), resolve(fixtureDirectory))
      : "..";
    if (
      !projectId.startsWith("demo-") ||
      !fixtureDirectory ||
      rel.startsWith("..") ||
      isAbsolute(rel) ||
      process.env.FIREBASE_AUTH_EMULATOR_HOST !== "127.0.0.1:19099"
    ) {
      throw new Error(
        "CSV 에뮬레이터는 demo 프로젝트, 로컬 Auth와 임시 CSV 폴더가 필요합니다.",
      );
    }
  } else if (
    projectId !== PROJECT ||
    process.env.FIREBASE_AUTH_EMULATOR_HOST ||
    fixtureDirectory
  ) {
    throw new Error("CSV 관리 서버의 Firebase 설정을 확인하세요.");
  }
  const name = `dashboard-specs-${projectId}`;
  const app =
    getApps().find((app) => app.name === name) ||
    initializeApp({ projectId }, name);
  const operations = createSpecOperations({
    root,
    store: createCsvStore({
      directory: emulator ? fixtureDirectory : join(root, "docs/SpecData"),
    }),
    publisher,
    remoteEnabled: !emulator,
  });
  const middleware = createSpecMiddleware({
    operations,
    verifyToken: (token) => getAuth(app).verifyIdToken(token),
  });
  return {
    name: "cardbattle-local-specs",
    configureServer(server) {
      server.middlewares.use(middleware);
    },
    configurePreviewServer(server) {
      server.middlewares.use(middleware);
    },
  };
}
