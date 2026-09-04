import {randomUUID} from "node:crypto";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {db} from "../firebaseApp";
import {mutateSave, requireUid, SaveMutation} from "../save/saveDocument";
import {clientReceiptId, isClientReceiptId} from "../save/receiptId";
import {buildFreshAccountSlots} from "../save/freshAccount";
import {resolveStarterCardIds} from "../save/starterCards";
import {clearGrants, grantsRef} from "../growth/tutorialGrants";

/**
 * QA 계정 초기화. 세이브 슬롯 9개를 첫실행으로 밀고 무료 한 방 문서를 함께 지운다.
 *
 * 클라가 아니라 서버가 미는 이유는 **슬롯 모양의 단일 진실원**이다 — buildFreshAccountSlots 는
 * 신규 계정에 ensureAccount 가 쓰는 바로 그 함수이고 스타터 목록도 서버 스펙에서 온다.
 * 클라가 사본을 들면 슬롯 하나를 빠뜨리는 사고와 룰 픽스처와 모양이 갈리는 사고가 함께 열린다.
 *
 * 튜토리얼 좌표는 여기서 심지 않는다 — 서버의 어떤 판정도 그 좌표를 읽지 않으므로 클라의 몫이다.
 * grants 를 같이 지우는 것은 필수다: 신규 계정 잔액에는 Shard·Energy 가 없어, 무료 한 방이 소진된
 * 채 되감으면 강화가 NotAffordable 로 거절되고 그 스텝에서 멈춘다.
 * 지갑은 건드리지 않는다(잔액은 지갑 문서의 것이고 되돌릴 경로가 서버 어디에도 없다).
 */
export const devResetSave = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");

  // 라이브 문서는 어떤 경우에도 이 함수가 건드리지 않는다.
  if (env !== "test") {
    throw new HttpsError(
      "permission-denied",
      "devResetSave is available on the test env only.",
    );
  }

  const starter = await resolveStarterCardIds(env);

  // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
  const txId = clientReceiptId(request.data?.txId, randomUUID());

  // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
  let replayed = true;

  const result = await mutateSave(env, uid, "devResetSave", {kind: "client", txId},
    (current, transaction): SaveMutation => {
      // 닉네임은 살린다 — 안 넘기면 buildFreshAccountSlots 가 매 초기화마다 새 닉을 굳힌다.
      const profile = (current.profile ?? {}) as Record<string, unknown>;
      const nickname = typeof profile.nickname === "string" && profile.nickname.length > 0 ?
        profile.nickname : undefined;

      // 세이브 변이와 같은 트랜잭션이다 — 한쪽만 성공한 계정이 생기지 않는다(enhanceCard 선례).
      clearGrants(transaction, grantsRef(db, env, uid));

      return {slots: buildFreshAccountSlots(starter.cardIds, nickname)} as SaveMutation;
    },
    (adopted) => {
      replayed = false;
      return adopted;
    });

  if (replayed) {
    logger.info("receipt replay", {uid, env, source: "devResetSave", txId, revision: result.revision});
  } else {
    logger.info("devResetSave", {
      uid, env, revision: result.revision,
      txIdSource: isClientReceiptId(request.data?.txId) ? "client" : "server",
    });
  }
  return result;
});
