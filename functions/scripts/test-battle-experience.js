"use strict";
const assert = require("node:assert/strict");
const {test} = require("node:test");
const {confirmedBattleExperienceOutcome: judge} = require("../lib/account/battleExperience");

const ranked = {
  seedSource: "server", status: "confirmed", mode: "pvp", expectedParticipants: 2,
  participantUids: ["alice", "bob"], payouts: {alice: {won: true}, bob: {won: false}},
};
test("battle XP uses each participant's server payout and ignores claimed outcome", () => {
  assert.deepEqual(judge({...ranked, won: false}, "alice"), {allow: true, won: true});
  assert.deepEqual(judge({...ranked, won: true}, "bob"), {allow: true, won: false});
  assert.equal(judge(ranked, "outsider").allow, false);
});
test("pending, void, legacy, and missing payout cannot award experience", () => {
  for (const change of [{status: "pending"}, {status: "flagged"}, {seedSource: "client"},
    {mode: "solo", resultProtocol: 0}, {payouts: {}}, {payouts: {alice: {won: "true"}}}]) {
    assert.equal(judge({...ranked, ...change}, "alice").allow, false);
  }
});
const adventure = {
  ...ranked, mode: "solo", resultProtocol: 1, expectedParticipants: 1, participantUids: ["alice"],
  adventureNodeId: "chapter1.node1", serverSimulation: {ok: true, draw: false, winnerOwner: 0},
};
test("adventure XP uses verified replay for win, loss, and draw", () => {
  assert.deepEqual(judge(adventure, "alice"), {allow: true, won: true});
  assert.deepEqual(judge({...adventure, serverSimulation: {ok: true, draw: false, winnerOwner: 1}}, "alice"),
    {allow: true, won: false});
  assert.deepEqual(judge({...adventure, serverSimulation: {ok: true, draw: true, winnerOwner: -1}}, "alice"),
    {allow: true, won: false});
});
test("adventure ownership and successful replay cannot be replaced by payout claims", () => {
  for (const change of [{mode: "pvp"}, {expectedParticipants: 2}, {participantUids: ["alice", "bob"]},
    {serverSimulation: {ok: false}}, {serverSimulation: {ok: true, winnerOwner: 0}},
    {serverSimulation: {ok: true, draw: false, winnerOwner: 99}}]) {
    assert.equal(judge({...adventure, ...change}, "alice").allow, false);
  }
});
