"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.sameSubmission = sameSubmission;
exports.expectedMatchId = expectedMatchId;
exports.sameBoardOrder = sameBoardOrder;
exports.submissionsAgree = submissionsAgree;
exports.decideMatch = decideMatch;
const node_crypto_1 = require("node:crypto");
function sameSubmission(a, b) {
    return a.uid === b.uid && (a.seedSource ?? "commit_reveal") === (b.seedSource ?? "commit_reveal") &&
        a.myNonce === b.myNonce && a.opponentNonce === b.opponentNonce &&
        a.myDeckHash === b.myDeckHash && a.opponentDeckHash === b.opponentDeckHash &&
        a.finalStateHash === b.finalStateHash && a.stateHashChain === b.stateHashChain &&
        a.stateHashChainPrev === b.stateHashChainPrev &&
        a.stateHashChainLength === b.stateHashChainLength &&
        a.contentFingerprint === b.contentFingerprint && a.won === b.won &&
        a.myRemaining === b.myRemaining && a.opponentRemaining === b.opponentRemaining &&
        a.rankPointsBefore === b.rankPointsBefore &&
        (a.commandLogVersion ?? 0) === (b.commandLogVersion ?? 0) &&
        (a.commandLog ?? "") === (b.commandLog ?? "") &&
        (a.commandLogHash ?? "") === (b.commandLogHash ?? "") &&
        (a.commandCount ?? 0) === (b.commandCount ?? 0) &&
        Boolean(a.commandLogTruncated) === Boolean(b.commandLogTruncated);
}
function expectedMatchId(myNonce, opponentNonce) {
    const a = Buffer.from(myNonce, "hex");
    const b = Buffer.from(opponentNonce, "hex");
    const seed = Buffer.alloc(8);
    for (let i = 0; i < seed.length; i++)
        seed[i] = a[i] ^ b[i];
    return (0, node_crypto_1.createHash)("sha256").update(seed).digest("hex").slice(0, 32);
}
/**
 * 두 제출의 보드 순서가 같은가. 한쪽이라도 없으면 대조 자체를 못 하므로 불일치로 본다.
 * @param {Submission} a 한쪽 제출.
 * @param {Submission} b 다른 쪽 제출.
 * @return {boolean} 두 제출의 보드 순서가 완전히 같으면 true.
 */
function sameBoardOrder(a, b) {
    const x = a.boardOrder;
    const y = b.boardOrder;
    if (x == null || y == null)
        return false;
    for (const side of ["owner0", "owner1"]) {
        const left = x[side];
        const right = y[side];
        if (!Array.isArray(left) || !Array.isArray(right) || left.length !== right.length)
            return false;
        for (let i = 0; i < left.length; i++)
            if (left[i] !== right[i])
                return false;
    }
    return true;
}
function submissionsAgree(a, b) {
    if (a.uid === b.uid)
        return "same_uid";
    // 무승부는 양쪽 won 이 모두 false 다 — 승자 대조를 그대로 돌리면 winner_conflict 로 튕긴다.
    // 한쪽만 무승부를 주장하면 판정이 갈린 것이므로 불일치다.
    if ((a.draw ?? false) !== (b.draw ?? false))
        return "draw_conflict";
    if (!(a.draw ?? false) && a.won === b.won)
        return "winner_conflict";
    if ((a.draw ?? false) && (a.won || b.won))
        return "draw_conflict";
    const seedSource = a.seedSource ?? "commit_reveal";
    if (seedSource !== (b.seedSource ?? "commit_reveal"))
        return "seed_source_mismatch";
    if (seedSource === "commit_reveal" &&
        (a.myNonce !== b.opponentNonce || a.opponentNonce !== b.myNonce))
        return "nonce_mismatch";
    if (a.myDeckHash !== b.opponentDeckHash || a.opponentDeckHash !== b.myDeckHash)
        return "deck_mismatch";
    if (a.finalStateHash !== b.finalStateHash)
        return "state_hash_mismatch";
    const chainsAgree = a.stateHashChainLength === b.stateHashChainLength ?
        a.stateHashChain === b.stateHashChain :
        a.stateHashChainLength === b.stateHashChainLength + 1 ?
            a.stateHashChainPrev === b.stateHashChain :
            b.stateHashChainLength === a.stateHashChainLength + 1 &&
                b.stateHashChainPrev === a.stateHashChain;
    if (!chainsAgree)
        return "state_chain_mismatch";
    if (a.contentFingerprint !== b.contentFingerprint)
        return "content_mismatch";
    if (a.myRemaining !== b.opponentRemaining || a.opponentRemaining !== b.myRemaining)
        return "remaining_mismatch";
    const commandVersion = a.commandLogVersion ?? 0;
    if (commandVersion !== (b.commandLogVersion ?? 0))
        return "command_log_version_mismatch";
    if (commandVersion > 0) {
        if (a.commandLogTruncated || b.commandLogTruncated)
            return "command_log_truncated";
        if (a.commandCount !== b.commandCount || a.commandLogHash !== b.commandLogHash || a.commandLog !== b.commandLog) {
            return "command_log_mismatch";
        }
    }
    return null;
}
function decideMatch(entries, createdAtMs, nowMs, deadlineMs) {
    if (entries.length < 2) {
        return nowMs - createdAtMs > deadlineMs ?
            { status: "flagged", reason: "single_submission" } : { status: "pending" };
    }
    if (entries.length > 2)
        return { status: "flagged", reason: "too_many_submissions" };
    const reason = submissionsAgree(entries[0], entries[1]);
    return reason ? { status: "flagged", reason } : { status: "confirmed" };
}
//# sourceMappingURL=matchResult.js.map