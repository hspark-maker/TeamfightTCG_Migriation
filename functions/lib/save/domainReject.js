"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.rejectDomain = rejectDomain;
const https_1 = require("firebase-functions/v2/https");
const logger = __importStar(require("firebase-functions/logger"));
/**
 * 도메인 거절. **반드시 permission-denied 여야 한다.**
 *
 * 클라 CloudFailureClassifier 는 permission-denied·already-exists 만 "거절"로 보고,
 * failed-precondition·invalid-argument 는 "이 세션은 못 쓴다"로 보아 BlockSession 을 건다.
 * 잔액 부족으로 세션을 끊을 수는 없다.
 *
 * reason 은 **와이어 계약**이다 — 클라가 문자열을 그대로 enum 이름에 대조한다
 * (카드팩 "InsufficientGold" ↔ EPackOpenResult · 강화 "NotAffordable" ↔ EEnhanceOutcome).
 * 사유 목록은 도메인마다 다르므로 여기서 정하지 않는다. 각 command 가 자기 union 을 갖는다.
 *
 * 그 사유를 **message 앞머리에 "Reason: 설명" 으로도 싣는다.** details 만으로는 클라에 닿지 않는다 —
 * Unity SDK 의 FunctionsErrorParser 가 응답에서 status 와 message 만 살리고 details 를 버린다.
 * 읽는 쪽은 ServerCommandRejectedException.Reason 하나뿐이니 이 접두어 모양을 바꾸지 마라.
 *
 * 거절은 **무조건 로그를 남긴다** — 잔액 부족이냐 자격 미달이냐를 응답만 보고는 가를 수 없고,
 * 아무것도 안 남기면 functions:log 가 3~4분 늦는 것과 겹쳐 "호출이 안 왔다"로 오진하게 된다.
 * @param {string} reason 사유 코드
 * @param {string} message 로그용 설명
 * @param {Record<string, unknown>} context 어느 값에 막혔는지(가격·잔액·등급 등)
 */
function rejectDomain(reason, message, context = {}) {
    logger.warn("domain rejected", { reason, ...context });
    throw new https_1.HttpsError("permission-denied", `${reason}: ${message}`, { reason });
}
//# sourceMappingURL=domainReject.js.map