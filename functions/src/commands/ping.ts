import {onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {DATABASE_ID} from "../firebaseApp";
import {
  isKnownEnv,
  saveDocument,
  SCHEMA_VERSION,
} from "../save/saveDocument";

/**
 * 왕복 진단. 인증이 없어도 던지지 않는다 — 인증이 원인일 때
 * 그 사실 자체를 알려주지 못하면 진단 도구가 아니다.
 */
export const ping = onCall(async (request) => {
  const uid = request.auth?.uid ?? null;
  const env = String(request.data?.env ?? "test");

  let exists = false;
  let revision = 0;
  let readError: string | null = null;

  const envKnown = isKnownEnv(env);

  if (uid && envKnown) {
    try {
      const snapshot = await saveDocument(env, uid).get();
      exists = snapshot.exists;
      revision = Number(snapshot.data()?.revision ?? 0);
    } catch (error) {
      readError = error instanceof Error ? error.message : String(error);
    }
  }

  logger.info("ping", {uid, env, envKnown, exists, revision});

  // 진단 도구가 "정상"이라 답하면 안 되는 경우까지 ok 에 담는다.
  return {
    ok: envKnown && readError === null,
    envKnown,
    uid,
    env,
    database: DATABASE_ID,
    schemaVersion: SCHEMA_VERSION,
    exists,
    revision,
    readError,
  };
});
