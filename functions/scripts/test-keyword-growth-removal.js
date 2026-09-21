"use strict";
const assert = require("node:assert/strict");
const {test} = require("node:test");
const {validateDeckSnapshots} = require("../lib/deckValidation");
const {createHash} = require("node:crypto");
const {onboardingFingerprint} = require("../lib/save/onboardingOperation");

const specs = new Map([[1, {
  id: 1, maxHp: 10, keywords: 3, keywordUnlockLevel: 3,
  defaultEvolutionStage: 0, synergies: [], hp2: 4, hp3: 6, hp4: 8,
}]]);
const enhance = new Map([[3, {level: 3, cost: 12, currency: "Shard", successPermille: 1000}]]);
function save(level, limitBreak = 0, shardProgress = 0) {
  return {ownership: {cardIds: [1]}, cardGrowth: {entries: {1: {level, limitBreak, shardProgress}}}};
}
function snapshot(level, hpBonus) {
  return {cardId: 1, level, hpBonus, evolutionStage: level >= 4 ? 2 : level >= 3 ? 1 : 0,
    unlockedKeywords: level >= 3 ? 3 : 0, synergyUnlocked: level >= 3};
}
function validate(card, document) {
  return validateDeckSnapshots([card], specs, document, enhance);
}

test("retired keyword commands retain their old fingerprint for read-only recovery", () => {
  const expected = createHash("sha256").update(JSON.stringify({command: "enhanceKeyword",
    args: {keyword: 1, freeShot: true}})).digest("hex");
  assert.equal(onboardingFingerprint("enhanceKeyword", {keyword: "1", freeShot: true, env: "test"}), expected);
  assert.notEqual(onboardingFingerprint("enhanceKeyword", {keyword: 1, freeShot: false}), expected);
  assert.notEqual(onboardingFingerprint("enhanceKeyword", {keyword: 2, freeShot: true}), expected);
});

test("legacy keyword growth is ignored whether populated, absent, or malformed", () => {
  for (const legacy of [{}, {keywordGrowth: {levels: {}}},
    {keywordGrowth: {levels: {1: 10, 2: 5}}}, {keywordGrowth: null}]) {
    assert.deepEqual(validate(snapshot(4, 18), {...save(4, 2), ...legacy}), {ok: true});
  }
});

test("old keyword and limit-break HP bonuses are rejected while card growth remains required", () => {
  const document = {...save(4, 2), keywordGrowth: {levels: {1: 10, 2: 5}}};
  assert.deepEqual(validate(snapshot(4, 41), document), {ok: false, code: "hp_bonus_mismatch", cardId: 1});
  assert.deepEqual(validate(snapshot(4, 26), document), {ok: false, code: "hp_bonus_mismatch", cardId: 1});
  assert.deepEqual(validate(snapshot(4, 18), document), {ok: true});
});

test("partial shard progress and keyword unlock checks remain active", () => {
  assert.deepEqual(validate(snapshot(2, 7), save(2, 0, 6)), {ok: true});
  assert.deepEqual(validate(snapshot(1, 0), save(1)), {ok: true});
  assert.deepEqual(validate({...snapshot(2, 7), unlockedKeywords: 3}, save(2, 0, 6)),
    {ok: false, code: "keywords_mismatch", cardId: 1});
  assert.deepEqual(validate({...snapshot(3, 10), unlockedKeywords: 0}, save(3)),
    {ok: false, code: "keywords_mismatch", cardId: 1});
});
