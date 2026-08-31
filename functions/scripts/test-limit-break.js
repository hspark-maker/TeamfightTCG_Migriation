// 한계돌파 순수 모듈 회귀. 에뮬레이터 없이 lib/ 를 직접 require 한다(test-enhance.js 관용구).
//
// 여기서 지키는 것은 다섯 가지다.
//  1) readSpecRows 는 id 로만 정렬한다 — 저작 순서가 stage 순서와 달라도 곡선이 제대로 선다.
//  2) snackCost 는 1 미만이면 1로 올라간다 — 클램프가 없으면 공란 저작이 공짜 한계돌파가 된다.
//  3) hpGain 은 누적이다 — "3단계 카드는 1~3단계 합을 받는다"가 표 툴팁의 규약이다.
//  4) 단계가 끊기면 fail-closed — 구멍 위 단계의 합은 뜻이 없으므로 거기까지로 상한을 깎는다.
//  5) 한계돌파 체력 가산의 소비자가 둘(callable · deckValidation)인데 **한 곡선을 본다**.
//
// 표 값에 의존하지 않는다 — 아래 행은 전부 이 파일이 만든 합성 픽스처다.
const assert = require("node:assert/strict");
const {
  parseLimitBreakCurve,
  limitBreakStep,
  limitBreakHpBonus,
} = require("../lib/growth/limitBreakTable.js");
const {
  LIMIT_BREAK_STAGE_CEILING,
  parseCardEnhanceRule,
} = require("../lib/growth/enhanceRules.js");
const {
  BASE_LEVEL,
  canAffordSnack,
  applyLimitBreak,
  growthSlot,
  readGrowthEntries,
} = require("../lib/growth/cardGrowth.js");
const {
  parseCardSpecRow,
  validateDeckSnapshots,
} = require("../lib/deckValidation.js");

// ── 파서: id 순서와 stage 순서가 달라도 곡선이 선다 ─────────────────────────
{
  const curve = parseLimitBreakCurve([
    {id: 1, stage: 3, hpGain: 30, snackCost: 9},
    {id: 2, stage: 1, hpGain: 10, snackCost: 3},
    {id: 3, stage: 2, hpGain: 20, snackCost: 6},
  ], 3);

  assert.equal(curve.maxStage, 3);
  assert.deepEqual(limitBreakStep(curve, 1), {stage: 1, hpGain: 10, snackCost: 3});
  assert.deepEqual(limitBreakStep(curve, 2), {stage: 2, hpGain: 20, snackCost: 6});
  assert.deepEqual(limitBreakStep(curve, 3), {stage: 3, hpGain: 30, snackCost: 9});
}

// ── 파서: 같은 stage 가 둘이면 id 가 작은 쪽이 이긴다 ───────────────────────
{
  const curve = parseLimitBreakCurve([
    {id: 1, stage: 1, hpGain: 10, snackCost: 3},
    {id: 2, stage: 1, hpGain: 999, snackCost: 999},
  ], 3);

  assert.equal(curve.maxStage, 1, "stage 2 가 없으므로 상한은 1이다");
  assert.equal(limitBreakStep(curve, 1).hpGain, 10);
  assert.equal(limitBreakStep(curve, 1).snackCost, 3);
}

// ── 파서: snackCost 는 1 미만이면 1로 올라간다 ──────────────────────────────
{
  const curve = parseLimitBreakCurve([
    {id: 1, stage: 1, hpGain: 1, snackCost: 0},
    {id: 2, stage: 2, hpGain: 1},
    {id: 3, stage: 3, hpGain: 1, snackCost: -5},
  ], 3);

  assert.equal(limitBreakStep(curve, 1).snackCost, 1, "0 은 공짜가 아니다");
  assert.equal(limitBreakStep(curve, 2).snackCost, 1, "공란도 공짜가 아니다");
  assert.equal(limitBreakStep(curve, 3).snackCost, 1, "음수도 공짜가 아니다");
  assert.equal(limitBreakStep(curve, 1).hpGain, 1);
}

// ── 파서: hpGain 음수는 0 으로 ──────────────────────────────────────────────
{
  const curve = parseLimitBreakCurve([{id: 1, stage: 1, hpGain: -4, snackCost: 2}], 3);
  assert.equal(limitBreakStep(curve, 1).hpGain, 0);
}

