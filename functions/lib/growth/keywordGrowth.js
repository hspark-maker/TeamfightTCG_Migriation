"use strict";
/**
 * 키워드 강화 진행도(키워드 한 종류의 레벨)의 슬롯 코덱.
 * 클라 KeywordGrowthSaveData / KeywordGrowthManager 의 쌍둥이다. 순수(Firestore·HttpsError 모름).
 *
 * 키는 **CardKeyword 플래그 정수의 문자열**이다 — "1"=Ranged · "2"=Peerless · "4"=Execution …
 * 클라 SyncSaveData 가 ((int)keyword).ToString() 으로 쓰고 Init 이 int.TryParse 로 읽는다.
 * 키워드 **이름**으로 쓰면 그 계정의 키워드 강화가 통째로 사라진다(클라가 파싱에 실패해 버린다).
 *
 * 카드 성장(growth/cardGrowth)과 합치지 않는다 — 키 공간도 값 모양도 다르다(저쪽은 카드 id → 3필드).
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.SUPPORTED_KEYWORDS = exports.KEYWORD_MAX_LEVEL = void 0;
exports.parseKeywordFlag = parseKeywordFlag;
exports.isSupportedKeyword = isSupportedKeyword;
exports.readKeywordLevels = readKeywordLevels;
exports.levelOfKeyword = levelOfKeyword;
exports.setKeywordLevel = setKeywordLevel;
exports.keywordGrowthSlot = keywordGrowthSlot;
const saveValues_1 = require("../save/saveValues");
/** 키워드 강화 상한 레벨. 클라 KeywordGrowthRules.MaxLevel 과 같아야 한다 — 저쪽 Init 이 여기서 자른다. */
exports.KEYWORD_MAX_LEVEL = 10;
/** CardKeyword 이름(소문자) → 플래그 정수. KeywordEnhance 표의 keyword 열이 이 이름으로 저작된다. */
const KEYWORD_FLAGS = {
    ranged: 1,
    peerless: 2,
    execution: 4,
    taunt: 8,
    cunning: 16,
    mark: 32,
    healer: 64,
    invincible: 128,
    bonushp: 256,
    immortal: 512,
};
/**
 * 강화가 열려 있는 키워드. 클라 KeywordGrowthRules.SupportedKeywords 의 쌍둥이다.
 * 표에 다른 키워드 행이 저작돼도 여기 없으면 열지 않는다 — 클라 Init 이 목록 밖 키를 버리므로
 * 서버가 레벨을 남겨 두면 다음 클라 저장이 그 항목을 지워 진행도가 조용히 증발한다.
 */
exports.SUPPORTED_KEYWORDS = [
    KEYWORD_FLAGS.ranged,
    KEYWORD_FLAGS.peerless,
    KEYWORD_FLAGS.execution,
    KEYWORD_FLAGS.taunt,
    KEYWORD_FLAGS.cunning,
    KEYWORD_FLAGS.healer,
];
/**
 * 표의 keyword 열을 플래그 정수로 읽는다. 못 읽으면 0 — 그 행은 버린다.
 * @param {string} value keyword 열 값
 * @return {number} CardKeyword 플래그 정수
 */
function parseKeywordFlag(value) {
    return KEYWORD_FLAGS[value.trim().toLowerCase()] ?? 0;
}
/**
 * 강화가 열려 있는 키워드인가.
 * @param {number} keyword 플래그 정수
 * @return {boolean} 지원 목록에 있으면 true
 */
function isSupportedKeyword(keyword) {
    return exports.SUPPORTED_KEYWORDS.includes(keyword);
}
/**
 * 키워드 레벨을 읽는다. 지원 밖 키·0 이하 레벨은 버리고 상한에서 자른다(클라 Init 과 같은 정규화).
 * @param {unknown} keywordGrowth 문서의 keywordGrowth 슬롯
 * @return {KeywordLevels} 키워드 레벨
 */
function readKeywordLevels(keywordGrowth) {
    const source = keywordGrowth?.levels ?? {};
    const levels = {};
    for (const [key, raw] of Object.entries(source)) {
        const keyword = (0, saveValues_1.intOf)(key);
        if (!isSupportedKeyword(keyword))
            continue;
        const level = (0, saveValues_1.intOf)(raw);
        if (level <= 0)
            continue;
        levels[String(keyword)] = level > exports.KEYWORD_MAX_LEVEL ? exports.KEYWORD_MAX_LEVEL : level;
    }
    return levels;
}
/**
 * 이 키워드의 현재 레벨(기록이 없으면 0).
 * @param {KeywordLevels} levels 키워드 레벨
 * @param {number} keyword 플래그 정수
 * @return {number} 레벨
 */
function levelOfKeyword(levels, keyword) {
    const level = levels[String(keyword)] ?? 0;
    return level > 0 ? level : 0;
}
/**
 * 레벨 하나를 고쳐 새 맵을 낸다. 입력 맵은 건드리지 않는다 — 트랜잭션이 재실행되면 원본을 다시 쓴다.
 * @param {KeywordLevels} levels 기존 레벨
 * @param {number} keyword 플래그 정수
 * @param {number} level 새 레벨
 * @return {KeywordLevels} 갱신된 레벨
 */
function setKeywordLevel(levels, keyword, level) {
    const next = { ...levels };
    if (!isSupportedKeyword(keyword))
        return next;
    const clamped = level > exports.KEYWORD_MAX_LEVEL ? exports.KEYWORD_MAX_LEVEL : level;
    if (clamped <= 0)
        delete next[String(keyword)];
    else
        next[String(keyword)] = clamped;
    return next;
}
/**
 * 세이브의 keywordGrowth 슬롯 **전체 값**. 레벨 0 항목은 버린다
 * — 클라 SyncSaveData 가 레벨 0 을 애초에 담지 않으므로, 남기면 다음 저장에서 문서가 흔들린다.
 * @param {KeywordLevels} levels 키워드 레벨
 * @return {object} keywordGrowth 슬롯
 */
function keywordGrowthSlot(levels) {
    const pruned = {};
    for (const [key, level] of Object.entries(levels)) {
        if (level <= 0)
            continue;
        pruned[key] = level;
    }
    return { levels: pruned };
}
//# sourceMappingURL=keywordGrowth.js.map