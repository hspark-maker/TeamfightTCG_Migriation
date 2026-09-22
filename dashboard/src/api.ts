import { initializeApp } from "firebase/app";
import {
  browserSessionPersistence,
  connectAuthEmulator,
  getAuth,
  setPersistence,
} from "firebase/auth";
import {
  connectFunctionsEmulator,
  getFunctions,
  httpsCallable,
} from "firebase/functions";

export type Env = "test" | "live";
export type Json =
  | null
  | boolean
  | number
  | string
  | Json[]
  | { [key: string]: Json };
export type Doc = Record<string, Json>;
export type ReplayDay = {
  day: string;
  exists: boolean;
  settled: number;
  replayOk: number;
  replayFailed: number;
  unavailable: number;
  divergent: number;
  outcomeMismatch: number;
  hashMismatch: number;
};
export type Overview = {
  env: Env;
  fetchedAtMs: number;
  content: Doc | null;
  appPolicy: Doc | null;
  replay: { enabled: boolean | null; days: ReplayDay[] };
};
export type Player = {
  env: Env;
  uid: string;
  fetchedAtMs: number;
  account: {
    uid: string;
    email: string | null;
    displayName: string | null;
    disabled: boolean;
    createdAt: string;
    lastSignInAt: string;
    providers: string[];
  } | null;
  save: Doc | null;
  wallet: Doc | null;
};

export const projectId =
  import.meta.env.VITE_FIREBASE_PROJECT_ID || "bm-cardbattle";
export const emulator =
  import.meta.env.DEV && import.meta.env.VITE_USE_EMULATORS === "true";
const apiKey = import.meta.env.VITE_FIREBASE_API_KEY;
const appId = import.meta.env.VITE_FIREBASE_APP_ID;
export const configError =
  !apiKey || !appId
    ? "Firebase 연결 설정이 필요합니다. dashboard/README.md의 실행 방법을 확인하세요."
    : emulator &&
        (!projectId.startsWith("demo-") ||
          !["localhost", "127.0.0.1"].includes(location.hostname))
      ? "에뮬레이터는 로컬 주소와 demo- 프로젝트에서만 사용할 수 있습니다."
      : !emulator && projectId.startsWith("demo-")
        ? "로컬 테스트 설정으로 실제 서버에 연결할 수 없습니다."
        : null;

function connect() {
  const app = initializeApp({
    apiKey,
    appId,
    projectId,
    authDomain:
      import.meta.env.VITE_FIREBASE_AUTH_DOMAIN ||
      `${projectId}.firebaseapp.com`,
  });
  const auth = getAuth(app);
  const functions = getFunctions(app, "asia-northeast3");
  if (emulator) {
    connectAuthEmulator(auth, "http://127.0.0.1:19099", {
      disableWarnings: true,
    });
    connectFunctionsEmulator(functions, "127.0.0.1", 15099);
  }
  const ready = setPersistence(auth, browserSessionPersistence);
  return {
    auth,
    ready,
    overview: async (env: Env) =>
      (
        await httpsCallable<{ env: Env }, Overview>(
          functions,
          "adminDashboardOverview",
          { timeout: 30_000 },
        )({ env })
      ).data,
    player: async (env: Env, uid: string) =>
      (
        await httpsCallable<{ env: Env; uid: string }, Player>(
          functions,
          "adminDashboardPlayer",
          { timeout: 30_000 },
        )({ env, uid })
      ).data,
  };
}

export const api = configError ? null : connect();

export function errorMessage(error: unknown): string {
  const code =
    typeof error === "object" && error !== null && "code" in error
      ? String(error.code)
      : "";
  const messages: Record<string, string> = {
    "auth/invalid-credential": "이메일 또는 비밀번호를 확인해 주세요.",
    "auth/user-not-found": "이메일 또는 비밀번호를 확인해 주세요.",
    "auth/wrong-password": "이메일 또는 비밀번호를 확인해 주세요.",
    "auth/invalid-email": "올바른 이메일을 입력해 주세요.",
    "auth/user-disabled": "사용 중지된 계정입니다.",
    "auth/too-many-requests": "요청이 많습니다. 잠시 뒤 다시 시도해 주세요.",
    "auth/popup-closed-by-user": "로그인이 취소되었습니다.",
    "auth/popup-blocked": "로그인 팝업을 허용한 뒤 다시 시도해 주세요.",
    "auth/unauthorized-domain":
      "이 주소가 Firebase 로그인 허용 도메인에 등록되어 있지 않습니다.",
    "auth/operation-not-allowed":
      "Firebase에서 이 로그인 방식이 활성화되어 있지 않습니다.",
    "auth/network-request-failed":
      "네트워크 연결을 확인한 뒤 다시 시도해 주세요.",
    "functions/permission-denied":
      "관리자 권한이 필요합니다. 권한을 확인하고 다시 로그인해 주세요.",
    "functions/unauthenticated":
      "로그인이 만료되었습니다. 다시 로그인해 주세요.",
    "functions/not-found": "해당 UID의 계정과 게임 데이터를 찾지 못했습니다.",
    "functions/invalid-argument": "조회 환경과 UID를 확인해 주세요.",
    "functions/deadline-exceeded":
      "조회 시간이 초과되었습니다. 다시 시도해 주세요.",
    "functions/unavailable":
      "조회 서버에 연결할 수 없습니다. 연결 상태와 API 배포 상태를 확인해 주세요.",
    "functions/internal":
      "조회에 실패했습니다. 연결 상태와 API 배포 상태를 확인해 주세요.",
  };
  return (
    messages[code] || `요청을 완료하지 못했습니다.${code ? ` (${code})` : ""}`
  );
}

export const links = [
  {
    label: "Authentication",
    detail: "계정 · 로그인",
    href: `https://console.firebase.google.com/project/${projectId}/authentication/users`,
  },
  {
    label: "Firestore",
    detail: "게임 데이터 · cardbattle",
    href: `https://console.firebase.google.com/project/${projectId}/firestore/databases/cardbattle/data`,
  },
  {
    label: "Cloud Functions",
    detail: "게임 API · 실행 상태",
    href: `https://console.firebase.google.com/project/${projectId}/functions`,
  },
  {
    label: "Cloud Logging",
    detail: "오류 · 요청 로그",
    href: `https://console.cloud.google.com/logs/query?project=${projectId}`,
  },
  {
    label: "Battle Replay",
    detail: "전투 검증 · Cloud Run",
    href: `https://console.cloud.google.com/run/detail/asia-northeast3/battle-replay/metrics?project=${projectId}`,
  },
  {
    label: "Hosting",
    detail: "리소스 · 배포 이력",
    href: `https://console.firebase.google.com/project/${projectId}/hosting/sites/bm-cardbattle-assets`,
  },
];
