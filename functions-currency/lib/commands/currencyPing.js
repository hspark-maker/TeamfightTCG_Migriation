"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.currencyPing = void 0;
const https_1 = require("firebase-functions/v2/https");
const firebaseApp_1 = require("../firebaseApp");
/**
 * 재화 codebase 의 왕복 진단. 이 codebase 가 실제로 호출 가능한지만 본다.
 *
 * 인증을 요구하는 것은 의도다 — 2세대 함수는 Cloud Run invoker 바인딩이 없으면 **코드 앞에서**
 * HTML 403 을 뱉고, 그 바인딩은 서비스를 새로 만들 때만 걸린다. 인증 없는 URL POST 가
 * **401 이면 도달한 것이고 403 이면 바인딩이 없는 것**이라, 이 함수가 그 판정을 밟는다.
 *
 * 더 이상 이 codebase 의 첫 함수가 아니다(C6.6 이 devGrantCurrency 를 들여왔다). 그래도 남긴다.
 * 1. **릴리즈 뒤 이 codebase 가 살아 있는지 묻는 유일한 수단이다.** devGrantCurrency 는
 *    `env !== "test"` 를 막으므로 live 에서 호출 가능한 함수가 여기 하나도 없다.
 *    ping 은 환경을 안 가려서 live 에서도 왕복이 성립한다.
 * 2. 함수가 하나뿐이면 "codebase 자체가 안 떴나" 와 "그 함수 하나가 문제인가" 를 가를 수 없다.
 *    아무것도 안 하는 함수가 옆에 있어야 지갑 쓰기 실패의 범위가 좁혀진다.
 */
exports.currencyPing = (0, https_1.onCall)((request) => {
    var _a;
    const uid = (_a = request.auth) === null || _a === void 0 ? void 0 : _a.uid;
    if (!uid) {
        throw new https_1.HttpsError("unauthenticated", "Sign in first.");
    }
    return {
        ok: true,
        uid,
        codebase: "currency",
        database: firebaseApp_1.DATABASE_ID,
    };
});
//# sourceMappingURL=currencyPing.js.map