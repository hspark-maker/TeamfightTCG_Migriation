import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {mutateSave, requireUid, SaveMutation} from "../save/saveDocument";

/**
 * R0 채택 계약 실증용. 서버가 실제로 문서를 쓰고 revision 을 올린 뒤
 * {revision, updatedSlots} 를 돌려준다. R9 에서 debugMutate 로 흡수되거나 삭제된다.
 */
export const devBumpRevision = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");

  // 라이브 문서는 어떤 경우에도 이 함수가 건드리지 않는다.
  if (env !== "test") {
    throw new HttpsError(
      "permission-denied",
      "devBumpRevision is available on the test env only.",
    );
  }

  const nickname = request.data?.nickname;
  if (nickname !== undefined && typeof nickname !== "string") {
    throw new HttpsError("invalid-argument", "nickname must be a string.");
  }

  const result = await mutateSave("devBumpRevision", env, uid, (current): SaveMutation => {
    if (nickname === undefined) return {slots: {}};

    // 갱신 후 슬롯 **전체**를 돌려준다 — 클라는 슬롯을 통째로 갈아끼운다.
    const profile = (current.profile ?? {}) as Record<string, unknown>;
    return {slots: {profile: {...profile, nickname}}};
  });

  logger.info("devBumpRevision", {uid, env, revision: result.revision});
  return result;
});
