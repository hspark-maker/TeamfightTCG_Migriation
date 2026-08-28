// 강화(카드·키워드) 순수 모듈 회귀. 에뮬레이터 없이 lib/ 를 직접 require 한다(test-claim-reward.js 관용구).
//
// 여기서 지키는 것은 네 가지다.
//  1) 실측 저작(CardEnhance 25/75/150 · KeywordEnhance 5/10/15…)이 그대로 재현된다 —
//     화면이 보여 준 값과 실제 차감이 갈리면 유저는 도둑맞은 것으로 본다.
//  2) 상한 밖은 null 이다 — 만렙에서 스텝이 나오면 돈만 나가고 레벨이 안 오른다.
//  3) 지원 목록 밖 키워드는 곡선에서 배제된다 — 클라가 못 읽는 키에 레벨을 씌우면 진행도가 증발한다.
//  4) 튜토리얼 무료 한 방은 축별로 갈리고 다른 축의 낙인을 지우지 않는다.
const assert = require("node:assert/strict");
const {
  PERMILLE,
  CARD_MAX_LEVEL_CEILING,
  parseCardEnhanceRule,
  parseCardEnhanceOverrides,
  cardEnhanceStep,
  parseKeywordEnhanceRules,
  keywordEnhanceStep,
  rollSucceeded,
} = require("../lib/growth/enhanceRules.js");
const {
  BASE_LEVEL,
  readGrowthEntries,
  levelOfCard,
  applyEnhanceLevel,
  growthSlot,
} = require("../lib/growth/cardGrowth.js");
const {
  KEYWORD_MAX_LEVEL,
  readKeywordLevels,
  levelOfKeyword,
  setKeywordLevel,
  keywordGrowthSlot,
} = require("../lib/growth/keywordGrowth.js");
const {
  GRANT_SCHEMA_VERSION,
  grantsRef,
  readGrants,
  hasFreeShot,
  writeGrantUsed,
} = require("../lib/growth/tutorialGrants.js");

// ── 실측 스펙 행 (envs/test 에서 확인한 저작 그대로) ─────────────────────────
const CARD_RULE_ROWS = [{
  id: 1, baseEnhanceCost: 25, costGrowthPerLevel: 50, baseSuccessPermille: 1000,
  rateDropPerLevelPermille: 0, maxLevel: 4, hpPerLevel: 4, maxLimitBreak: 3,
}];
const CARD_ENHANCE_ROWS = [
  {id: 1, level: 2, cost: 25, costCurrency: "Shard", successPermille: 1000},
  {id: 2, level: 3, cost: 75, costCurrency: "Shard", successPermille: 1000},
  {id: 3, level: 4, cost: 150, costCurrency: "Shard", successPermille: 1000},
];
const KEYWORD_ROWS = ["Ranged", "Peerless", "Execution", "Taunt", "Cunning", "Healer"].map(
  (keyword, index) => ({
    id: index + 1, keyword, baseCost: 5, costGrowthPerLevel: 5, maxLevel: 10, costCurrency: "Energy",
  }));

const RANGED = 1;
const PEERLESS = 2;
const MARK = 32;

// ── 카드: 저작 오버라이드가 곡선을 이긴다 ───────────────────────────────────
{
  const rule = parseCardEnhanceRule(CARD_RULE_ROWS);
  const overrides = parseCardEnhanceOverrides(CARD_ENHANCE_ROWS);

  assert.equal(rule.maxLevel, 4);
  assert.deepEqual(cardEnhanceStep(rule, overrides, 2),
    {level: 2, currency: "Shard", cost: 25, successPermille: 1000});
  assert.equal(cardEnhanceStep(rule, overrides, 3).cost, 75);
  assert.equal(cardEnhanceStep(rule, overrides, 4).cost, 150, "실측 레벨 4 = 150 (선형 폴백 125 가 아니다)");

  // 상한 밖·바닥 아래는 스텝이 없다 = 만렙 거절(MaxLevel).
  assert.equal(cardEnhanceStep(rule, overrides, 5), null);
  assert.equal(cardEnhanceStep(rule, overrides, BASE_LEVEL), null);
  assert.equal(cardEnhanceStep(rule, overrides, 0), null);
}

// ── 카드: 오버라이드가 없으면 선형 폴백 ─────────────────────────────────────
{
  const rule = parseCardEnhanceRule(CARD_RULE_ROWS);
  const none = new Map();
  assert.equal(cardEnhanceStep(rule, none, 2).cost, 25);
  assert.equal(cardEnhanceStep(rule, none, 3).cost, 75);
  // 폴백은 레벨 4 에서 클라 계단 곡선과 갈린다(125 vs 150) — 그래서 전 레벨이 오버라이드로 저작돼 있다.
  assert.equal(cardEnhanceStep(rule, none, 4).cost, 125);
  assert.equal(cardEnhanceStep(rule, none, 4).currency, "Shard", "재화 열이 없는 폴백은 조각이다");
}

