import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests",
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
    },
  },
});
