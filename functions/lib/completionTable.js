"use strict";
// 도감 완성의 **판정 근거 표** 해석과, 완성 판정 자체. 보상 해석(rewardTable.ts)과 소유를 나눈다 —
// 저쪽은 "무엇을 주는가", 여기는 "다 모았는가" 다.
//
// TournamentChapter 표는 tournamentTable.ts 가 읽는다(표 하나에 파서 하나) — 저쪽은 완주 모수뿐
// 아니라 해금 사슬까지 재므로 도감과 나눠 둔다. isCompleted 는 두 도메인이 함께 쓴다.
//
// 순수 모듈 제약: firebase-admin · HttpsError 를 들이지 마라. functions/scripts 의 회귀가
// lib/ 를 직접 require 하고 돈다.
Object.defineProperty(exports, "__esModule", { value: true });
exports.ALBUM_ROOT_KEY = void 0;
exports.parseAlbumEntryRows = parseAlbumEntryRows;
exports.parseAlbumScope = parseAlbumScope;
exports.albumScopeCardIds = albumScopeCardIds;
exports.isCompleted = isCompleted;
exports.missingCount = missingCount;
/** 도감 전체 보상의 낙인 키. 클라 AlbumRewardManager.AlbumRewardKey 와 같은 문자열이다. */
exports.ALBUM_ROOT_KEY = "b";
/**
 * 정수로 읽고 못 읽으면 0. rewardTable.looseInteger 와 같은 규약이다 —
 * id·order 가 비었다고 표 전체 파싱이 죽으면 수령이 통째로 멈춘다.
 * @param {unknown} value 표 값
 * @return {number} 정수
 */
function looseInteger(value) {
    const numeric = Number(value ?? 0);
    return Number.isFinite(numeric) ? Math.trunc(numeric) : 0;
}
/**
 * AlbumEntry 표를 읽는다. 테마·페이지 키가 비었거나 카드 id 가 0 이하인 줄은 버린다
 * — 못 읽는 줄을 모수에 넣으면 영영 완성되지 않는 페이지가 생긴다.
 * @param {Record<string, unknown>[]} rows 표 전량
 * @return {AlbumEntryRow[]} 읽힌 줄만
 */
function parseAlbumEntryRows(rows) {
    return rows
        .map((row) => ({
        id: looseInteger(row.id),
        themeId: String(row.themeId ?? "").trim(),
        pageId: String(row.pageId ?? "").trim(),
        cardId: looseInteger(row.cardId),
        order: looseInteger(row.order),
    }))
        .filter((row) => row.themeId.length > 0 && row.pageId.length > 0 && row.cardId > 0);
}
/**
 * 도감 낙인 키를 범위로 읽는다. 모양이 다르면 null — 부르는 쪽은 RewardNotFound 로 떨어뜨린다.
 *
 * 구분자가 애매한 키("p:A/B/C")는 추측하지 않고 null 이다. 잘못 갈라 읽으면 다른 페이지의
 * 모수로 자격을 재게 된다.
 * @param {string} ownerId 낙인 키
 * @return {AlbumScope | null} 범위, 못 읽으면 null
 */
function parseAlbumScope(ownerId) {
    if (ownerId === exports.ALBUM_ROOT_KEY)
        return { kind: "album" };
    if (ownerId.startsWith("t:")) {
        const themeId = ownerId.slice(2);
        return themeId.length > 0 && !themeId.includes("/") ? { kind: "theme", themeId } : null;
    }
    if (ownerId.startsWith("p:")) {
        const rest = ownerId.slice(2);
        const separator = rest.indexOf("/");
        if (separator <= 0)
            return null;
        const themeId = rest.slice(0, separator);
        const pageId = rest.slice(separator + 1);
        return pageId.length > 0 && !pageId.includes("/") ? { kind: "page", themeId, pageId } : null;
    }
    return null;
}
/**
 * 그 범위가 요구하는 카드 id(중복 제거, 표 순서 유지). **빈 배열은 "모수 없음"이다** —
 * 부르는 쪽이 완성으로 읽으면 저작되지 않은 페이지에서 보상이 샌다.
 * @param {AlbumEntryRow[]} rows AlbumEntry 표 전량
 * @param {AlbumScope} scope 낙인 범위
 * @return {number[]} 요구 카드 id
 */
function albumScopeCardIds(rows, scope) {
    const matched = rows.filter((row) => {
        if (scope.kind === "album")
            return true;
        if (scope.kind === "theme")
            return row.themeId === scope.themeId;
        return row.themeId === scope.themeId && row.pageId === scope.pageId;
    });
    return [...new Set(matched.map((row) => row.cardId))];
}
/**
 * 요구 목록을 전부 갖췄는가. **모수 0 은 완성이 아니다** — 빈 집합을 "다 모았다"로 읽으면
 * 표가 안 올라간 환경에서 모든 보상이 수령 가능해진다.
 * @param {Array} required 요구 목록
 * @param {Set} held 보유 집합(소유 카드 · 클리어 정점)
 * @return {boolean} 전부 갖췄으면 true
 */
function isCompleted(required, held) {
    if (required.length === 0)
        return false;
    return required.every((entry) => held.has(entry));
}
/**
 * 아직 못 갖춘 개수. 거절 로그가 "몇 개 남았나"를 싣게 하는 재료다.
 * @param {Array} required 요구 목록
 * @param {Set} held 보유 집합
 * @return {number} 부족한 개수
 */
function missingCount(required, held) {
    return required.filter((entry) => !held.has(entry)).length;
}
//# sourceMappingURL=completionTable.js.map