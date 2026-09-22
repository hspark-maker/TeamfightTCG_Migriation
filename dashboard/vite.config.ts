import { defineConfig, loadEnv } from "vite";
import { homedir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { specPlugin } from "./server/spec-service.mjs";

export default defineConfig(({ command, mode }) => {
  const env = loadEnv(mode, process.cwd());
  if (command === "build" && (!env.VITE_FIREBASE_API_KEY || !env.VITE_FIREBASE_APP_ID ||
    !env.VITE_FIREBASE_PROJECT_ID || env.VITE_FIREBASE_PROJECT_ID.startsWith("demo-") ||
    env.VITE_USE_EMULATORS === "true")) {
    throw new Error("실제 Firebase 웹 앱 설정이 필요합니다. .env.local을 UTF-8(BOM 없음)으로 저장하세요.");
  }
  return {
    plugins: command === "build" ? [] : [specPlugin({
      root: fileURLToPath(new URL("..", import.meta.url)),
      projectId: env.VITE_FIREBASE_PROJECT_ID || "bm-cardbattle",
      emulator: env.VITE_USE_EMULATORS === "true",
      fixtureDirectory: process.env.DASHBOARD_SPEC_FIXTURE_DIR,
    })],
    build: {
      outDir: join(homedir(), "Desktop", "build", "CardBattleDashboard"),
      emptyOutDir: false,
    },
  };
});
