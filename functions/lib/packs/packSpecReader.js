"use strict";
/**
 * 카드팩 판정에 필요한 스펙 표 읽기. 표 자체는 `../specs/specBlobReader` 가 블롭 문서 1개로 읽는다.
 *
 * 팩 1회 개봉에 표 4개(CardPack · CardPackDrop · Card · RankGrade)를 봐야 한다.
 * 여기서는 그 표들을 팩 어휘로 옮기는 일만 한다 — 읽기 · 캐시 · 무결성 대조는 리더의 몫이다.
 *
 * **orderBy 를 쓰지 않는다.** 이 프로젝트에는 firestore.indexes.json 이 없어 복합 인덱스를 만들 수 없다
 * — 정렬은 리더가 메모리에서 id 를 **숫자로** 비교해서 한다(문자열 정렬은 "10" 이 "2" 앞에 서서 풀 순서를 바꾼다).
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.readSpecRows = exports.clearSpecCache = void 0;
exports.readCardPackRow = readCardPackRow;
exports.readDropRows = readDropRows;
exports.readRankGradeRows = readRankGradeRows;
const currencyKeys_1 = require("../currency/currencyKeys");
const specBlobReader_1 = require("../specs/specBlobReader");
const tutorialGrantPack_1 = require("./tutorialGrantPack");
var specBlobReader_2 = require("../specs/specBlobReader");
Object.defineProperty(exports, "clearSpecCache", { enumerable: true, get: function () { return specBlobReader_2.clearSpecCache; } });
Object.defineProperty(exports, "readSpecRows", { enumerable: true, get: function () { return specBlobReader_2.readSpecRows; } });
/**
 * 팩 행 하나. 시트에 없으면 null — 클라는 이때 SO 인스펙터 값으로 폴백하지만 서버는 SO 를 못 본다.
 * @param {string} env 환경 id
 * @param {string} packId CardPack.packId
 * @return {Promise<CardPackRow | null>} 팩 행
 */
async function readCardPackRow(env, packId) {
    const rows = await (0, specBlobReader_1.readSpecRows)(env, "CardPack");
    const row = rows.find((r) => String(r.packId ?? "") === packId);
    if (row === undefined)
        return null;
    return {
        packId,
        priceType: (0, currencyKeys_1.parseCurrency)(String(row.priceType ?? "")),
        price: Number(row.price ?? 0),
        priceAuthored: (0, tutorialGrantPack_1.isPriceAuthored)(row.price),
        // 클라 CardPackData.DrawCount 가 Mathf.Max(1, …) 로 조인다.
        drawCount: Math.max(1, Number(row.drawCount ?? 0)),
        uniqueDraw: Number(row.uniqueDraw ?? 0) !== 0,
        refundType: (0, currencyKeys_1.parseCurrency)(String(row.refundType ?? "")),
        refundAmount: Number(row.refundAmount ?? 0),
        minRankGrade: String(row.minRankGrade ?? ""),
    };
}
/**
 * 이 팩의 드롭 행. 표를 통째로 읽어 캐시한 뒤 메모리에서 거른다
 * — packId 마다 where 질의를 던지면 캐시가 팩 수만큼 갈라진다.
 * @param {string} env 환경 id
 * @param {string} packId CardPack.packId
 * @return {Promise<DropRow[]>} id 오름차순 드롭 행
 */
async function readDropRows(env, packId) {
    const rows = await (0, specBlobReader_1.readSpecRows)(env, "CardPackDrop");
    return rows
        .filter((row) => String(row.packId ?? "") === packId)
        .map((row) => ({
        id: Number(row.id),
        packId,
        minGrade: String(row.minGrade ?? ""),
        cardId: Number(row.cardId ?? 0),
        weight: Number(row.weight ?? 0),
    }));
}
/**
 * RankGrade 행. 클라 RankConfig 의 grades 리스트에 대응한다.
 * @param {string} env 환경 id
 * @return {Promise<RankGradeRow[]>} id 오름차순 등급 행
 */
async function readRankGradeRows(env) {
    const rows = await (0, specBlobReader_1.readSpecRows)(env, "RankGrade");
    return rows.map((row) => ({
        id: Number(row.id),
        gradeKey: String(row.gradeKey ?? ""),
        entryPoints: Number(row.entryPoints ?? Number.NaN),
    }));
}
//# sourceMappingURL=packSpecReader.js.map