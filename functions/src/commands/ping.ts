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
  let documentSchemaVersion: unknown = null;
  let readError: string | null = null;

  const envKnown = isKnownEnv(env);

  if (uid && envKnown) {
    try {
      const snapshot = await saveDocument(env, uid).get();
      exists = snapshot.exists;
      revision = Number(snapshot.data()?.revision ?? 0);
      documentSchemaVersion = snapshot.data()?.schemaVersion ?? null;
    } catch (error) {
      readError = error instanceof Error ? error.message : String(error);
    }
  }

  logger.info("ping", {
    uid,
    env,
    envKnown,
    exists,
    revision,
    serverSchemaVersion: SCHEMA_VERSION,
    documentSchemaVersion,
  });

  // 진단 도구가 "정상"이라 답하면 안 되는 경우까지 ok 에 담는다.
  return {
    ok: uid !== null && envKnown && readError === null,
    envKnown,
    uid,
    env,
    database: DATABASE_ID,
    schemaVersion: SCHEMA_VERSION,
    // 서버 기대값 옆에 문서의 실제 값을 나란히 둔다 — 쓰기 전에 드리프트를 본다.
    documentSchemaVersion,
    exists,
    revision,
    readError,
  };
});