// ── 카드: 규칙을 못 읽으면 null (= RuleUnavailable) ─────────────────────────
assert.equal(parseCardEnhanceRule([]), null);
assert.equal(parseCardEnhanceRule([{maxLevel: 1}]), null, "바닥 이하 상한은 강화가 없는 것이다");
assert.equal(parseCardEnhanceRule([{maxLevel: "x"}]), null);

// 표가 천장보다 큰 상한을 말해도 잘라 읽는다 — 클라 체력 곡선이 거기까지만 저작돼 있다.
assert.equal(parseCardEnhanceRule([{maxLevel: 99, baseEnhanceCost: 25}]).maxLevel, CARD_MAX_LEVEL_CEILING);

// 성공률 1000분율은 0~1000 으로 조인다.
assert.equal(parseCardEnhanceRule([{maxLevel: 4, baseSuccessPermille: 5000}]).baseSuccessPermille, PERMILLE);
assert.equal(parseCardEnhanceRule([{maxLevel: 4, baseSuccessPermille: -1}]).baseSuccessPermille, 0);

// ── 카드: 오버라이드 파싱 ───────────────────────────────────────────────────
{
  const overrides = parseCardEnhanceOverrides([
    {id: 1, level: 2, cost: 25, costCurrency: "Shard", successPermille: 1000},
    {id: 2, level: 2, cost: 999, costCurrency: "Gold", successPermille: 1},
    {id: 3, level: 1, cost: 5, costCurrency: "Shard", successPermille: 1000},
    {id: 4, level: 3, cost: -7, costCurrency: "", successPermille: 1000},
  ]);
  assert.equal(overrides.size, 2, "레벨 1 행은 버리고 중복 레벨은 첫 줄이 이긴다");
  assert.equal(overrides.get(2).cost, 25);
  assert.equal(overrides.get(3).cost, 0, "음수 비용은 0");
  assert.equal(overrides.get(3).currency, "Shard", "재화를 못 읽으면 축의 기본 재화");
}

// ── 키워드: 5/10/15… 등차, 상한에서 멈춘다 ─────────────────────────────────
{
  const rules = parseKeywordEnhanceRules(KEYWORD_ROWS);
  assert.equal(rules.size, 6);

  const ranged = rules.get(RANGED);
  assert.equal(ranged.currency, "Energy");
  assert.deepEqual([0, 1, 2, 8, 9].map((level) => keywordEnhanceStep(ranged, level).cost), [5, 10, 15, 45, 50]);
  assert.equal(keywordEnhanceStep(ranged, 9).level, KEYWORD_MAX_LEVEL);
  assert.equal(keywordEnhanceStep(ranged, KEYWORD_MAX_LEVEL), null, "만렙에서는 스텝이 없다");
  assert.equal(keywordEnhanceStep(ranged, -1), null);

  // 키워드 강화는 확률 실패가 없다 — 성공률을 최대로 실어 카드와 같은 경로를 탄다.
  assert.equal(keywordEnhanceStep(ranged, 0).successPermille, PERMILLE);
  assert.equal(rules.get(PEERLESS).baseCost, 5);
}

// ── 키워드: 지원 목록 밖 행은 배제된다 ──────────────────────────────────────
{
  const rules = parseKeywordEnhanceRules([
    {id: 1, keyword: "Mark", baseCost: 5, costGrowthPerLevel: 5, maxLevel: 10, costCurrency: "Energy"},
    {id: 2, keyword: "Invincible", baseCost: 5, costGrowthPerLevel: 5, maxLevel: 10, costCurrency: "Energy"},
    {id: 3, keyword: "Nonsense", baseCost: 5, costGrowthPerLevel: 5, maxLevel: 10, costCurrency: "Energy"},
    {id: 4, keyword: " ranged ", baseCost: 5, costGrowthPerLevel: 5, maxLevel: 99, costCurrency: ""},
  ]);
  assert.equal(rules.size, 1, "Mark·Invincible·미상 이름은 곡선에 들지 않는다");
  assert.equal(rules.has(MARK), false);
  assert.equal(rules.get(RANGED).maxLevel, KEYWORD_MAX_LEVEL, "표가 더 크게 말해도 코덱 상한에서 자른다");
  assert.equal(rules.get(RANGED).currency, "Energy", "재화를 못 읽으면 축의 기본 재화");
}

// ── 판정: 양 끝에서는 난수를 뽑지 않는다 ────────────────────────────────────
{
  const explode = () => {
    throw new Error("확정된 자리에서 굴리면 안 된다");
  };
  assert.equal(rollSucceeded(PERMILLE, explode), true);
  assert.equal(rollSucceeded(PERMILLE + 500, explode), true);
  assert.equal(rollSucceeded(0, explode), false);
  assert.equal(rollSucceeded(-1, explode), false);

  const asked = [];
  const roll = (value) => (max) => {
    asked.push(max);
    return value;
  };
  assert.equal(rollSucceeded(500, roll(499)), true);
  assert.equal(rollSucceeded(500, roll(500)), false, "경계는 실패 쪽이다");
  assert.deepEqual(asked, [PERMILLE, PERMILLE]);
}

