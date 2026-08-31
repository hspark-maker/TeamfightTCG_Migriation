"use strict";
// 매치 검증 콜러블들이 공유하는 페이로드 가드. 명령별 파서는 각 commands/ 파일이 갖고,
// 여기에는 여러 명령이 같이 쓰는 것만 둔다.
Object.defineProperty(exports, "__esModule", { value: true });
exports.HEX_64 = exports.HEX_32 = exports.HEX_16 = void 0;
exports.objectRecord = objectRecord;
exports.safeInteger = safeInteger;
exports.HEX_16 = /^[0-9a-f]{16}$/;
exports.HEX_32 = /^[0-9a-f]{32}$/;
exports.HEX_64 = /^[0-9a-f]{64}$/;
function objectRecord(value) {
    if (value == null || typeof value !== "object" || Array.isArray(value))
        return null;
    return value;
}
function safeInteger(value) {
    return typeof value === "number" && Number.isSafeInteger(value) ? value : null;
}
//# sourceMappingURL=payloadGuards.js.map