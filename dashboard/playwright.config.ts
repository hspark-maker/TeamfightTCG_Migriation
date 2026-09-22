import { defineConfig } from "@playwright/test";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

const specFixtures = mkdtempSync(join(tmpdir(), "cardbattle-dashboard-specs-"));
writeFileSync(join(specFixtures, "Card_sheet.csv"), "번호,이름,생명력\r\nid,displayName,maxHp\r\nint,string,int\r\n1,테스트 카드,3\r\n2,다른 카드,4\r\n");

export default defineConfig({
  testDir: "./tests",
  testMatch: "**/*.spec.ts",
  fullyParallel: false,
  workers: 1,
  use: {
    baseURL: "http://127.0.0.1:5174",
    browserName: "chromium",
    channel: "chrome",
    trace: "retain-on-failure",
  },
  webServer: {
    command: "npx vite --host 127.0.0.1 --port 5174 --strictPort",
    url: "http://127.0.0.1:5174",
    reuseExistingServer: false,
    env: {
      VITE_FIREBASE_API_KEY: "demo-dashboard-key",
      VITE_FIREBASE_PROJECT_ID: "demo-dashboard",
      VITE_FIREBASE_APP_ID: "demo-dashboard-web",
      VITE_FIREBASE_AUTH_DOMAIN: "demo-dashboard.firebaseapp.com",
      VITE_USE_EMULATORS: "true",
      DASHBOARD_SPEC_FIXTURE_DIR: specFixtures,
    },
  },
});