// ── 카드 레벨 코덱: 읽기·반영·가지치기 ──────────────────────────────────────
{
  assert.equal(levelOfCard({}, 7), BASE_LEVEL, "기록이 없으면 미강화");
  assert.equal(levelOfCard(readGrowthEntries({entries: {7: {snack: 3}}}), 7), BASE_LEVEL,
    "레벨을 0부터 세던 세이브도 미강화로 읽는다");
  assert.equal(levelOfCard({7: {level: 3, snack: 0, limitBreak: 0}}, 7), 3);

  const before = {7: {level: 2, snack: 5, limitBreak: 1}};
  const after = applyEnhanceLevel(before, 7, 3);
  assert.deepEqual(after[7], {level: 3, snack: 5, limitBreak: 1}, "먹이·한계돌파는 그대로 실려야 한다");
  assert.equal(before[7].level, 2, "입력 맵은 그대로여야 한다 — 트랜잭션이 재실행되면 원본을 다시 쓴다");

  // 신규 카드도 강화 대상이다(팩으로 받은 뒤 성장 기록이 아직 없는 상태).
  assert.deepEqual(growthSlot(applyEnhanceLevel({}, 9, 2)), {entries: {9: {level: 2, snack: 0, limitBreak: 0}}});
}

// ── 키워드 레벨 코덱 ────────────────────────────────────────────────────────
{
  const levels = readKeywordLevels({levels: {1: 2, 32: 9, 2: 0}});
  assert.deepEqual(levels, {1: 2}, "지원 밖 키·레벨 0 은 버린다");
  assert.equal(levelOfKeyword(levels, PEERLESS), 0);

  const next = setKeywordLevel(levels, PEERLESS, 1);
  assert.deepEqual(keywordGrowthSlot(next), {levels: {1: 2, 2: 1}});
  assert.deepEqual(levels, {1: 2}, "입력 맵은 그대로여야 한다");
  assert.deepEqual(setKeywordLevel(levels, RANGED, 99), {1: KEYWORD_MAX_LEVEL});
  assert.deepEqual(setKeywordLevel(levels, MARK, 5), {1: 2}, "지원 밖 키워드는 씌우지 않는다");
}

// ── 튜토리얼 무료 한 방 ─────────────────────────────────────────────────────
{
  const db = {doc: (path) => path};
  assert.equal(grantsRef(db, "test", "u1"), "envs/test/users/u1/grants/current");

  const snapshot = (data) => ({exists: data !== undefined, data: () => data});
  assert.deepEqual(readGrants(snapshot(undefined)), {enhanceCard: false, enhanceKeyword: false},
    "문서가 없으면 미사용 — 낼 돈이 없는 신규 계정이 여기서 멈추면 안 된다");
  assert.deepEqual(readGrants(snapshot({})), {enhanceCard: false, enhanceKeyword: false});
  assert.deepEqual(readGrants(snapshot({enhanceCard: 1, enhanceKeyword: "true"})),
    {enhanceCard: false, enhanceKeyword: false}, "boolean 이 아니면 미사용으로 읽는다");
  assert.deepEqual(readGrants(snapshot({enhanceCard: true})), {enhanceCard: true, enhanceKeyword: false});

  assert.equal(hasFreeShot({enhanceCard: false, enhanceKeyword: true}, "enhanceCard"), true);
  assert.equal(hasFreeShot({enhanceCard: false, enhanceKeyword: true}, "enhanceKeyword"), false);

  const written = [];
  const transaction = {set: (ref, value) => written.push({ref, value})};
  writeGrantUsed(transaction, "ref", "enhanceCard", {enhanceCard: false, enhanceKeyword: true}, "now");
  assert.deepEqual(written[0].value, {
    schemaVersion: GRANT_SCHEMA_VERSION,
    enhanceCard: true,
    enhanceKeyword: true,
    updatedAt: "now",
  }, "다른 축의 낙인을 지우지 않는다");
}

// ── 무료 한 방은 비용만 0으로 만든다(성공률은 그대로) ───────────────────────
{
  const rule = parseCardEnhanceRule(CARD_RULE_ROWS);
  const overrides = parseCardEnhanceOverrides(CARD_ENHANCE_ROWS);
  const step = cardEnhanceStep(rule, overrides, 2);

  const charge = (grants) => (hasFreeShot(grants, "enhanceCard") ? 0 : step.cost);
  assert.equal(charge({enhanceCard: false, enhanceKeyword: false}), 0);
  assert.equal(charge({enhanceCard: true, enhanceKeyword: false}), 25, "소진 뒤에는 제값을 문다");
  assert.equal(step.successPermille, PERMILLE, "무료가 성공률을 건드리면 안 된다");
}

console.log("test-enhance: ok");
