import {randomUUID} from "node:crypto";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {mutateSave, requireUid, SaveMutation} from "../save/saveDocument";
import {clientReceiptId, isClientReceiptId} from "../save/receiptId";

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

  // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
  const txId = clientReceiptId(request.data?.txId, randomUUID());

  // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
  // finalize 안에서 뒤집는다 — 트랜잭션 재실행마다 다시 돌아도 결과가 같다.
  let replayed = true;

  const result = await mutateSave(env, uid, "devBumpRevision", {kind: "client", txId},
    (current): SaveMutation => {
      if (nickname === undefined) return {slots: {}};

      // 갱신 후 슬롯 **전체**를 돌려준다 — 클라는 슬롯을 통째로 갈아끼운다.
      const profile = (current.profile ?? {}) as Record<string, unknown>;
      return {slots: {profile: {...profile, nickname}}};
    },
    (adopted) => {
      replayed = false;
      return adopted;
    });

  if (replayed) {
    logger.info("receipt replay", {uid, env, source: "devBumpRevision", txId, revision: result.revision});
  } else {
    logger.info("devBumpRevision", {uid, env, revision: result.revision,
      txIdSource: isClientReceiptId(request.data?.txId) ? "client" : "server"});
  }
  return result;
});