// ── 누적: 1~stage 의 합 ─────────────────────────────────────────────────────
{
  const [h1, h2, h3] = [2, 3, 5];
  const curve = parseLimitBreakCurve([
    {id: 1, stage: 1, hpGain: h1, snackCost: 1},
    {id: 2, stage: 2, hpGain: h2, snackCost: 2},
    {id: 3, stage: 3, hpGain: h3, snackCost: 3},
  ], 3);

  assert.equal(limitBreakHpBonus(curve, 0), 0, "미돌파는 가산이 없다");
  assert.equal(limitBreakHpBonus(curve, -1), 0, "음수 단계도 0");
  assert.equal(limitBreakHpBonus(curve, 1), h1);
  assert.equal(limitBreakHpBonus(curve, 2), h1 + h2);
  assert.equal(limitBreakHpBonus(curve, 3), h1 + h2 + h3);
  assert.equal(limitBreakHpBonus(curve, 99), h1 + h2 + h3, "상한 위 단계는 상한까지만 센다");
}

// ── 파서: 상한 초과 행은 무시된다 ───────────────────────────────────────────
{
  const curve = parseLimitBreakCurve([
    {id: 1, stage: 1, hpGain: 1, snackCost: 1},
    {id: 2, stage: 2, hpGain: 1, snackCost: 2},
    {id: 3, stage: 3, hpGain: 1, snackCost: 3},
    {id: 4, stage: 4, hpGain: 100, snackCost: 1},
  ], 3);

  assert.equal(curve.maxStage, 3);
  assert.equal(limitBreakStep(curve, 4), null, "상한 밖 단계는 스텝이 없다 = MaxStage 거절");
  assert.equal(limitBreakHpBonus(curve, 4), 3, "무시된 행이 합에 들어오면 안 된다");
  assert.equal(limitBreakStep(curve, 0), null);
  assert.equal(limitBreakStep(curve, -1), null);
}

// ── 파서: 결손은 fail-closed ────────────────────────────────────────────────
{
  const holed = parseLimitBreakCurve([
    {id: 1, stage: 1, hpGain: 1, snackCost: 1},
    {id: 2, stage: 3, hpGain: 1, snackCost: 3},
  ], 3);
  assert.equal(holed.maxStage, 1, "stage 2 가 비면 그 위는 합이 뜻을 잃는다");
  assert.equal(limitBreakStep(holed, 3), null, "구멍 위 단계는 맵에서도 지운다");
  assert.equal(limitBreakHpBonus(holed, 3), 1);

  assert.equal(parseLimitBreakCurve([{id: 1, stage: 2, hpGain: 1, snackCost: 1}], 3), null,
    "stage 1 이 없으면 곡선이 서지 않는다");
  assert.equal(parseLimitBreakCurve([], 3), null, "빈 표는 곡선이 아니다");
  assert.equal(parseLimitBreakCurve([{id: 1, stage: 1, hpGain: 1, snackCost: 1}], 0), null,
    "축이 닫혀 있으면 곡선도 없다");
}

// ── 규칙: maxLimitBreak 는 천장에서 자르고, 공란이 rule 을 죽이지 않는다 ────
{
  const huge = parseCardEnhanceRule([{maxLevel: 4, baseEnhanceCost: 25, maxLimitBreak: 99}]);
  assert.equal(huge.maxLimitBreak, LIMIT_BREAK_STAGE_CEILING, "표가 더 크게 말해도 코드 천장에서 자른다");

  const blank = parseCardEnhanceRule([{maxLevel: 4, baseEnhanceCost: 25}]);
  assert.notEqual(blank, null, "한계돌파 열이 비었다고 카드 강화 전체가 죽으면 안 된다");
  assert.equal(blank.maxLimitBreak, 0, "0 = 축이 닫혀 있다. 거절은 호출부가 한다");
  assert.equal(blank.maxLevel, 4, "강화 곡선은 그대로 서 있어야 한다");

  const negative = parseCardEnhanceRule([{maxLevel: 4, baseEnhanceCost: 25, maxLimitBreak: -3}]);
  assert.equal(negative.maxLimitBreak, 0, "음수도 닫힌 축으로 읽는다");
}

