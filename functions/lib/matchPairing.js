"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.MATCH_PAIRING_TTL_MS = exports.SERVER_AUTHORITATIVE_RULESET_VERSION = exports.SERVER_RULESET_VERSION = void 0;
exports.pairingDocumentId = pairingDocumentId;
exports.matchIdFromPairingKey = matchIdFromPairingKey;
exports.joinPairing = joinPairing;
const node_crypto_1 = require("node:crypto");
exports.SERVER_RULESET_VERSION = 2;
exports.SERVER_AUTHORITATIVE_RULESET_VERSION = 2;
exports.MATCH_PAIRING_TTL_MS = 10 * 60 * 1000;
function pairingDocumentId(pairingKey) {
    return (0, node_crypto_1.createHash)("sha256").update(pairingKey, "utf8").digest("hex");
}
/**
 * 매치 문서 id. pairingKey 에서 결정론적으로 파생한다 —
 * 그래야 페어링 레코드와 매치 문서가 같은 문서 하나로 합쳐진다(별도 matchPairings 컬렉션 불필요).
 *
 * 예측 가능해도 안전하다: 시드는 이 값과 무관한 별도 난수이고, 참가 자격은 matchId 가 아니라
 * participantUids 명단으로 판정한다. 명단은 페어링 트랜잭션에서만 늘어난다.
 *
 * pairingKey 자체가 매치마다 새 난수(양측 nonce)를 섞어 만들어지므로 사실상 재사용되지 않지만,
 * 만에 하나 같은 키가 다시 오면 createMatch 가 phase/status 를 보고 거절한다.
 * @param {string} pairingKey 클라가 만든 페어링 키
 * @return {string} 32자리 hex 매치 id
 */
function matchIdFromPairingKey(pairingKey) {
    return pairingDocumentId(pairingKey).slice(0, 32);
}
function joinPairing(existing, uid, contentFingerprint, nowMs, newIdentity, expectedParticipants = 2) {
    const filled = (record) => record.participantUids.length >= record.expectedParticipants ? "paired" : "waiting";
    let record = existing;
    if (record == null || record.expiresAtMs <= nowMs) {
        record = {
            ...newIdentity,
            contentFingerprint,
            rulesetVersion: exports.SERVER_RULESET_VERSION,
            participantUids: [uid],
            expectedParticipants,
            createdAtMs: nowMs,
            expiresAtMs: nowMs + exports.MATCH_PAIRING_TTL_MS,
        };
        // AI 대전은 정원이 1이라 이 자리에서 곧장 성립한다 — 기다릴 상대가 없다.
        return { record, slot: 0, status: filled(record) };
    }
    if (record.contentFingerprint !== contentFingerprint) {
        throw new Error("content_fingerprint_mismatch");
    }
    // 정원이 다른 매치에 끼어드는 것을 막는다 — 1인 문서에 둘째가 붙으면
    // lockDeck 이 한 명의 승인만으로 "approved" 를 내주고 대인전이 검증 없이 시작된다.
    if (record.expectedParticipants !== expectedParticipants) {
        throw new Error("match_mode_mismatch");
    }
    const priorSlot = record.participantUids.indexOf(uid);
    if (priorSlot >= 0) {
        return { record, slot: priorSlot, status: filled(record) };
    }
    if (record.participantUids.length >= record.expectedParticipants) {
        throw new Error("match_pairing_full");
    }
    record = { ...record, participantUids: [...record.participantUids, uid] };
    return { record, slot: record.participantUids.length - 1, status: filled(record) };
}
//# sourceMappingURL=matchPairing.js.map