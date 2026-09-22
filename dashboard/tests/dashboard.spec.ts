import { test, expect, type Page, type Route } from "@playwright/test";
import { createRequire } from "node:module";
import { resolve } from "node:path";
import { randomUUID } from "node:crypto";
import type { MatchPeriod, MatchStatsData } from "../src/api";

const require = createRequire(resolve("../functions/package.json"));
const authHost = process.env.FIREBASE_AUTH_EMULATOR_HOST;
if (
  authHost !== "127.0.0.1:19099" ||
  !process.env.GCLOUD_PROJECT?.startsWith("demo-")
) {
  throw new Error(
    "Use firebase emulators:exec with demo-dashboard and local Auth at 127.0.0.1:19099.",
  );
}
const { initializeApp, deleteApp } = require("firebase-admin/app");
const { getAuth } = require("firebase-admin/auth");
const adminApp = initializeApp(
  { projectId: "demo-dashboard" },
  "dashboard-browser-tests",
);
const auth = getAuth(adminApp);
const password = randomUUID() + "Aa1!";
const adminEmail = `admin-${randomUUID()}@example.test`;
const memberEmail = `member-${randomUUID()}@example.test`;

test.beforeAll(async () => {
  const admin = await auth.createUser({ email: adminEmail, password });
  await auth.setCustomUserClaims(admin.uid, { admin: true });
  await auth.createUser({ email: memberEmail, password });
});
test.afterAll(async () => {
  await deleteApp(adminApp);
});

const overview = (env: string) => ({
  env,
  fetchedAtMs: Date.now(),
  content: {
    major: 6,
    minor: 12,
    contentVersion: env === "test" ? "6.12" : "5.4",
    minAppMajor: 6,
    tables: { Card: { revision: 8, payloadHash: "a".repeat(64) } },
  },
  appPolicy: {
    latest: "1.2.3",
    minSupported: "1.0.0",
    noticeTitle: "새로운 시즌",
    noticeBody: "새 시즌이 시작되었습니다.",
  },
  replay: {
    enabled: true,
    days: Array.from({ length: 7 }, (_, index) => ({
      day: new Date(Date.now() - index * 86400000).toISOString().slice(0, 10),
      exists: index < 5,
      settled: index < 5 ? 100 + index * 11 : 0,
      replayOk: 90,
      divergent: 2,
      replayFailed: 3,
      unavailable: 7,
      outcomeMismatch: 1,
      hashMismatch: 2,
    })),
  },
});
const player = (env: string, uid: string) => ({
  env,
  uid,
  fetchedAtMs: Date.now(),
  account: {
    uid,
    email: "player@example.test",
    displayName: "테스트 유저",
    disabled: false,
    createdAt: "2026-01-01T00:00:00Z",
    lastSignInAt: "2026-09-21T10:00:00Z",
    providers: ["password"],
  },
  save: {
    revision: 15,
    updatedAt: "2026-09-21T10:00:00Z",
    profile: { nickname: `유저-${uid}`, accountExp: 321 },
    ownership: { cardIds: [1, 2, 3, 4] },
    rank: { points: 200 },
    deck: {
      selectedSlot: 0,
      slots: [{ name: "나의 덱", cardIds: [1, 2, 3, 4, 5, 6] }],
    },
    adventure: { clearedNodeIds: ["node-1", "node-2"] },
    tutorial: { outgameCompleted: true },
  },
  wallet: { rev: 4, balances: { Gold: 12345, Shard: 123, CardDust: 70 } },
});
const fulfill = (route: Route, data: unknown) =>
  route.fulfill({ json: { result: data } });

