"use strict";

const assert = require("node:assert/strict");
const {test} = require("node:test");
const {qualifiesGuideSynergyBattle, completeGuideSynergyBattle,
  SYNERGY_BATTLE_EVENT} = require("../lib/missions/guideSynergyBattle");
const {judgeMissionClaim} = require("../lib/missions/judgeMissionClaim");
const {commitMissionBumps, commitMissionClaim, applyPeriodReset} = require("../lib/missions/missionStore");
const {evaluateGuideProgress} = require("../lib/missions/guideProgress");

const key = "guide." + SYNERGY_BATTLE_EVENT;
const mission = (id, sortOrder, enabled = true) => ({id, sortOrder, enabled, period: "guide", target: 1,
  event: id === "guide.16" ? SYNERGY_BATTLE_EVENT : "Guide." + id, passExp: 0});
const catalog = [mission("guide.07", 4), mission("guide.04", 5, false), mission("guide.05", 5),
  mission("guide.06", 6), mission("guide.16", 7), mission("guide.08", 8), mission("guide.15", 15)];
const state = () => ({dailyKey: "today", weeklyKey: "week", progress: {}, passExp: 0,
  claimed: {"guide.07": true, "guide.05": true, "guide.06": true}});
const cards = [1, 2, 3, 4, 5, 6].map((id) => ({id,
  synergies: id <= 3 ? "Data_Synergy_Caretaker" : "Data_Synergy_Bulk/Data_Synergy_Brand"}));
const tiers = [{synergyId: "Caretaker", requiredCount: 3}, {synergyId: "Bulk", requiredCount: 2}];
const snapshots = (unlocked = []) => cards.map(({id}) => ({cardId: id, synergyUnlocked: unlocked.includes(id)}));
const qualifies = (current = state(), unlocked = [1, 2, 3]) =>
  qualifiesGuideSynergyBattle(current, catalog, snapshots(unlocked), cards, tiers);

test("only the first enabled unclaimed guide can authorize the battle", () => {
  assert.equal(qualifies(), true);
  for (const id of ["guide.07", "guide.05", "guide.06"]) {
    const before = state();
    delete before.claimed[id];
    assert.equal(qualifies(before), false, id);
  }
  const after = state();
  after.claimed["guide.16"] = true;
  assert.equal(qualifies(after), false);
  assert.equal(qualifiesGuideSynergyBattle(state(), catalog.filter((m) => m.id !== "guide.16"),
    snapshots([1, 2, 3]), cards, tiers), false);
});

test("server-approved growth and each authored synergy threshold decide eligibility", () => {
  for (const ids of [[], [1], [1, 2], [1, 2, 4], [3, 5]]) assert.equal(qualifies(state(), ids), false);
  for (const ids of [[1, 2, 3], [4, 5], [4, 5, 6]]) assert.equal(qualifies(state(), ids), true);
  assert.equal(qualifiesGuideSynergyBattle(state(), catalog, snapshots([1, 2]), [...cards, cards[0]], tiers), false);
  assert.equal(qualifiesGuideSynergyBattle(state(), catalog, snapshots([1, 2, 3]), cards, []), false);
});

test("eligibility at lock persists across later activation, growth and deck changes", () => {
  const before = state();
  delete before.claimed["guide.06"];
  const oldApproval = {guideSynergyBattleEligible: qualifies(before)};
  const current = state();
  completeGuideSynergyBattle(current, oldApproval);
  assert.equal(current.progress[key], undefined);

  const approval = {guideSynergyBattleEligible: qualifies()};
  const later = state();
  later.claimed["guide.08"] = true;
  completeGuideSynergyBattle(later, approval);
  assert.equal(later.progress[key], 1);
  assert.equal(evaluateGuideProgress({}, [], catalog, later.progress)[key], 1);
});

test("legacy, false and malformed approval flags never count", () => {
  for (const approval of [undefined, null, {}, {guideSynergyBattleEligible: false},
    {guideSynergyBattleEligible: 1}, {guideSynergyBattleEligible: "true"}]) {
    const current = state();
    completeGuideSynergyBattle(current, approval);
    assert.equal(current.progress[key], undefined);
  }
});

test("rank/adventure outcomes share permanent completion, independent of daily unlock and TriggerSynergy", () => {
  for (const mode of ["rank", "adventure"]) for (const outcome of ["win", "loss", "draw"]) {
    const current = state();
    current.progress["daily.TriggerSynergy"] = 2;
    completeGuideSynergyBattle(current, {guideSynergyBattleEligible: true});
    completeGuideSynergyBattle(current, {guideSynergyBattleEligible: true});
    let saved;
    const bump = {state: current, unlocked: false, ref: {}, period: {daily: "today", weekly: "week"}};
    commitMissionBumps({set(ref, value) { saved = value; }}, bump, [{event: "BattleCompleted", amount: 1}], 0);
    assert.equal(saved.progress[key], 1, mode + " " + outcome);
    assert.equal(saved.progress["daily.TriggerSynergy"], 2);
    assert.equal(saved.progress["daily." + SYNERGY_BATTLE_EVENT], undefined);
  }
});

