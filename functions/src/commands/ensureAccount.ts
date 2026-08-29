import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {
  ensureSaveDocument,
  isKnownEnv,
  requireUid,
} from "../save/saveDocument";
import {
  buildFreshAccountBalances,
  buildFreshAccountSlots,
  STARTER_GOLD,
} from "../save/freshAccount";
import {resolveStarterCardIds} from "../save/starterCards";

/** 클라 PlayerSaveDocument 가 만드는 기기 id 모양(Guid "N" 포맷). 룰이 정확히 32자를 요구한다. */
const DEVICE_ID_PATTERN = /^[0-9a-f]{32}$/;

const APP_VERSION_MAX_LENGTH = 64;

/**
 * 신규 계정의 세이브 문서와 지갑을 서버가 **한 트랜잭션**에 만든다. 이미 있으면 아무것도 쓰지 않고
 * 현재 revision 만 돌려준다.
 *
 * 클라는 문서를 먼저 읽고 **없을 때만** 부른다 — 매 부팅 호출이면 cold start 가 모든 유저의
 * 부트에 얹힌다. 응답은 채택하지 않고 클라가 문서를 다시 읽어 정상 부트 경로로 합류한다.
 */
export const ensureAccount = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const deviceId = String(request.data?.deviceId ?? "");
  const appVersion = String(request.data?.appVersion ?? "");

  // 여기가 유일한 방어선이다 — 룰의 isValidSave 를 깨는 문서를 만들면 그 계정은
  // 이후 모든 저장이 영구 거부되고 delete: if false 라 룰 층에 복구 경로가 없다.
  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }
  if (!DEVICE_ID_PATTERN.test(deviceId)) {
    throw new HttpsError("invalid-argument", "deviceId must be 32 lowercase hex characters.");
  }
  if (appVersion.length === 0 || appVersion.length > APP_VERSION_MAX_LENGTH) {
    throw new HttpsError(
      "invalid-argument",
      `appVersion must be 1..${APP_VERSION_MAX_LENGTH} characters.`,
    );
  }

  const starter = await resolveStarterCardIds(env);
  const outcome = await ensureSaveDocument(
    env, uid, deviceId, appVersion,
    () => buildFreshAccountSlots(starter.cardIds),
    buildFreshAccountBalances(),
  );

  if (outcome.repaired) {
    // 스키마 밖 문서를 버리고 다시 만들었다. 원인 추적이 되도록 버린 필드 이름을 남긴다.
    logger.warn("ensureAccount repaired", {
      uid,
      env,
      discardedFields: outcome.discardedFields,
      starterCardIds: starter.cardIds,
      starterSource: starter.source,
    });
  } else if (outcome.created) {
    logger.info("ensureAccount granted", {
      uid,
      env,
      revision: outcome.revision,
      // 지갑이 이미 있었다면 스타터 골드는 나가지 않았다 — 로그가 그것을 숨기면 안 된다.
      gold: outcome.walletCreated ? STARTER_GOLD : 0,
      walletCreated: outcome.walletCreated,
      starterCardIds: starter.cardIds,
      starterSource: starter.source,
    });
  } else {
    logger.info("ensureAccount noop", {uid, env, revision: outcome.revision});
  }

  return {
    revision: outcome.revision,
    created: outcome.created,
    starterSource: starter.source,
  };
});
