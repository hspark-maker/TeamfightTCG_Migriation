import {randomUUID} from "node:crypto";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {mutateSave, requireUid, SaveMutation} from "../save/saveDocument";
import {clientReceiptId, isClientReceiptId} from "../save/receiptId";
import {buildFreshAccountSlots} from "../save/freshAccount";
import {resolveStarterCardIds} from "../save/starterCards";

/** 좌표 상한. 저작 스텝 수를 서버가 모르므로 형식만 본다 — 범위 밖 좌표는 클라 러너가 스스로 접는다. */
const COORD_MAX = 1000;

/**
 * QA 되감기. 세이브 슬롯 9개를 첫실행으로 밀고 튜토리얼 좌표만 지정 값으로 심는다.
 *
 * 클라가 아니라 서버가 미는 이유는 진행도의 단조성 때문이다 — 클라가 세이브를 뒤로 쓸 수 있으면
 * 룰에 "뒤로 못 간다"를 걸 수 없고, 그 검사가 없으면 보상 수령 목록을 비워 재수령할 수 있다.
 * 지갑은 건드리지 않는다(잔액은 지갑 문서의 것이고 되돌릴 경로가 서버 어디에도 없다).
 */
export const devRewindTutorial = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");

  // 라이브 문서는 어떤 경우에도 이 함수가 건드리지 않는다.
  if (env !== "test") {
    throw new HttpsError(
      "permission-denied",
      "devRewindTutorial is available on the test env only.",
    );
  }

  const chapterIndex = Number(request.data?.chapterIndex ?? 0);
  const stepIndex = Number(request.data?.stepIndex ?? 0);
  if (!isCoord(chapterIndex) || !isCoord(stepIndex)) {
    throw new HttpsError(
      "invalid-argument",
      `chapterIndex and stepIndex must be integers in 0..${COORD_MAX}.`,
    );
  }

  const starter = await resolveStarterCardIds(env);

  // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
  const txId = clientReceiptId(request.data?.txId, randomUUID());

  // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
  let replayed = true;

  const result = await mutateSave(env, uid, "devRewindTutorial", {kind: "client", txId},
    (current): SaveMutation => {
      // 닉네임은 살린다 — 안 넘기면 buildFreshAccountSlots 가 매 되감기마다 새 닉을 굳힌다.
      const profile = (current.profile ?? {}) as Record<string, unknown>;
      const nickname = typeof profile.nickname === "string" && profile.nickname.length > 0 ?
        profile.nickname : undefined;

      const slots = buildFreshAccountSlots(starter.cardIds, nickname) as Record<string, unknown>;
      const tutorial = (slots.tutorial ?? {}) as Record<string, unknown>;

      // 좌표만 덮는다 — 부트 카운터의 -1 센티널은 클라 TutorialSaveData 초기값과 짝이라 건드리지 않는다.
      return {
        slots: {
          ...slots,
          tutorial: {...tutorial, chapterIndex, chapterStepIndex: stepIndex},
        },
      } as SaveMutation;
    },
    (adopted) => {
      replayed = false;
      return adopted;
    });

  if (replayed) {
    logger.info("receipt replay", {uid, env, source: "devRewindTutorial", txId, revision: result.revision});
  } else {
    logger.info("devRewindTutorial", {
      uid, env, chapterIndex, stepIndex, revision: result.revision,
      txIdSource: isClientReceiptId(request.data?.txId) ? "client" : "server",
    });
  }
  return result;
});

/**
 * 좌표 형식 검사.
 * @param {number} value 검사할 값
 * @return {boolean} 0..COORD_MAX 범위의 정수인가
 */
function isCoord(value: number): boolean {
  return Number.isInteger(value) && value >= 0 && value <= COORD_MAX;
}
