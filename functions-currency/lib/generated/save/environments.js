"use strict";
/**
 * 환경 id 화이트리스트. 아무것도 import 하지 않는 순수 파일이다.
 *
 * **이 파일은 functions-currency 로 미러된다**(`scripts/shared-files.js`). 재화 codebase 도
 * 지갑 문서를 열기 전에 같은 판정을 하는데, 목록이 갈리면 한쪽만 아는 환경이 생겨
 * 같은 uid 의 지갑을 codebase 마다 다르게 거절한다. currencyKeys 와 같은 이유로 데이터를 공유한다.
 *
 * `HttpsError` 를 지지 않는다 — 거절 코드는 호출부 callable 이 정한다(walletStore 와 같은 계약).
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.ENVIRONMENTS = void 0;
exports.isKnownEnv = isKnownEnv;
/** 알려진 환경 id. 클라 FirebaseRootPath 의 envId 와 같은 목록이어야 한다. */
exports.ENVIRONMENTS = ["live", "test"];
/**
 * 알려진 환경인가. 던지지 않고 묻는 쪽(진단 함수)이 쓴다.
 * @param {string} env 환경 id
 * @return {boolean} 알려진 환경이면 true
 */
function isKnownEnv(env) {
    return exports.ENVIRONMENTS.includes(env);
}
//# sourceMappingURL=environments.js.map