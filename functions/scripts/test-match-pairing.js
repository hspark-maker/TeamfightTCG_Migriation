const assert = require("node:assert/strict");
const {
  joinPairing,
  matchIdFromPairingKey,
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

// matchId 는 pairingKey 해시의 앞 32자다 — 두 클라가 같은 문서를 집어야 페어링이 성립한다.
{
  const key = "room-abc_deadbeef_cafebabe";
  const derived = matchIdFromPairingKey(key);
  assert.equal(derived.length, 32);
  assert.match(derived, /^[0-9a-f]{32}$/);
  assert.equal(derived, pairingDocumentId(key).slice(0, 32));
  assert.equal(derived, matchIdFromPairingKey(key));               // 결정론
  assert.notEqual(derived, matchIdFromPairingKey(key + "x"));      // 키가 다르면 문서도 다르다
}

// AI 대전(정원 1): 첫 참가자에서 곧장 성립하고, 둘째는 아예 끼어들지 못한다.
{
  const solo = joinPairing(null, "uid-solo", fingerprint, 2000,
    {matchId: "7".repeat(32), seedHex: "8".repeat(16)}, 1);
  assert.equal(solo.status, "paired");
  assert.equal(solo.slot, 0);
  assert.equal(solo.record.expectedParticipants, 1);
  assert.deepEqual(solo.record.participantUids, ["uid-solo"]);

  // 같은 유저의 재호출은 멱등이어야 한다(왕복 유실 뒤 재시도).
  const soloRetry = joinPairing(solo.record, "uid-solo", fingerprint, 2001,
    {matchId: "9".repeat(32), seedHex: "a".repeat(16)}, 1);
  assert.equal(soloRetry.status, "paired");
  assert.equal(soloRetry.slot, 0);
  assert.equal(soloRetry.record.matchId, "7".repeat(32));

  // 정원이 찼으므로 남이 붙을 수 없다 — 붙으면 1인 승인으로 대인전이 검증 없이 선다.
  assert.throws(() => joinPairing(solo.record, "uid-other", fingerprint, 2002,
    {matchId: "b".repeat(32), seedHex: "c".repeat(16)}, 1), /match_pairing_full/);

  // 정원이 다른 매치에 끼어드는 것도 막는다(솔로 문서에 대인전으로 접근).
  assert.throws(() => joinPairing(solo.record, "uid-other", fingerprint, 2003,
    {matchId: "b".repeat(32), seedHex: "c".repeat(16)}, 2), /match_mode_mismatch/);

  // 반대 방향: 대인전 문서에 솔로로 접근.
  assert.throws(() => joinPairing(first.record, "uid-solo", fingerprint, 2004,
    {matchId: "b".repeat(32), seedHex: "c".repeat(16)}, 1), /match_mode_mismatch/);
}

// 정원을 생략하면 대인전이다 — 구 클라 페이로드가 그대로 2인으로 읽혀야 한다.
assert.equal(first.record.expectedParticipants, 2);

console.log("match-pairing tests: pass");
