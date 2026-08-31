"use strict";
/**
 * 세이브 문서에서 값을 안전하게 읽는 공용 도구. Firestore 를 모른다.
 *
 * 문서가 손상돼도 룰이 거부하는 값(NaN·문자열)을 되쓰지 않게 하는 것이 목적이라
 * 재화·소유·성장이 전부 같은 함수를 써야 한다 — 갈라 두면 슬롯마다 방어가 달라진다.
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.intOf = intOf;
/**
 * 정수로 읽고 못 읽으면 0.
 * @param {unknown} value 문서 값
 * @return {number} 정수
 */
function intOf(value) {
    const numeric = Number(value);
    return Number.isFinite(numeric) ? Math.trunc(numeric) : 0;
}
//# sourceMappingURL=saveValues.js.map