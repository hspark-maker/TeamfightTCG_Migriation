const assert = require("node:assert/strict");
const {
  joinPairing,
  pairingDocumentId,
  SERVER_RULESET_VERSION,
} = require("../lib/matchPairing.js");

const fingerprint = "a".repeat(64);
const firstIdentity = {matchId: "1".repeat(32), seedHex: "2".repeat(16)};
const first = joinPairing(null, "uid-a", fingerprint, 1000, firstIdentity);
assert.equal(first.status, "waiting");
assert.equal(first.slot, 0);
assert.equal(first.record.rulesetVersion, SERVER_RULESET_VERSION);

const firstRetry = joinPairing(first.record, "uid-a", fingerprint, 1001, {
  matchId: "3".repeat(32), seedHex: "4".repeat(16),
});
assert.equal(firstRetry.record.matchId, firstIdentity.matchId);
assert.equal(firstRetry.slot, 0);

const second = joinPairing(first.record, "uid-b", fingerprint, 1002, {
  matchId: "3".repeat(32), seedHex: "4".repeat(16),
});
assert.equal(second.status, "paired");
assert.equal(second.slot, 1);
assert.deepEqual(second.record.participantUids, ["uid-a", "uid-b"]);
assert.throws(() => joinPairing(second.record, "uid-c", fingerprint, 1003, firstIdentity),
  /match_pairing_full/);
assert.throws(() => joinPairing(first.record, "uid-b", "b".repeat(64), 1003, firstIdentity),
  /content_fingerprint_mismatch/);
assert.match(pairingDocumentId("photon-room_01"), /^[0-9a-f]{64}$/);

const replacement = joinPairing(first.record, "uid-c", fingerprint,
  first.record.expiresAtMs, {matchId: "5".repeat(32), seedHex: "6".repeat(16)});
assert.equal(replacement.record.matchId, "5".repeat(32));
assert.deepEqual(replacement.record.participantUids, ["uid-c"]);

console.log("match-pairing tests: pass");
