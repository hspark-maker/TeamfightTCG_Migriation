import {getAuth, UserRecord} from "firebase-admin/auth";
import {Timestamp} from "firebase-admin/firestore";
import {CallableRequest, HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {isKnownEnv} from "../save/environments";

type JsonValue = string | number | boolean | null | JsonValue[] | JsonRecord;
type JsonRecord = {[key: string]: JsonValue};

// Keep the projection explicit: server receipts, tokens and future private fields are not a wire contract.
const SAVE_FIELDS = [
  "schemaVersion", "revision", "updatedAt", "deviceId", "appVersion",
  "ownership", "deck", "cardGrowth", "rank", "albumReward", "adventure", "tutorial", "profile",
];
const CONTENT_FIELDS = [
  "major", "minor", "minAppMajor", "contentVersion", "nextMinor", "history", "tables", "updatedAt", "publishedAt",
];
const APP_FIELDS = [
  "minSupported", "latest", "storeUrlAndroid", "storeUrlIOS", "storeUrl",
  "noticeId", "noticeTitle", "noticeBody", "updatedAt",
];
const REPLAY_COUNTERS = [
  "settled", "replayOk", "replayFailed", "unavailable", "divergent", "outcomeMismatch", "hashMismatch",
] as const;

function scope(request: CallableRequest): string {
  if (!request.auth?.uid) throw new HttpsError("unauthenticated", "Sign-in is required.");
  if (request.auth.token?.admin !== true) throw new HttpsError("permission-denied", "Admin claim is required.");
  const env = request.data?.env;
  if (typeof env !== "string" || !isKnownEnv(env)) throw new HttpsError("invalid-argument", "Known env is required.");
  return env;
}

function playerUid(value: unknown): string {
  if (typeof value !== "string" || value.length === 0 || value.length > 128 ||
      /[\s/\\]/u.test(value) || Array.from(value).some((char) => char.charCodeAt(0) < 32 || char.charCodeAt(0) === 127) ||
      value === "." || value === "..") {
    throw new HttpsError("invalid-argument", "An exact UID of 1..128 characters without whitespace or slashes is required.");
  }
  return value;
}

function jsonValue(value: unknown): JsonValue {
  if (value === null || typeof value === "string" || typeof value === "boolean") return value;
  if (typeof value === "number") return Number.isFinite(value) ? value : null;
  if (value instanceof Timestamp) return value.toDate().toISOString();
  if (value instanceof Date) return value.toISOString();
  if (Array.isArray(value)) return value.map(jsonValue);
  if (typeof value === "object" && value !== null && Object.getPrototypeOf(value) === Object.prototype) {
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, jsonValue(item)]));
  }
  return null;
}

function project(data: Record<string, unknown> | undefined, fields: readonly string[]): JsonRecord | null {
  if (data === undefined) return null;
  return Object.fromEntries(fields.filter((field) => Object.prototype.hasOwnProperty.call(data, field))
    .map((field) => [field, jsonValue(data[field])]));
}

function accountView(user: UserRecord | null) {
  if (user === null) return null;
  return {
    uid: user.uid,
    email: user.email ?? null,
    displayName: user.displayName ?? null,
    disabled: user.disabled,
    createdAt: user.metadata.creationTime ? new Date(user.metadata.creationTime).toISOString() : "",
    lastSignInAt: user.metadata.lastSignInTime ? new Date(user.metadata.lastSignInTime).toISOString() : "",
    providers: user.providerData.map((provider) => provider.providerId),
  };
}

async function readAccount(uid: string): Promise<UserRecord | null> {
  try {
    return await getAuth().getUser(uid);
  } catch (error) {
    if (typeof error === "object" && error !== null && "code" in error && error.code === "auth/user-not-found") return null;
    throw error;
  }
}

/** Direct snapshots only. Gameplay getters may initialize/reset state, so must not be called here. */
export const adminDashboardOverview = onCall({enforceAppCheck: false}, async (request) => {
  const env = scope(request);
  const fetchedAtMs = Date.now();
  // One time anchor keeps all seven IDs consistent even when this request crosses UTC midnight.
  const days = Array.from({length: 7}, (_, offset) =>
    new Date(fetchedAtMs - offset * 86400000).toISOString().slice(0, 10));
  const refs = [
    `envs/${env}/specs/_index`, `envs/${env}/config/app`, `envs/${env}/config/battleReplay`,
    ...days.map((day) => `envs/${env}/telemetry/replayDaily/days/${day}`),
  ];
  const [content, appPolicy, replayConfig, ...replayDays] = await db.getAll(...refs.map((path) => db.doc(path)));
  const enabled = replayConfig.data()?.enabled;
  return {
    env,
    fetchedAtMs,
    content: project(content.data(), CONTENT_FIELDS),
    appPolicy: project(appPolicy.data(), APP_FIELDS),
    replay: {
      enabled: typeof enabled === "boolean" ? enabled : null,
      days: replayDays.map((snapshot, index) => {
        const data = snapshot.data();
        const counters = Object.fromEntries(REPLAY_COUNTERS.map((key) => {
          const value = data?.[key];
          return [key, typeof value === "number" && Number.isFinite(value) ? value : 0];
        }));
        return {day: days[index], exists: snapshot.exists, ...counters};
      }),
    },
  };
});

export const adminDashboardPlayer = onCall({enforceAppCheck: false}, async (request) => {
  const env = scope(request);
  const uid = playerUid(request.data?.uid);
  const fetchedAtMs = Date.now();
  const root = `envs/${env}/users/${uid}`;
  // Firestore getAll reads save and wallet together. Auth belongs to the shared project, not an env.
  const [[save, wallet], user] = await Promise.all([
    db.getAll(db.doc(`${root}/save/current`), db.doc(`${root}/wallet/current`)),
    readAccount(uid),
  ]);
  if (!save.exists && !wallet.exists && user === null) throw new HttpsError("not-found", "Player was not found.");
  return {
    env,
    uid,
    fetchedAtMs,
    account: accountView(user),
    save: project(save.data(), SAVE_FIELDS),
    wallet: project(wallet.data(), ["schemaVersion", "rev", "balances", "updatedAt"]),
  };
});
