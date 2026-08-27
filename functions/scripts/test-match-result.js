const assert = require("node:assert/strict");
const {decideMatch, expectedMatchId, sameSubmission, submissionsAgree} = require("../lib/matchResult.js");

const a = {uid: "a", myNonce: "0000000000000001", opponentNonce: "0000000000000002",
  myDeckHash: "a".repeat(64), opponentDeckHash: "b".repeat(64), finalStateHash: "1".repeat(16),
  stateHashChain: "2".repeat(16), stateHashChainPrev: "1".repeat(16), stateHashChainLength: 5,
  contentFingerprint: "c".repeat(64), won: true,
  myRemaining: 3, opponentRemaining: 0};
const b = {uid: "b", myNonce: a.opponentNonce, opponentNonce: a.myNonce,
  myDeckHash: a.opponentDeckHash, opponentDeckHash: a.myDeckHash, finalStateHash: a.finalStateHash,
  stateHashChain: a.stateHashChain, stateHashChainPrev: a.stateHashChainPrev,
  stateHashChainLength: a.stateHashChainLength, contentFingerprint: a.contentFingerprint, won: false,
  myRemaining: 0, opponentRemaining: 3};

assert.equal(expectedMatchId(a.myNonce, a.opponentNonce), expectedMatchId(b.myNonce, b.opponentNonce));
assert.equal(submissionsAgree(a, b), null);
assert.equal(sameSubmission(a, {...a}), true);
assert.equal(sameSubmission(a, {...a, myRemaining: 2}), false);
assert.equal(sameSubmission(a, {...a, stateHashChainLength: 4}), false);
assert.equal(submissionsAgree(a, {...b, won: true}), "winner_conflict");
assert.equal(submissionsAgree(a, {...b, myDeckHash: "f".repeat(64)}), "deck_mismatch");
assert.equal(submissionsAgree(a, {...b, stateHashChain: a.stateHashChainPrev,
  stateHashChainPrev: "0".repeat(16), stateHashChainLength: 4}), null);
assert.equal(submissionsAgree(a, {...b, stateHashChain: "3".repeat(16),
  stateHashChainPrev: a.stateHashChain, stateHashChainLength: 6}), null);
assert.equal(submissionsAgree(a, {...b, stateHashChain: a.stateHashChainPrev}), "state_chain_mismatch");
assert.equal(submissionsAgree(a, {...b, stateHashChain: a.stateHashChainPrev,
  stateHashChainPrev: "0".repeat(16), stateHashChainLength: 3}), "state_chain_mismatch");
assert.equal(submissionsAgree(a, {...b, stateHashChain: "9".repeat(16),
  stateHashChainPrev: "8".repeat(16)}), "state_chain_mismatch");

assert.deepEqual(decideMatch([a], 0, 120, 120), {status: "pending"});
assert.deepEqual(decideMatch([a], 0, 121, 120), {status: "flagged", reason: "single_submission"});
assert.deepEqual(decideMatch([a, b], 0, 121, 120), {status: "confirmed"});
assert.deepEqual(decideMatch([a, b, {...a, uid: "c"}], 0, 10, 120),
  {status: "flagged", reason: "too_many_submissions"});
console.log("match-result tests: pass");
