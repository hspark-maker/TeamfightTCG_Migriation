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
exports.resolveStarterCardIds = resolveStarterCardIds;
const logger = __importStar(require("firebase-functions/logger"));
const specBlobReader_1 = require("../specs/specBlobReader");
const starterPool_1 = require("./starterPool");
/** 카드 카탈로그가 될 수 있는 표. 클라 SpecSource 가 ContentProfile 의 RunMode 로 하나를 고르는데,
 * 서버는 그 선택을 알 수 없어 합집합으로 본다 — 여기서 걸러야 할 것은 "어느 표에도 없는 카드"다. */
const CARD_TABLES = ["Card", "Card_Test"];
/**
 * 카드 카탈로그의 id 집합. 행 문서 id 가 곧 카드 id 다(업로더가 id 열로 문서를 만든다).
 * @param {string} env 환경 id
 * @return {Promise<Set<number>>} 카탈로그에 있는 카드 id
 */
async function readKnownCardIds(env) {
    const ids = new Set();
    for (const table of CARD_TABLES) {
        for (const row of await (0, specBlobReader_1.readSpecRows)(env, table)) {
            const id = Number(row.id);
            if (Number.isInteger(id) && id > 0)
                ids.add(id);
        }
    }
    return ids;
}
/**
 * 스펙 표에서 스타터 카드를 읽는다. 표가 없거나 읽지 못해도 계정 생성을 막지 않는다.
 *
 * 클라 BattleContentSync 는 meta 문서의 rowCount·payloadHash 로 표 무결성을 대조하고 어긋나면
 * 통째로 거부하는데, 여기서는 rows 를 직접 읽어 그 검사를 건너뛴다. 업로드가 중간에 끊긴 표로
 * 만들어진 계정만 다른 스타터를 갖게 된다 — 카드 존재 검사가 그 피해를 덱 무효화까지는 가지
 * 않게 막지만, 무결성 대조까지 옮기는 것은 R3(스펙 서버화)의 몫이다.
 * @param {string} env 환경 id
 * @return {Promise<{cardIds: number[], source: StarterSource}>} 카드 목록과 출처
 */
async function resolveStarterCardIds(env) {
    try {
        // 표를 블롭으로 통째 읽고 packId 는 메모리에서 거른다 — where 질의도 맞는 행 수만큼 과금되고,
        // CardPackDrop 은 300행이 넘어 계정 생성 1건이 수백 읽기가 됐다. 정렬은 리더가 id 숫자로 한다.
        const rows = (await (0, specBlobReader_1.readSpecRows)(env, "CardPackDrop"))
            .filter((row) => String(row.packId ?? "") === starterPool_1.STARTER_PACK_ID)
            .map((row) => ({
            id: Number(row.id),
            minGrade: String(row.minGrade ?? ""),
            cardId: Number(row.cardId),
        }));
        // 카탈로그를 못 읽으면 존재 검사 없이 뽑는 대신 폴백으로 간다 — 검증 없이 지급하면
        // 카탈로그에 없는 카드가 덱에 굳어 클라가 덱 0개로 초기화되고 복구 경로가 없다.
        const knownCardIds = rows.length > 0 ? await readKnownCardIds(env) : new Set();
        const cardIds = knownCardIds.size > 0 ?
            (0, starterPool_1.resolveStarterCardsFromRows)(rows, starterPool_1.FRESH_ACCOUNT_GRADE, knownCardIds) :
            [];
        if (cardIds.length > 0)
            return { cardIds, source: "spec" };
        logger.info("starter cards fell back to the built-in list", {
            env,
            rowCount: rows.length,
            knownCardCount: knownCardIds.size,
        });
        return { cardIds: [...starterPool_1.FALLBACK_STARTER_CARD_IDS], source: "fallback" };
    }
    catch (error) {
        // 스펙을 못 읽는 것이 계정을 못 만들 이유는 아니다 — 어느 갈래였는지만 남기고 폴백으로 간다.
        logger.error("starter card spec read failed", {
            env,
            message: error instanceof Error ? error.message : String(error),
        });
        return { cardIds: [...starterPool_1.FALLBACK_STARTER_CARD_IDS], source: "specError" };
    }
}
//# sourceMappingURL=starterCards.js.map