import {HttpsError, onCall} from "firebase-functions/v2/https";
import {DATABASE_ID} from "../firebaseApp";

/**
 * 재화 codebase 의 왕복 진단. 이 codebase 가 실제로 호출 가능한지만 본다.
 *
 * 인증을 요구하는 것은 의도다 — 2세대 함수는 Cloud Run invoker 바인딩이 없으면 **코드 앞에서**
 * HTML 403 을 뱉고, 그 바인딩은 서비스를 새로 만들 때만 걸린다. 인증 없는 URL POST 가
 * **401 이면 도달한 것이고 403 이면 바인딩이 없는 것**이라, 새 codebase 의 첫 함수가
 * 이 판정을 미리 밟아 준다.
 */
export const currencyPing = onCall((request) => {
  const uid = request.auth?.uid;
  if (!uid) {
    throw new HttpsError("unauthenticated", "Sign in first.");
  }

  return {
    ok: true,
    uid,
    codebase: "currency",
    database: DATABASE_ID,
  };
});
