import {HttpsError, onCall} from "firebase-functions/v2/https";
import {defineSecret} from "firebase-functions/params";
import {isKnownEnv, requireUid} from "../save/saveDocument";
import {verifyMatchTicket as verifyTicket} from "../match/matchTicket";

const matchTicketSecret = defineSecret("MATCH_TICKET_SECRET");

/**
 * 상대가 보내온 매칭 티켓을 검증한다. 클라는 이 응답의 티어만 믿는다 —
 * 상대가 메시지에 함께 실어 보낸 티어 값은 표시에 쓰지 않는다.
 *
 * 서명이 깨졌거나 만료됐으면 tierIndex 를 돌려주지 않는다(호출부는 랭크 표시를 비운다).
 * 게임을 막지는 않는다 — 티어를 모르는 상대와 붙는 것은 허용이고, 모르는 티어를 지어내지 않을 뿐이다.
 */
export const verifyMatchTicket = onCall({secrets: [matchTicketSecret]}, async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const ticket = String(request.data?.ticket ?? "");

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }
  if (ticket === "") {
    throw new HttpsError("invalid-argument", "ticket must be a non-empty string");
  }

  const verdict = verifyTicket(ticket, matchTicketSecret.value(), Math.floor(Date.now() / 1000));
  if (!verdict.ok) {
    return {valid: false, reason: verdict.reason};
  }

  // 자기 티켓을 자기가 검증하는 것은 상대 티어를 확인하는 이 경로의 용도가 아니다.
  if (verdict.payload.uid === uid) {
    return {valid: false, reason: "self"};
  }
  // 다른 환경(라이브/테스트)에서 받은 티켓은 이 매칭의 증거가 아니다.
  if (verdict.payload.env !== env) {
    return {valid: false, reason: "env"};
  }

  return {
    valid: true,
    uid: verdict.payload.uid,
    tierIndex: verdict.payload.tier,
  };
});
