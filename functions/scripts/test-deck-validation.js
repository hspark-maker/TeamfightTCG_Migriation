const assert = require("node:assert/strict");
const {
  computeDeckHash,
  LIMIT_BREAK_CURVE_SHRUNK,
  parseCardSpecRow,
  validateDeckShape,
  validateDeckSnapshots,
} = require("../lib/deckValidation.js");
// 한계돌파 곡선은 필수 인자다. 픽스처를 상수 맵으로 손수 적으면 파서 버그가 회귀를 그대로 통과하므로
// 여기서도 표 행을 파서에 태워 만든다(hpGain 1 · snackCost 단계값 = 클라 GrowthRules 하드코딩과 같은 곡선).
const {parseLimitBreakCurve} = require("../lib/growth/limitBreakTable.js");
const LIMIT_BREAK = parseLimitBreakCurve([
  {id: 1, stage: 1, hpGain: 1, snackCost: 1},
  {id: 2, stage: 2, hpGain: 1, snackCost: 2},
  {id: 3, stage: 3, hpGain: 1, snackCost: 3},
], 3);
assert.equal(LIMIT_BREAK.maxStage, 3);

const spec = parseCardSpecRow({
  id: 12,
  keywords: "Ranged Taunt",
  keywordUnlockLevel: 3,
  hp2: 2,
  hp3: 3,
  hp4: 4,
});
assert.ok(spec);
const specs = new Map([[12, spec]]);
const save = {
  ownership: {cardIds: [12]},
  cardGrowth: {entries: {"12": {level: 3, snack: 0, limitBreak: 2}}},
  keywordGrowth: {levels: {"1": 4, "8": 3}},
};
const valid = [{
  cardId: 12,
  level: 3,
  hpBonus: 14,
  evolutionStage: 1,
  unlockedKeywords: 9,
  synergyUnlocked: true,
}];

assert.deepEqual(validateDeckSnapshots(valid, specs, save, LIMIT_BREAK), {ok: true});
assert.equal(
  validateDeckSnapshots(valid, specs, {
    ...save,
    keywordGrowth: {levels: {Ranged: 4, Taunt: 3}},
  }, LIMIT_BREAK).code,
  "hp_bonus_mismatch"
);
assert.equal(
  validateDeckSnapshots([{...valid[0], cardId: 99}], specs, save, LIMIT_BREAK).code,
  "card_not_found"
);
assert.equal(
  validateDeckSnapshots(valid, specs, {...save, ownership: {cardIds: []}}, LIMIT_BREAK).code,
  "card_not_owned"
);
assert.equal(
  validateDeckSnapshots([{...valid[0], hpBonus: 13}], specs, save, LIMIT_BREAK).code,
  "hp_bonus_mismatch"
);
assert.equal(
  validateDeckSnapshots([{...valid[0], level: 4}], specs, save, LIMIT_BREAK).code,
  "level_mismatch"
);
assert.match(computeDeckHash(valid), /^[0-9a-f]{64}$/);

// 위조와 표 사고는 갈라 나가야 한다 — 호출부(lockDeck)가 앞은 rejectLock, 뒤는 unavailable 로 접는다.
// 코드 천장(3) 초과는 어떤 표로도 나올 수 없는 값이라 덱 거절이다.
const forgedSave = {
  ...save,
  cardGrowth: {entries: {"12": {level: 3, snack: 0, limitBreak: 4}}},
};
assert.equal(
  validateDeckSnapshots(valid, specs, forgedSave, LIMIT_BREAK).code,
  "saved_growth_out_of_range"
);

// 곡선만 짧아진 자리(= 표가 깎였다). 저장값 2 는 서버가 이미 지급한 단계이므로 유저를 태우지 않는다.
const shrunkCurve = parseLimitBreakCurve([
  {id: 1, stage: 1, hpGain: 1, snackCost: 1},
], 1);
assert.equal(shrunkCurve.maxStage, 1);
assert.equal(
  validateDeckSnapshots(valid, specs, save, shrunkCurve).code,
  LIMIT_BREAK_CURVE_SHRUNK
);
// 곡선이 깎여도 천장 초과는 여전히 위조다(두 갈래가 겹칠 때 위조가 이긴다).
assert.equal(
  validateDeckSnapshots(valid, specs, forgedSave, shrunkCurve).code,
  "saved_growth_out_of_range"
);

const baseLevel = [{
  cardId: 12,
  level: 1,
  hpBonus: 0,
  evolutionStage: 0,
  unlockedKeywords: 0,
  synergyUnlocked: false,
}];
const baseSave = {
  ownership: {cardIds: [12]},
  cardGrowth: {entries: {}},
  keywordGrowth: {levels: {}},
};
assert.deepEqual(validateDeckSnapshots(baseLevel, specs, baseSave, LIMIT_BREAK), {ok: true});

const sixCards = Array.from({length: 6}, (_, index) => ({
  ...baseLevel[0],
  cardId: index + 1,
}));
assert.equal(validateDeckShape(sixCards), null);
assert.match(validateDeckShape(sixCards.slice(0, 5)), /^deck_size:/);
assert.match(validateDeckShape([...sixCards.slice(0, 5), sixCards[0]]), /^duplicate_card:/);

// cardId 오름차순 규약: 순서만 다른 같은 덱은 거절한다.
const swapped = [...sixCards];
[swapped[0], swapped[1]] = [swapped[1], swapped[0]];
assert.match(validateDeckShape(swapped), /^deck_order:/);
assert.equal(validateDeckShape([...swapped].sort((a, b) => a.cardId - b.cardId)), null);

console.log("deck-validation tests passed");
