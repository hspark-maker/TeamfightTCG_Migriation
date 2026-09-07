import * as logger from "firebase-functions/logger";
import {db} from "./firebaseApp";

const CACHE_TTL_MS = 60_000;

type CachedConfig = {
  enabled: boolean;
  expiresAtMs: number;
};

const cache = new Map<string, CachedConfig>();

/**
 * Cloud Run 전투 재생 호출 여부를 읽는다.
 * 문서가 없으면 안전한 초기 상태인 off다. 읽기 장애 때는 마지막 관측값을 유지하고,
 * 관측값도 없으면 off로 접되 매번 오류를 남겨 조용한 권위 후퇴를 막는다.
 * @param {string} env 환경 id
 * @return {Promise<boolean>} true 면 재생을 호출하고 그 결과로 정산한다
 */
export async function isBattleReplayEnabled(env: string): Promise<boolean> {
  const now = Date.now();
  const cached = cache.get(env);
  if (cached != null && cached.expiresAtMs > now) return cached.enabled;

  try {
    const snapshot = await db.doc(`envs/${env}/config/battleReplay`).get();
    const enabled = snapshot.exists && snapshot.data()?.enabled === true;
    cache.set(env, {enabled, expiresAtMs: now + CACHE_TTL_MS});
    return enabled;
  } catch (error) {
    logger.error("battle_replay_config_read_failed", {
      env,
      fallback: cached?.enabled ?? false,
      hasCachedValue: cached != null,
      error,
    });
    return cached?.enabled ?? false;
  }
}
