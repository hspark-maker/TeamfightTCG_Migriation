/**
 * 요청이 실어 온 영수증 번호(txId)를 검사하는 순수 파일.
 *
 * `firebase-admin`·`HttpsError` 를 지지 않는다 — 순수 회귀(`scripts/`)가 `lib/` 를 직접
 * require 하는 관용구가 깨지고, functions-currency 미러가 순수 계약을 잃는다.
 */

/**
 * 영수증 번호의 형식. Firestore 문서 id 로 그대로 쓰이므로 `/` · `.` · 제어문자를 막는다.
 * 길이 하한 8 은 "a" 같은 값이 계정 전역 멱등 키가 되어 서로 다른 명령을 잡아먹는 것을 막는다.
 */
const RECEIPT_ID_PATTERN = /^[A-Za-z0-9:_-]{8,128}$/;

/**
 * 요청의 txId 가 클라가 제대로 발급한 영수증 번호인가.
 * 로그의 txIdSource 를 "client"/"server" 로 가르는 데 쓴다.
 * @param {unknown} raw 요청에 실린 txId 원본 값
 * @return {boolean} 형식을 통과했는가
 */
export function isClientReceiptId(raw: unknown): boolean {
  return typeof raw === "string" && RECEIPT_ID_PATTERN.test(raw);
}

/**
 * 요청의 txId 를 영수증 번호로 받는다. 없거나 형식을 벗어나면 서버가 발급한다.
 *
 * **절대 던지지 않는다.** invalid-argument 로 거절하면 클라 CloudFailureClassifier 가
 * Unusable → BlockSession 으로 읽어 세션이 끊긴다. txId 를 보내지 않는 구 클라도
 * 그대로 살아야 서버·클라 배포 순서에 제약이 생기지 않는다(멱등만 못 받을 뿐이다).
 *
 * **클라 발급 규칙: txId 는 요청 하나당 하나여야 한다.** 화면·세션 단위로 발급하면
 * 같은 txId 로 다른 인자를 보낸 요청이 첫 응답을 그대로 받는다 — source 대조는 명령만
 * 가르고 인자는 가르지 않는다(같은 openPack 이면 다른 packId 라도 히트로 읽힌다).
 * @param {unknown} raw 요청에 실린 txId 원본 값
 * @param {string} fallback 형식을 벗어났을 때 쓸 서버 발급 번호(randomUUID)
 * @return {string} 영수증 번호
 */
export function clientReceiptId(raw: unknown, fallback: string): string {
  return isClientReceiptId(raw) ? (raw as string) : fallback;
}