// These records are explicit response fixtures; match statistics never contact Firestore here.
const dayText = (ms: number) => new Date(ms).toISOString().slice(0, 10);
const matches = (period: MatchPeriod = 7): MatchStatsData => {
  const now = Date.now();
  const today = Date.parse(`${dayText(now)}T00:00:00Z`);
  const fromMs = typeof period === "number" ? today - (period - 1) * 86400000 : Date.parse(`${period.startDate}T00:00:00Z`);
  const endDay = typeof period === "number" ? today : Date.parse(`${period.endDate}T00:00:00Z`);
  const toMs = Math.min(endDay + 86400000, now);
  const days = (endDay - fromMs) / 86400000 + 1;
  const retentionStart = now - 7 * 86400000;
  const detailAvailable = toMs > retentionStart;
  return {
    env: "test", days, fromMs, toMs,
    startDate: dayText(fromMs), endDate: dayText(endDay),
    detailFromMs: detailAvailable ? Math.max(fromMs, retentionStart) : null,
    detailToMs: detailAvailable ? toMs : null,
    detailLimited: fromMs < retentionStart,
    detailAvailable,
    fetchedAtMs: now, limit: 500, hasMore: true, sampleSize: 500,
    summary: {
      confirmed: 497, flagged: 2, other: 1, replayed: 8,
      averageTurns: 12.5, turnSamples: 8,
      aiWins: 4, aiLosses: 2, aiDraws: 2, aiUnknown: 489,
    },
    modes: [
      { mode: "ai", total: 495, confirmed: 493, flagged: 1, wins: 3, losses: 1, draws: 2, unknown: 489 },
      { mode: "adventure", total: 2, confirmed: 2, flagged: 0, wins: 1, losses: 1, draws: 0, unknown: 0 },
      { mode: "pvp", total: 2, confirmed: 1, flagged: 1, wins: 0, losses: 0, draws: 0, unknown: 0 },
      { mode: "unknown", total: 1, confirmed: 1, flagged: 0, wins: 0, losses: 0, draws: 0, unknown: 1 },
    ],
    daily: Array.from({ length: days }, (_, index) => ({
      day: dayText(fromMs + index * 86400000),
      exists: index % 7 !== 6, settled: index % 7 === 6 ? null : index === 0 ? 0 : 70 + (index % 11) * 13,
    })),
    recent: [
      { id: "fixture-confirmed", mode: "ai", status: "confirmed", settledAtMs: now, reason: null, turns: null, outcome: "win" },
      { id: "fixture-flagged", mode: "pvp", status: "flagged", settledAtMs: now - 60000, reason: "hash-mismatch", turns: null, outcome: "pvp" },
      { id: "fixture-unknown", mode: "unknown", status: "other", settledAtMs: now - 120000, reason: null, turns: null, outcome: "unknown" },
    ],
  };
};

async function routes(page: Page) {
  await page.route("**/adminDashboardOverview", (route) =>
    fulfill(route, overview(route.request().postDataJSON().data.env)),
  );
  await page.route("**/adminDashboardPlayer", (route) => {
    const { env, uid } = route.request().postDataJSON().data;
    if (uid === "missing")
      return route.fulfill({
        status: 404,
        json: { error: { status: "NOT_FOUND", message: "missing" } },
      });
    return fulfill(route, player(env, uid));
  });
}
async function login(page: Page, email = adminEmail) {
  await page.goto("/");
  await page.getByLabel("이메일", { exact: true }).fill(email);
  await page.getByLabel("비밀번호", { exact: true }).fill(password);
  await page.getByRole("button", { name: "로그인", exact: true }).click();
}

test("admin signs in, sees real contract values, searches, switches env, and signs out", async ({
  page,
}) => {
  await routes(page);
  await login(page);
  await expect(page.getByText("6.12", { exact: true })).toBeVisible();
  await page
    .getByRole("button", { name: "콘텐츠 · 버전", exact: true })
    .click();
  await expect(
    page.getByRole("cell", { name: "Card", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "전투 검증", exact: true }).click();
  await expect(
    page.getByRole("columnheader", { name: "서비스 불가", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "유저 조회", exact: true }).click();
  await page.getByLabel("Firebase UID", { exact: true }).fill("player-one");
  await page
    .getByRole("button", { name: "유저 조회", exact: true })
    .last()
    .click();
  await expect(
    page.getByRole("heading", { name: "유저-player-one", exact: true }),
  ).toBeVisible();
  await expect(page.getByText("12,345", { exact: true })).toBeVisible();
  await page.getByLabel("Firebase UID", { exact: true }).fill("missing");
  await page
    .getByRole("button", { name: "유저 조회", exact: true })
    .last()
    .click();
  await expect(page.getByRole("alert")).toContainText("찾지 못했습니다");
  await expect(page.getByText("12,345", { exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "LIVE", exact: true }).click();
  await expect(page.getByText("5.4", { exact: true })).toBeVisible();
  await expect(page.getByText("6.12", { exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "로그아웃", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "관리자 로그인", exact: true }),
  ).toBeVisible();
  await expect(page.getByText("5.4", { exact: true })).toHaveCount(0);
});

test("non-admin can authenticate but never requests dashboard data", async ({
  page,
}) => {
  const requests: string[] = [];
  page.on("request", (request) => {
    if (request.url().includes("/adminDashboard")) requests.push(request.url());
  });
  await routes(page);
  await login(page, memberEmail);
  await expect(
    page.getByRole("heading", { name: "관리자 권한이 필요합니다" }),
  ).toBeVisible();
  expect(requests).toEqual([]);
  await expect(page.getByRole("navigation")).toHaveCount(0);
});

test("a delayed old environment response cannot overwrite the new environment", async ({
  page,
}) => {
  let release: () => void = () => {};
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  let entered: () => void = () => {};
  const started = new Promise<void>((resolve) => {
    entered = resolve;
  });
  await routes(page);
  await page.route("**/adminDashboardOverview", async (route) => {
    const env = route.request().postDataJSON().data.env;
    if (env === "test") {
      entered();
      await gate;
    }
    await fulfill(route, overview(env));
  });
  await login(page);
  await started;
  await page.getByRole("button", { name: "LIVE", exact: true }).click();
  await expect(page.getByText("5.4", { exact: true })).toBeVisible();
  release();
  await expect(page.getByText("6.12", { exact: true })).toHaveCount(0);
});

test("failed overview can retry and missing telemetry is not shown as measured zero", async ({
  page,
}) => {
  await routes(page);
  let failing = true;
  await page.route("**/adminDashboardOverview", (route) => {
    if (failing)
      return route.fulfill({
        status: 500,
        json: { error: { status: "INTERNAL", message: "test" } },
      });
    const data = overview("test");
    data.replay.days = data.replay.days.map((day) => ({
      ...day,
      exists: false,
      settled: 0,
    }));
    return fulfill(route, data);
  });
  await login(page);
  await expect(page.getByRole("alert")).toBeVisible();
  failing = false;
  await page.getByRole("button", { name: "다시 시도", exact: true }).click();
  await expect(page.getByText("6.12", { exact: true })).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "아직 집계 데이터가 없습니다" }),
  ).toBeVisible();
});

