"use strict";
/**
 * 카드팩 개봉이 바꾸는 **소유 슬롯**의 갱신 후 전체 값 빌더.
 * 클라 OwnershipSaveData 의 모양을 그대로 낸다.
 *
 * 키는 camelCase 다 — 클라가 Newtonsoft(CamelCaseNamingStrategy, ProcessDictionaryKeys=false)로
 * 역직렬화하므로 프로퍼티는 camelCase, 딕셔너리 키(재화 이름·카드 id)는 원형이어야 한다.
 *
 * 재화는 currency/wallet, 카드 성장은 growth/cardGrowth 가 갖는다.
 * Firestore 를 모른다(scripts/test-open-pack.js 가 직접 부른다).
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.readOwnedIds = readOwnedIds;
exports.buildOwnershipSlot = buildOwnershipSlot;
const saveValues_1 = require("../save/saveValues");
/**
 * 소유 카드 id. **기존 순서를 유지**하고 중복·비정수·0 이하를 버린다.
 * @param {unknown} ownership 문서의 ownership 슬롯
 * @return {number[]} 카드 id
 */
function readOwnedIds(ownership) {
    const source = ownership?.cardIds;
    if (!Array.isArray(source))
        return [];
    const seen = new Set();
    const ids = [];
    for (const raw of source) {
        const id = (0, saveValues_1.intOf)(raw);
        if (id <= 0 || seen.has(id))
            continue;
        seen.add(id);
        ids.push(id);
    }
    return ids;
}
/**
 * 지급 후 소유 슬롯. 신규 카드를 뽑힌 순서로 뒤에 붙인다.
 * @param {number[]} owned 기존 소유 id(순서 유지)
 * @param {DrawnCard[]} drawn 뽑힌 카드
 * @return {object} ownership 슬롯 전체 값
 */
function buildOwnershipSlot(owned, drawn) {
    const seen = new Set(owned);
    const cardIds = [...owned];
    for (const card of drawn) {
        if (!card.isNew || seen.has(card.cardId))
            continue;
        seen.add(card.cardId);
        cardIds.push(card.cardId);
    }
    return { cardIds };
}
//# sourceMappingURL=packSlots.js.map