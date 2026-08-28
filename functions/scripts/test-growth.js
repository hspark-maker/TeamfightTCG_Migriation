// 카드 성장·먹이 순수 회귀. 에뮬레이터 없이 lib/ 를 직접 require 한다(test-fresh-account.js 관용구).
//
// 여기서 지키는 것은 "서버가 만든 cardGrowth 슬롯이 클라 CardGrowthManager 와 같은 모양인가" 다.
// 가지치기 조건이 갈리면 다음 클라 저장이 문서를 다시 흔들어 revision 이 헛돈다.
const assert = require("node:assert/strict");
const {BASE_LEVEL, SNACK_MAX, readGrowthEntries, growthSlot,
  addSnack, canAffordSnack, spendSnack, applyLimitBreak} = require("../lib/growth/cardGrowth.js");

// 팩 개봉이 하는 일: 뽑힌 카드를 순회하며 적립하고 슬롯으로 조립한다.
const openPack = (entries, drawn) =>
  growthSlot(drawn.reduce((acc, card) => addSnack(acc, card.cardId, card.snack), entries));

// ── 읽기: 3필드 정규화 ───────────────────────────────────────────────────────
assert.deepEqual(readGrowthEntries({entries: {8: {level: 2}}}), {8: {level: 2, snack: 0, limitBreak: 0}});
assert.deepEqual(readGrowthEntries({entries: {0: {level: 9}, "-3": {level: 9}}}), {}, "id 0 이하는 버린다");
assert.deepEqual(readGrowthEntries(undefined), {});
assert.deepEqual(readGrowthEntries({entries: {5: {level: "x", snack: 1.7}}}),
  {5: {level: 0, snack: 1, limitBreak: 0}}, "못 읽는 값은 0, 소수는 버림");

// ── 적립: 중복 지급에만 붙는다 ───────────────────────────────────────────────
// 항목이 없으면 level = BASE_LEVEL 로 신설된다. level 0 으로 만들면
// 클라 FlushToData 가 다음 저장에서 그 항목을 통째로 날린다.
assert.deepEqual(openPack({}, [{cardId: 5, isNew: false, snack: 1}]),
  {entries: {5: {level: BASE_LEVEL, snack: 1, limitBreak: 0}}});

// 기존 항목에는 더하고, 음수 간식은 0에서 시작한다(클라 AddSnack 과 같다).
assert.deepEqual(openPack({5: {level: 3, snack: -4, limitBreak: 1}}, [{cardId: 5, isNew: false, snack: 1}]),
  {entries: {5: {level: 3, snack: 1, limitBreak: 1}}});

// 신규 카드에는 간식이 안 붙는다.
assert.deepEqual(openPack({}, [{cardId: 5, isNew: true, snack: 0}]), {entries: {}});

// 한 팩에서 같은 카드가 두 번 중복이면 두 번 다 쌓인다.
assert.equal(openPack({}, [{cardId: 5, snack: 1}, {cardId: 5, snack: 1}]).entries[5].snack, 2);

// 입력 맵을 건드리지 않는다 — 트랜잭션이 재실행되면 원본을 다시 쓴다.
const before = {5: {level: 2, snack: 1, limitBreak: 0}};
addSnack(before, 5, 3);
assert.equal(before[5].snack, 1, "원본은 그대로여야 한다");

// 클라 CardGrowthEntry.Snack 은 int 다 — AddSnack 이 int.MaxValue 에서 자르는 것과 맞춘다.
assert.equal(addSnack({5: {level: 1, snack: SNACK_MAX, limitBreak: 0}}, 5, 10)[5].snack, SNACK_MAX);

// ── 가지치기: 기본값뿐인 항목은 버린다 ──────────────────────────────────────
assert.deepEqual(growthSlot({7: {level: BASE_LEVEL, snack: 0, limitBreak: 0}}), {entries: {}});
assert.deepEqual(growthSlot({7: {level: BASE_LEVEL, snack: 0, limitBreak: 1}}),
  {entries: {7: {level: BASE_LEVEL, snack: 0, limitBreak: 1}}}, "한계돌파만 있어도 남긴다");

// ── 먹이 소비 (한계돌파) ─────────────────────────────────────────────────────
const fed = {5: {level: 1, snack: 6, limitBreak: 0}};
assert.equal(canAffordSnack(fed, 5, 6), true, "같은 값이면 낼 수 있다");
assert.equal(canAffordSnack(fed, 5, 7), false);
assert.equal(canAffordSnack({5: {level: 1, snack: -3, limitBreak: 0}}, 5, 1), false, "음수는 0으로 읽는다");
assert.equal(canAffordSnack({}, 99, 1), false, "기록이 없으면 0");

assert.deepEqual(spendSnack(fed, 5, 4), {5: {level: 1, snack: 2, limitBreak: 0}});
assert.equal(spendSnack(fed, 5, 999)[5].snack, 0, "보유보다 큰 소모는 0에서 멈춘다");

// 차감과 단계 증가는 한 몸이다 — 클라 TryLimitBreak 이 함께 저장한다.
assert.deepEqual(applyLimitBreak(fed, 5, 1, 6), {5: {level: 1, snack: 0, limitBreak: 1}});
assert.equal(fed[5].snack, 6, "원본은 그대로여야 한다");

console.log("test-growth: ok");