// ── 판정: 간식 부족 · 최대 단계 ─────────────────────────────────────────────
{
  const curve = parseLimitBreakCurve([
    {id: 1, stage: 1, hpGain: 1, snackCost: 1},
    {id: 2, stage: 2, hpGain: 1, snackCost: 2},
    {id: 3, stage: 3, hpGain: 1, snackCost: 3},
  ], 3);

  const entries = readGrowthEntries({entries: {7: {level: 2, snack: 2, limitBreak: 1}}});
  const next = limitBreakStep(curve, entries["7"].limitBreak + 1);
  assert.equal(next.snackCost, 2);
  assert.equal(canAffordSnack(entries, 7, next.snackCost), true, "딱 맞는 보유량은 통과다");
  assert.equal(canAffordSnack(entries, 7, next.snackCost + 1), false);
  assert.equal(canAffordSnack(entries, 9, 1), false, "기록 없는 카드는 간식 0 = NotEnoughSnack");

  assert.equal(limitBreakStep(curve, curve.maxStage + 1), null, "최대 단계에서는 스텝이 없다 = MaxStage");
}

// ── 코덱: 차감과 단계 증가가 한 몸이고 level 이 안 지워진다 ─────────────────
{
  const before = {7: {level: 3, snack: 5, limitBreak: 1}};
  const after = applyLimitBreak(before, 7, 2, 2);

  assert.deepEqual(after["7"], {level: 3, snack: 3, limitBreak: 2},
    "간식 차감과 단계 증가는 함께 가고 강화 레벨은 그대로다");
  assert.deepEqual(before["7"], {level: 3, snack: 5, limitBreak: 1},
    "입력 맵은 그대로여야 한다 — 트랜잭션이 재실행되면 원본을 다시 쓴다");
}

// ── 코덱: growthSlot 가지치기가 한계돌파만 있는 항목을 살린다 ───────────────
{
  const only = applyLimitBreak({7: {level: BASE_LEVEL, snack: 1, limitBreak: 0}}, 7, 1, 1);
  assert.deepEqual(growthSlot(only), {entries: {7: {level: BASE_LEVEL, snack: 0, limitBreak: 1}}},
    "미강화·간식 0 이어도 한계돌파가 있으면 남아야 한다");

  assert.deepEqual(growthSlot({9: {level: BASE_LEVEL, snack: 0, limitBreak: 0}}), {entries: {}},
    "기본값뿐인 항목은 계속 버린다");
}

// ── 소비자 둘이 한 곡선을 본다 (limitBreakHpBonus ↔ deckValidation) ────────
{
  const curve = parseLimitBreakCurve([
    {id: 1, stage: 1, hpGain: 2, snackCost: 1},
    {id: 2, stage: 2, hpGain: 3, snackCost: 2},
    {id: 3, stage: 3, hpGain: 5, snackCost: 3},
  ], 3);

  // 키워드가 없는 카드라 체력 가산 = 레벨 곡선 + 한계돌파 누적뿐이다.
  const spec = parseCardSpecRow({
    id: 12, keywords: "", keywordUnlockLevel: 9, hp2: 2, hp3: 3, hp4: 4,
  });
  const specs = new Map([[12, spec]]);

  for (const stage of [0, 1, 2, 3]) {
    const save = {
      ownership: {cardIds: [12]},
      cardGrowth: {entries: {"12": {level: 2, snack: 0, limitBreak: stage}}},
      keywordGrowth: {levels: {}},
    };
    const snapshot = [{
      cardId: 12,
      level: 2,
      // hp2(=2) + 한계돌파 누적. 이 값을 검증기가 스스로 다시 세어 맞춰야 한다.
      hpBonus: 2 + limitBreakHpBonus(curve, stage),
      evolutionStage: 0,
      unlockedKeywords: 0,
      synergyUnlocked: false,
    }];
    assert.deepEqual(validateDeckSnapshots(snapshot, specs, save, curve), {ok: true},
      `stage ${stage} 에서 두 소비자의 체력 가산이 갈리면 덱 잠금이 hp_bonus_mismatch 로 막힌다`);

    assert.equal(
      validateDeckSnapshots([{...snapshot[0], hpBonus: snapshot[0].hpBonus + 1}], specs, save, curve).code,
      "hp_bonus_mismatch");
  }

  // 곡선 상한 밖 단계가 세이브에 적혀 있으면 범위 밖으로 거절한다.
  const overSave = {
    ownership: {cardIds: [12]},
    cardGrowth: {entries: {"12": {level: 2, snack: 0, limitBreak: curve.maxStage + 1}}},
    keywordGrowth: {levels: {}},
  };
  assert.equal(
    validateDeckSnapshots([{
      cardId: 12, level: 2, hpBonus: 2, evolutionStage: 0, unlockedKeywords: 0, synergyUnlocked: false,
    }], specs, overSave, curve).code,
    "saved_growth_out_of_range");
}

console.log("test-limit-break: ok");
