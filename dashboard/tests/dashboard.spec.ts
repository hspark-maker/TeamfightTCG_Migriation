import { test, expect, type Page, type Route } from "@playwright/test";
import { createRequire } from "node:module";
import { resolve } from "node:path";
import { randomUUID } from "node:crypto";

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