test("retired claim and later-act claims survive; replacement reward can be claimed only once", () => {
  for (const retiredClaimed of [false, true]) {
    const current = state();
    current.claimed["guide.04"] = retiredClaimed;
    current.claimed["guide.08"] = true;
    current.claimed["guide.15"] = true;
    assert.equal(qualifies(current), true);
    assert.equal(judgeMissionClaim("guide.04", current, catalog).reason, "MissionDisabled");
    completeGuideSynergyBattle(current, {guideSynergyBattleEligible: true});
    assert.equal(judgeMissionClaim("guide.16", current, catalog).allow, true);
    commitMissionClaim({set() {}}, {state: current, period: {}, ref: {}}, "guide.16", 0, 0);
    assert.equal(judgeMissionClaim("guide.16", current, catalog).reason, "AlreadyClaimed");
    const reset = applyPeriodReset(current, {daily: "tomorrow", weekly: "next"});
    assert.equal(reset.progress[key], 1);
    for (const id of ["guide.08", "guide.15", "guide.16"]) assert.equal(reset.claimed[id], true);
  }
});

test("lockDeck first approval snapshots qualification; retry neither reads missions nor rewrites approval", async () => {
  const Module = require("node:module");
  const originalLoad = Module._load;
  const commandPath = require.resolve("../lib/commands/lockDeck");
  const hash = "a".repeat(64), matchId = "b".repeat(32), seedHex = "c".repeat(16);
  const root = "envs/test/";
  const docs = new Map();
  let writes = [], reads = [], rejectValidation = false;
  const db = {doc: (path) => ({path})};
  const tx = {
    async get(ref) {
      assert.equal(writes.length, 0, "read after write");
      reads.push(ref.path);
      const data = docs.get(ref.path);
      return {exists: data != null, data: () => data, get: (key) => data?.[key]};
    },
    set(ref, value, options) { writes.push({ref, value, options}); },
  };
  const timestamp = (ms) => ({toMillis: () => ms});
  class HttpsError extends Error {
    constructor(code, message) { super(message); this.code = code; }
  }
  const stubs = {
    "firebase-functions/v2/https": {onCall: (options, run) => run, HttpsError},
    "firebase-functions/logger": {info() {}, warn() {}, error() {}},
    "firebase-admin/firestore": {FieldValue: {serverTimestamp: () => 0},
      Timestamp: {now: () => timestamp(100), fromMillis: timestamp}},
    "../firebaseApp": {db},
    "../observability/countedTransaction": {async withCountedTransaction(name, run) {
      writes = []; reads = [];
      const result = await run(tx);
      for (const {ref, value, options} of writes)
        docs.set(ref.path, options?.merge ? {...docs.get(ref.path), ...value} : value);
      return result;
    }},
    "../observability/analyticsEvent": {recordEvent() {}},
    "../deckValidation": {computeDeckHash: () => hash, parseCardSpecRow: (row) => row,
      validateDeckShape: () => null, validateDeckSnapshots: () =>
        rejectValidation ? {ok: false, code: "synergy_mismatch", cardId: 1} : {ok: true}},
    "../growth/enhanceRules": {parseCardEnhanceRule: () => ({maxLimitBreak: 1, maxLevel: 3}),
      authoredMaxLimitBreak: () => 1, parseCardEnhanceOverrides: () => [], cardEnhanceStep: () => ({})},
    "../growth/limitBreakTable": {parseLimitBreakCurve: () => ({maxStage: 1})},
    "../missions/missionSpec": {readMissionCatalog: async () => catalog},
    "../specs/specBlobReader": {readSpecRows: async (env, table) =>
      table === "Card" ? cards : table === "SynergyTierDef" ? tiers : [{}]},
  };
  Module._load = function(request, parent, isMain) {
    if (Object.hasOwn(stubs, request)) return stubs[request];
    return originalLoad.call(this, request, parent, isMain);
  };
  try {
    delete require.cache[commandPath];
    const {lockDeck} = require(commandPath);
    const payload = {env: "test", matchId, seedSource: "server", seedHex, rulesetVersion: 1,
      ownerIndex: 0, cardDataVersion: hash, contentFingerprint: hash, deckHash: hash,
      cardSnapshots: snapshots([1, 2, 3]).map((card) => ({...card, level: 3, hpBonus: 0,
        evolutionStage: 0, unlockedKeywords: 0}))};
    const matchPath = root + "matches/" + matchId;
    const missionsPath = root + "users/player/missions/current";
    const reset = (missionState) => {
      docs.set(matchPath, {expectedParticipants: 1, participantUids: ["player"], ownerIndexByUid: {player: 0},
        seedSource: "server", seedHex, rulesetVersion: 1, cardDataVersion: hash});
      docs.set(root + "users/player/save/current", {revision: 3});
      docs.set(missionsPath, missionState);
    };
    for (const eligible of [false, true]) {
      const current = state();
      if (!eligible) delete current.claimed["guide.06"];
      reset(current);
      assert.deepEqual(await lockDeck({auth: {uid: "player"}, data: payload}),
        {status: "approved", idempotent: false});
      assert.ok(reads.includes(missionsPath));
      assert.equal(docs.get(matchPath).approvals.player.guideSynergyBattleEligible, eligible);
      docs.set(missionsPath, eligible ? {claimed: {"guide.16": true}} : state());
      assert.deepEqual(await lockDeck({auth: {uid: "player"}, data: payload}),
        {status: "approved", idempotent: true});
      assert.equal(reads.includes(missionsPath), false);
      assert.equal(writes.length, 0);
      assert.equal(docs.get(matchPath).approvals.player.guideSynergyBattleEligible, eligible);
    }
    reset(state());
    rejectValidation = true;
    assert.equal((await lockDeck({auth: {uid: "player"}, data: payload})).status, "rejected");
    assert.equal(docs.get(matchPath).approvals, undefined);
    assert.equal(reads.includes(missionsPath), false);
  } finally {
    Module._load = originalLoad;
    delete require.cache[commandPath];
  }
});