test("desktop and mobile layouts contain content without page overflow", async ({
  page,
}) => {
  await routes(page);
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/");
  await page.screenshot({
    path: "../.codex_tmp/dashboard-login.png",
    fullPage: true,
  });
  await login(page);
  await expect(page.getByText("6.12", { exact: true })).toBeVisible();
  await page.screenshot({
    path: "../.codex_tmp/dashboard-overview.png",
    fullPage: true,
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect
    .poll(() =>
      page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),
    )
    .toBe(true);
  await page.screenshot({
    path: "../.codex_tmp/dashboard-mobile.png",
    fullPage: true,
  });
});

async function openSpecs(page: Page) {
  await routes(page);
  await login(page);
  await page.getByRole("button", { name: "CSV · SpecData", exact: true }).click();
  await page.getByRole("button", { name: /Card 2행 · 3열/ }).click();
  await expect(page.getByLabel("Card 1 maxHp", { exact: true })).toBeVisible();
}

// These requests reach the real local middleware and real Auth emulator.
// playwright.config.ts restricts CSV persistence to its OS temporary fixture directory.
async function localSpec(page: Page, action: string, body: object) {
  return page.evaluate(async ({ action, body }) => {
    const modulePath = "/src/api.ts";
    const { api } = await import(modulePath);
    const token = await api.auth.currentUser.getIdToken();
    const response = await fetch(`/__spec/${action}`, {
      method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
      body: JSON.stringify(body),
    });
    return { status: response.status, data: await response.json() };
  }, { action, body });
}

test("CSV fixture changes require preview and persist through the real local API; invalid integers cannot save", async ({ page }) => {
  await openSpecs(page);
  const before = await localSpec(page, "read", { table: "Card" });
  expect(before.status).toBe(200);
  expect(before.data.rows).toHaveLength(2);
  const original = before.data.rows[0].cells[2];
  const next = String(Number(original) + 1);
  const hp = page.getByLabel("Card 1 maxHp", { exact: true });
  await hp.fill(next);
  await expect(page.getByRole("button", { name: "검토한 변경 저장", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "변경 검토 · 1개", exact: true }).click();
  await expect(page.getByRole("region", { name: "CSV 저장 전 검토" })).toBeVisible();
  const previewOnly = await localSpec(page, "read", { table: "Card" });
  expect(previewOnly.data.hash).toBe(before.data.hash);
  await page.getByRole("button", { name: "검토한 변경 저장", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("로컬 CSV에 저장했습니다");
  const saved = await localSpec(page, "read", { table: "Card" });
  expect(saved.data.rows[0].cells[2]).toBe(next);
  expect(saved.data.hash).not.toBe(before.data.hash);
  await page.getByRole("button", { name: "다시 읽기", exact: true }).click();
  await expect(hp).toHaveValue(next);

  await hp.fill("3.5");
  await page.getByRole("button", { name: "변경 검토 · 1개", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("32비트 정수");
  await expect(page.getByRole("button", { name: "검토한 변경 저장", exact: true })).toHaveCount(0);
  expect((await localSpec(page, "read", { table: "Card" })).data.hash).toBe(saved.data.hash);

  // Restore this temporary fixture through the same reviewed UI flow.
  await hp.fill(original);
  await page.getByRole("button", { name: "변경 검토 · 1개", exact: true }).click();
  await page.getByRole("button", { name: "검토한 변경 저장", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("로컬 CSV에 저장했습니다");
  expect((await localSpec(page, "read", { table: "Card" })).data.hash).toBe(before.data.hash);
});

test("unsaved CSV survives canceled navigation, environment switch, logout, and same-user token refresh", async ({ page }) => {
  await openSpecs(page);
  const name = page.getByLabel("Card 1 displayName", { exact: true });
  await name.fill("보존할 CSV 초안");
  for (const target of ["종합 현황", "LIVE", "로그아웃", "다시 읽기"]) {
    let message = "";
    page.once("dialog", async (dialog) => { message = dialog.message(); await dialog.dismiss(); });
    await page.getByRole("button", { name: target, exact: true }).click();
    await expect(name).toHaveValue("보존할 CSV 초안");
    expect(message).toContain("저장하지 않은");
  }
  await page.evaluate(async () => {
    const modulePath = "/src/api.ts";
    const { api } = await import(modulePath);
    await api.auth.currentUser.getIdToken(true);
  });
  await expect(name).toHaveValue("보존할 CSV 초안");
  await page.getByRole("button", { name: "변경 검토 · 1개", exact: true }).click();
  await expect(page.getByRole("region", { name: "CSV 저장 전 검토" })).toContainText("보존할 CSV 초안");
});

test("local CSV middleware rejects missing authentication and non-admin emulator users", async ({ page }) => {
  await page.goto("/");
  const unauthenticated = await page.evaluate(async () => {
    const response = await fetch("/__spec/list", { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}" });
    return { status: response.status, data: await response.json() };
  });
  expect(unauthenticated.status).toBe(401);
  expect(unauthenticated.data.error.code).toBe("UNAUTHENTICATED");
  await login(page, memberEmail);
  await expect(page.getByRole("heading", { name: "관리자 권한이 필요합니다" })).toBeVisible();
  const forbidden = await localSpec(page, "list", {});
  expect(forbidden.status).toBe(403);
  expect(forbidden.data.error.code).toBe("FORBIDDEN");
});

test("remote comparison is emulator-blocked; explicitly mocked plan review needs exact version and confirmation", async ({ page }) => {
  await openSpecs(page);
  await page.getByRole("button", { name: "서버 비교 · 발행", exact: true }).click();
  await page.getByRole("button", { name: "서버와 비교", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("로컬 에뮬레이터에서는 실제 서버 비교·발행을 실행하지 않습니다");
  // Remote Firestore operations are deliberately mocked here; no live or test server writes.
  await page.route("**/__spec/compare", (route) => route.fulfill({ json: {
    env: route.request().postDataJSON().env, contentVersion: "6.12", localMajor: 6,
    tables: [{ name: "Card", localRows: 2, localHash: "a".repeat(64), remoteHash: "b".repeat(64), state: "different" }],
  } }));
  await page.route("**/__spec/plan", (route) => route.fulfill({ json: {
    planId: "browser-review-fixture", expiresAtMs: Date.now() + 600000, env: "test", from: "6.12", to: "6.13", writes: 3,
    changes: [{ table: "Card", oldRows: 2, rows: 2, revision: 9, payloadHash: "a".repeat(64) }],
    diffs: [{ table: "Card", rowId: "1", column: "maxHp", before: "3", after: "4" }], diffsTruncated: false,
  } }));
  let publishes = 0;
  await page.route("**/__spec/publish", (route) => {
    publishes++;
    expect(route.request().postDataJSON()).toEqual({ env: "test", planId: "browser-review-fixture", confirmation: "6.13" });
    return route.fulfill({ json: { published: "6.13", env: "test", verifiedTables: 1 } });
  });
  await page.getByRole("button", { name: "서버와 비교", exact: true }).click();
  await expect(page.getByText("변경 있음", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "TEST 발행 계획 만들기", exact: true }).click();
  await expect(page.getByRole("region", { name: "서버 발행 계획" })).toContainText("6.12 → 6.13");
  const publish = page.getByRole("button", { name: "TEST 서버에 발행", exact: true });
  await expect(publish).toBeDisabled();
  await page.getByLabel("발행 버전", { exact: false }).fill("6.13");
  page.once("dialog", (dialog) => dialog.dismiss());
  await publish.click();
  expect(publishes).toBe(0);
  page.once("dialog", (dialog) => dialog.accept());
  await publish.click();
  await expect(page.getByRole("status")).toContainText("TEST 콘텐츠 6.13 발행을 완료했습니다");
  expect(publishes).toBe(1);
  await page.getByRole("button", { name: "LIVE", exact: true }).click();
  await page.getByRole("button", { name: "CSV · SpecData", exact: true }).click();
  await page.getByRole("button", { name: "서버 비교 · 발행", exact: true }).click();
  await expect(page.getByText("LIVE는 비교만 지원합니다.", { exact: false })).toBeVisible();
  await expect(page.getByRole("button", { name: "TEST 발행 계획 만들기", exact: true })).toBeDisabled();
});

test("CSV editor and change preview fit desktop and mobile with internal grid scrolling", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await openSpecs(page);
  await page.getByLabel("Card 1 maxHp", { exact: true }).fill("8");
  await page.getByRole("button", { name: "변경 검토 · 1개", exact: true }).click();
  await expect(page.getByRole("region", { name: "CSV 저장 전 검토" })).toBeVisible();
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: "../.codex_tmp/dashboard-specs-desktop.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(await page.locator(".spec-grid").evaluate((element) => element.scrollWidth > element.clientWidth)).toBe(true);
  await page.screenshot({ path: "../.codex_tmp/dashboard-specs-mobile.png", fullPage: true });
});

async function openMatches(page: Page) {
  await routes(page);
  await login(page);
  await page.getByRole("button", { name: "매치 통계", exact: true }).click();
}

test("match response fixtures show capped statistics, exclude draws and unknown outcomes from win rate, and fit desktop/mobile", async ({ page }) => {
  const data = matches();
  await page.route("**/adminDashboardMatches", (route) => {
    expect(route.request().postDataJSON().data).toEqual({ env: "test", days: 7 });
    return fulfill(route, data);
  });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await openMatches(page);
  const metrics = page.getByLabel("매치 상세 분석", { exact: true });
  const winRate = metrics.locator("article").filter({ hasText: "AI전 승률 · 모험 포함" });
  await expect(winRate.locator("strong")).toHaveText("66.7%");
  await expect(winRate).toContainText("4승 / 승패 확인 6건");
  await expect(winRate).toContainText("무승부 2 · 미확인 489 제외");
  await expect(metrics.locator("article").filter({ hasText: "평균 전투 턴" }).locator("strong")).toHaveText("12.5턴");
  await expect(metrics.locator("article").filter({ hasText: "확인 필요한 매치" })).toContainText("정산 확인 497 · 기타 1");
  await expect(page.getByRole("status")).toContainText("최신 500건만 분석한 결과");

  const daily = page.getByRole("list", { name: "날짜별 정산 건수" });
  await expect(daily.getByRole("listitem", { name: /UTC: 0건$/ })).toHaveCount(1);
  await expect(daily.getByRole("listitem", { name: /UTC: 집계 없음$/ })).toHaveCount(1);
  const modes = page.getByRole("region", { name: "모드별 매치 통계 표" });
  await expect(modes.getByRole("row").filter({ has: page.getByRole("rowheader", { name: "AI 대전", exact: true }) })).toContainText("75.0%");
  await expect(modes.getByRole("row").filter({ has: page.getByRole("rowheader", { name: "PvP", exact: true }) }).getByRole("cell").nth(3)).toHaveText("—");
  const recent = page.getByRole("region", { name: "최근 매치 목록 표" });
  const flagged = recent.getByRole("row").filter({ hasText: "fixture-flagged" });
  await expect(flagged).toContainText("확인 필요");
  await expect(flagged).toContainText("hash-mismatch");
  await expect(flagged.getByRole("cell").nth(5)).toHaveText("—");
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: "../.codex_tmp/dashboard-matches-desktop.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(await recent.evaluate((element) => element.scrollWidth > element.clientWidth)).toBe(true);
  await page.screenshot({ path: "../.codex_tmp/dashboard-matches-mobile.png", fullPage: true });
});

test("empty match response fixtures keep missing aggregate days and unknown win rate distinct from measured zero", async ({ page }) => {
  const data = matches();
  data.hasMore = false;
  data.sampleSize = 0;
  data.summary = { confirmed: 0, flagged: 0, other: 0, replayed: 0, averageTurns: null, turnSamples: 0, aiWins: 0, aiLosses: 0, aiDraws: 0, aiUnknown: 0 };
  data.daily = data.daily.map((day) => ({ ...day, exists: false, settled: null }));
  data.modes = [];
  data.recent = [];
  await page.route("**/adminDashboardMatches", (route) => fulfill(route, data));
  await openMatches(page);
  await expect(page.getByRole("heading", { name: "이 기간에 조회된 매치 상세가 없습니다" })).toBeVisible();
  const metrics = page.getByLabel("매치 상세 분석", { exact: true });
  await expect(metrics.locator("article").filter({ hasText: "AI전 승률 · 모험 포함" }).locator("strong")).toHaveText("—");
  await expect(metrics.locator("article").filter({ hasText: "평균 전투 턴" }).locator("strong")).toHaveText("—");
  const daily = page.getByRole("list", { name: "날짜별 정산 건수" });
  await expect(daily.getByRole("listitem", { name: /집계 없음$/ })).toHaveCount(7);
  await expect(daily.getByRole("listitem", { name: /UTC: 0건$/ })).toHaveCount(0);
  await expect(page.getByRole("region", { name: "최근 매치 목록 표" })).toHaveCount(0);
});

test("match period requests discard delayed old fixtures, show errors without stale values, and retry explicitly", async ({ page }) => {
  let release: () => void = () => {};
  const gate = new Promise<void>((resolve) => { release = resolve; });
  let entered: () => void = () => {};
  const started = new Promise<void>((resolve) => { entered = resolve; });
  let failing = false;
  let completedOld = 0;
  const requests: number[] = [];
  await page.route("**/adminDashboardMatches", async (route) => {
    const { env, days } = route.request().postDataJSON().data;
    expect(env).toBe("test");
    requests.push(days);
    // React StrictMode may mount twice in development. Hold every old request,
    // including the active mount, so the race assertion cannot pass trivially.
    if (days === 7) {
      entered();
      await gate;
      await fulfill(route, matches(7));
      completedOld++;
      return;
    }
    if (failing) {
      await route.fulfill({ status: 500, json: { error: { status: "INTERNAL", message: "fixture failure" } } });
      return;
    }
    const data = matches(days);
    data.hasMore = false;
    data.sampleSize = 17;
    await fulfill(route, data);
  });
  await openMatches(page);
  await started;
  await page.getByRole("button", { name: "오늘", exact: true }).click();
  await expect(page.getByText("상세 분석 17건 / 최대 500건", { exact: true })).toBeVisible();
  release();
  await expect.poll(() => completedOld).toBe(requests.filter((days) => days === 7).length);
  await expect(page.getByRole("button", { name: "오늘", exact: true })).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByText("상세 분석 500건 / 최대 500건", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("list", { name: "날짜별 정산 건수" }).getByRole("listitem")).toHaveCount(1);
  failing = true;
  await page.getByRole("button", { name: "매치 새로고침", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("조회에 실패했습니다");
  await expect(page.getByLabel("매치 상세 분석", { exact: true })).toHaveCount(0);
  failing = false;
  await page.getByRole("button", { name: "다시 시도", exact: true }).click();
  await expect(page.getByText("상세 분석 17건 / 최대 500건", { exact: true })).toBeVisible();
  await expect(page.getByRole("alert")).toHaveCount(0);
  expect(requests.slice(requests.indexOf(1))).toEqual([1, 1, 1]);
});

test("all rolling and calendar presets send explicit UTC periods; local grouping makes no network request", async ({ page }) => {
  const requests: Record<string, unknown>[] = [];
  await page.route("**/adminDashboardMatches", (route) => {
    const request = route.request().postDataJSON().data;
    requests.push(request);
    return fulfill(route, matches(request.days ?? { startDate: request.startDate, endDate: request.endDate }));
  });
  await openMatches(page);
  await expect(page.getByRole("list", { name: "날짜별 정산 건수" })).toBeVisible();
  const periodButtons = page.getByRole("group", { name: "매치 조회 기간" });
  for (const [label, days] of [["오늘", 1], ["3일", 3], ["7일", 7], ["14일", 14], ["30일", 30], ["90일", 90]] as const) {
    await periodButtons.getByRole("button", { name: label, exact: true }).click();
    await expect.poll(() => requests.at(-1)).toEqual({ env: "test", days });
    await expect(page.getByRole("list", { name: "날짜별 정산 건수" }).getByRole("listitem")).toHaveCount(days);
  }

  const today = new Date(`${dayText(Date.now())}T00:00:00Z`);
  const yesterday = dayText(today.getTime() - 86400000);
  const monday = dayText(today.getTime() - ((today.getUTCDay() + 6) % 7) * 86400000);
  for (const [label, startDate, endDate] of [
    ["어제", yesterday, yesterday],
    ["이번 주", monday, dayText(today.getTime())],
    ["이번 달", dayText(today.getTime()).slice(0, 8) + "01", dayText(today.getTime())],
  ]) {
    await periodButtons.getByRole("button", { name: label, exact: true }).click();
    await expect.poll(() => requests.at(-1)).toEqual({ env: "test", startDate, endDate });
    await expect(page.getByText(`조회 기간 UTC ${startDate} ~ ${endDate}`, { exact: true })).toBeVisible();
  }
  const requestCount = requests.length;
  const units = page.getByRole("group", { name: "추이 집계 단위" });
  await units.getByRole("button", { name: "주별", exact: true }).click();
  await expect(units.getByRole("button", { name: "주별", exact: true })).toHaveAttribute("aria-pressed", "true");
  await units.getByRole("button", { name: "월별", exact: true }).click();
  await expect(units.getByRole("button", { name: "월별", exact: true })).toHaveAttribute("aria-pressed", "true");
  await expect(page.locator(".match-chart").getByRole("listitem")).toHaveCount(1);
  expect(requests).toHaveLength(requestCount);
  await periodButtons.getByRole("button", { name: "3일", exact: true }).click();
  await expect.poll(() => requests.at(-1)).toEqual({ env: "test", days: 3 });
  await expect(units.getByRole("button", { name: "월별", exact: true })).toHaveAttribute("aria-pressed", "true");
  await page.getByRole("button", { name: "매치 새로고침", exact: true }).click();
  await expect.poll(() => requests.length).toBe(requestCount + 2);
  await expect(units.getByRole("button", { name: "월별", exact: true })).toHaveAttribute("aria-pressed", "true");
});

test("custom dates apply explicitly and reject reversed, future and over-90-day ranges without requesting", async ({ page }) => {
  const requests: Record<string, unknown>[] = [];
  await page.route("**/adminDashboardMatches", (route) => {
    const request = route.request().postDataJSON().data;
    requests.push(request);
    return fulfill(route, matches(request.days ?? { startDate: request.startDate, endDate: request.endDate }));
  });
  await openMatches(page);
  await expect(page.getByRole("list", { name: "날짜별 정산 건수" })).toBeVisible();
  const previousRequests = requests.length;
  await page.getByRole("button", { name: "직접 지정", exact: true }).click();
  const now = Date.now();
  const today = dayText(now);
  const start = page.getByLabel("시작일", { exact: true });
  const end = page.getByLabel("종료일", { exact: true });
  const apply = page.getByRole("button", { name: "기간 적용", exact: true });
  for (const [startDate, endDate, message] of [
    [today, dayText(now - 86400000), "늦을 수"],
    [today, dayText(now + 86400000), "미래"],
    [dayText(now - 90 * 86400000), today, "90일"],
    ["", today, "입력하세요"],
  ]) {
    await start.fill(startDate);
    await end.fill(endDate);
    await apply.click();
    await expect(page.getByRole("alert")).toContainText(message);
    expect(requests).toHaveLength(previousRequests);
  }
  const startDate = dayText(now - 89 * 86400000);
  await start.fill(startDate);
  await end.fill(today);
  expect(requests).toHaveLength(previousRequests);
  await apply.click();
  await expect.poll(() => requests.at(-1)).toEqual({ env: "test", startDate, endDate: today });
  await expect(page.getByRole("list", { name: "날짜별 정산 건수" }).getByRole("listitem")).toHaveCount(90);
  await expect(page.getByRole("alert")).toHaveCount(0);
});

test("historical ranges show daily aggregates without unavailable detail metrics; partial group subtotals stay explicit", async ({ page }) => {
  const now = Date.now();
  const period = { startDate: dayText(now - 40 * 86400000), endDate: dayText(now - 10 * 86400000) };
  let requests = 0;
  await page.route("**/adminDashboardMatches", (route) => {
    requests++;
    const request = route.request().postDataJSON().data;
    const data = matches(request.days ?? { startDate: request.startDate, endDate: request.endDate });
    if (!data.detailAvailable) {
      data.hasMore = false;
      data.sampleSize = 0;
      data.recent = [];
      data.modes = [];
      data.summary = { confirmed: 0, flagged: 0, other: 0, replayed: 0, averageTurns: null, turnSamples: 0, aiWins: 0, aiLosses: 0, aiDraws: 0, aiUnknown: 0 };
    }
    return fulfill(route, data);
  });
  await openMatches(page);
  await expect(page.getByRole("list", { name: "날짜별 정산 건수" })).toBeVisible();
  await page.getByRole("button", { name: "직접 지정", exact: true }).click();
  await page.getByLabel("시작일", { exact: true }).fill(period.startDate);
  await page.getByLabel("종료일", { exact: true }).fill(period.endDate);
  await page.getByRole("button", { name: "기간 적용", exact: true }).click();
  await expect(page.getByRole("heading", { name: "선택 기간이 상세 보관 범위 밖입니다", exact: true })).toBeVisible();
  await expect(page.getByLabel("매치 상세 분석", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("region", { name: "최근 매치 목록 표" })).toHaveCount(0);
  await expect(page.getByRole("list", { name: "날짜별 정산 건수" }).getByRole("listitem")).toHaveCount(31);
  const countBeforeGrouping = requests;
  await page.getByRole("button", { name: "월별", exact: true }).click();
  const groups = page.locator(".match-chart").getByRole("listitem");
  const expected = new Map<string, { start: string; end: string; sum: number; known: number; total: number }>();
  for (const day of matches(period).daily) {
    const key = day.day.slice(0, 7);
    const group = expected.get(key) ?? { start: day.day, end: day.day, sum: 0, known: 0, total: 0 };
    group.end = day.day;
    group.total++;
    if (day.exists && day.settled !== null) { group.known++; group.sum += day.settled; }
    expected.set(key, group);
  }
  await expect(groups).toHaveCount(expected.size);
  let index = 0;
  for (const group of expected.values()) {
    const value = group.known === 0 ? "집계 없음" : group.known < group.total ? `소계 ${group.sum.toLocaleString("ko-KR")}건, 부분 집계 ${group.known}/${group.total}일` : `${group.sum.toLocaleString("ko-KR")}건`;
    await expect(groups.nth(index++)).toHaveAttribute("aria-label", `${group.start} ~ ${group.end} UTC: ${value}`);
  }
  expect(requests).toBe(countBeforeGrouping);
});

test("ninety daily bars scroll internally and date controls fit desktop and mobile", async ({ page }) => {
  await page.route("**/adminDashboardMatches", (route) => {
    const request = route.request().postDataJSON().data;
    return fulfill(route, matches(request.days ?? { startDate: request.startDate, endDate: request.endDate }));
  });
  await page.setViewportSize({ width: 1440, height: 1080 });
  await openMatches(page);
  await page.getByRole("button", { name: "90일", exact: true }).click();
  await expect(page.getByRole("list", { name: "날짜별 정산 건수" }).getByRole("listitem")).toHaveCount(90);
  await expect(page.getByLabel("매치 상세 분석", { exact: true })).toContainText("12.5");
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(await page.locator(".match-chart-scroll").evaluate((element) => element.scrollWidth > element.clientWidth)).toBe(true);
  await page.screenshot({ path: "../.codex_tmp/dashboard-match-ranges-desktop.png", fullPage: true });
  await page.getByRole("button", { name: "월별", exact: true }).click();
  await expect(page.locator(".match-chart").getByRole("listitem", { name: /소계 .*부분 집계/ }).first()).toBeVisible();
  const unitButtons = page.getByRole("group", { name: "추이 집계 단위" });
  await expect.poll(() => unitButtons.getByRole("button", { name: "월별", exact: true }).evaluate((element) => getComputedStyle(element).backgroundColor)).toBe("rgb(48, 43, 37)");
  await expect.poll(() => unitButtons.getByRole("button", { name: "일별", exact: true }).evaluate((element) => getComputedStyle(element).backgroundColor)).toBe("rgba(0, 0, 0, 0)");
  for (const partial of await page.locator(".match-chart-column.partial .match-bar").all()) {
    expect(await partial.evaluate((element) => getComputedStyle(element).backgroundImage)).toContain("repeating-linear-gradient");
  }
  await page.screenshot({ path: "../.codex_tmp/dashboard-match-ranges-monthly.png", fullPage: true });
  await page.getByRole("button", { name: "직접 지정", exact: true }).click();
  await page.getByRole("button", { name: "일별", exact: true }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByLabel("시작일", { exact: true })).toBeVisible();
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: "../.codex_tmp/dashboard-match-ranges-mobile.png", fullPage: true });
});
