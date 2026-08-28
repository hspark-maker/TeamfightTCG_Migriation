// openPack 순수 모듈 회귀. 에뮬레이터 없이 lib/ 를 직접 require 한다(test-fresh-account.js 관용구).
//
// 여기서 지키는 것은 "서버 추첨이 클라 CardPackOpener 와 같은 답을 내는가" 하나다.
// 확률 계약이 깨지면 고지 확률(PackOdds)과 실제 추첨이 갈리는데, 그건 실플레이로는 안 보인다.
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const {resolveDropPool, drawPack, SNACK_PER_DUPLICATE} = require("../lib/packs/packDraw.js");
const {gradeOf, isRanked, entryPointsFromRows, parsePoolGrade, parseRequiredGrade,
  FALLBACK_ENTRY_POINTS, GRADE_KEYS} = require("../lib/packs/rankGrade.js");
const {readOwnedIds, buildOwnershipSlot} = require("../lib/packs/packSlots.js");

const drop = (id, minGrade, cardId, weight) => ({id, packId: "P", minGrade, cardId, weight});
const catalog = (...ids) => new Set(ids);

// 난수를 대신하는 대본. roll 이 무엇을 받았는지도 남겨 "잔여 합 재계산"을 검사한다.
function scriptedRoll(values) {
  const seen = [];
  let i = 0;
  const fn = (max) => {
    seen.push(max);
    const value = values[i++];
    assert.ok(value !== undefined, "대본보다 많이 뽑았다");
    assert.ok(value < max, `대본 값 ${value} 이 범위 ${max} 밖이다`);
    return value;
  };
  fn.seen = seen;
  return fn;
}

// ── 등급 판정 ────────────────────────────────────────────────────────────────
// 클라 RankConfig.ResolveTierIndex 는 첫 등급 미도달도 0(Bronze)으로 폴백한다.
assert.equal(gradeOf(FALLBACK_ENTRY_POINTS, 0), 0);
assert.equal(gradeOf(FALLBACK_ENTRY_POINTS, 99), 0);
assert.equal(gradeOf(FALLBACK_ENTRY_POINTS, 100), 0);
assert.equal(gradeOf(FALLBACK_ENTRY_POINTS, 259), 0);
assert.equal(gradeOf(FALLBACK_ENTRY_POINTS, 260), 1);
assert.equal(gradeOf(FALLBACK_ENTRY_POINTS, 739), 3);
assert.equal(gradeOf(FALLBACK_ENTRY_POINTS, 740), 4);
assert.equal(gradeOf(FALLBACK_ENTRY_POINTS, 999999), 4);

// 잠금 판정에는 IsRanked 가 따로 필요하다 — gradeOf 만 보면 0점도 브론즈로 읽힌다.
assert.equal(isRanked(FALLBACK_ENTRY_POINTS, 99), false);
assert.equal(isRanked(FALLBACK_ENTRY_POINTS, 100), true);

// ── 임계치 드리프트: TS 폴백 상수 ↔ Assets/SO/Rank/RankConfig.asset ────────────
// 서버는 RankGrade 시트를 읽지만, 시트를 못 읽었을 때 쓰는 폴백이 SO 와 어긋나면
// 팩 잠금이 클라 표시와 조용히 갈린다. 진실원인 .asset 을 직접 긁어 대조한다.
const assetPath = path.join(__dirname, "..", "..", "Assets", "SO", "Rank", "RankConfig.asset");
if (fs.existsSync(assetPath)) {
  const authored = [...fs.readFileSync(assetPath, "utf8").matchAll(/^\s*entryPoints:\s*(-?\d+)\s*$/gm)]
    .map((m) => Number(m[1]));
  assert.deepEqual(authored, FALLBACK_ENTRY_POINTS,
    "RankConfig.asset 의 entryPoints 가 rankGrade.ts 의 FALLBACK_ENTRY_POINTS 와 다르다");
} else {
  console.log("  (RankConfig.asset 없음 — 임계치 대조 건너뜀)");
}

// 표에서 뽑을 때는 행 순서가 아니라 gradeKey 로 자리를 잡는다.
const gradeRows = GRADE_KEYS.map((gradeKey, i) =>
  ({id: GRADE_KEYS.length - i, gradeKey, entryPoints: FALLBACK_ENTRY_POINTS[i]}));
assert.deepEqual(entryPointsFromRows(gradeRows), FALLBACK_ENTRY_POINTS);
assert.equal(entryPointsFromRows(gradeRows.slice(1)), null, "등급이 하나라도 빠지면 폴백으로 간다");

// ── 등급 문자열 파싱: 풀(대소문자 가림) vs 잠금(안 가림) ──────────────────────
assert.equal(parsePoolGrade("Silver"), 1);
assert.equal(parsePoolGrade("silver"), 0, "PackSpec.ParseGrade 는 대소문자를 가린다 → Bronze 폴백");
assert.equal(parsePoolGrade("2"), 2, "C# Enum.TryParse 는 정수 문자열도 받는다");
assert.equal(parsePoolGrade("99"), 99, "범위 밖 정수는 그대로 커서 풀에서 제외된다");
assert.equal(parsePoolGrade(""), 0);

assert.equal(parseRequiredGrade("silver"), 1, "TryGetMinRankGrade 는 대소문자를 안 가린다");
assert.equal(parseRequiredGrade("  "), null, "공백은 잠금 없음");
assert.equal(parseRequiredGrade("99"), null, "IsDefined 에 걸려 잠금 없음");
assert.equal(parseRequiredGrade("Diamond"), 4);

// ── 풀 해석: 만족 등급 중 가장 높은 하나만 (하위 합산 금지) ──────────────────
const rows = [
  drop(1, "Bronze", 10, 1), drop(2, "Bronze", 11, 1),
  drop(3, "Gold", 20, 5), drop(4, "Gold", 21, 5),
  drop(5, "Diamond", 30, 1),
];
const all = catalog(10, 11, 20, 21, 30);
assert.deepEqual(resolveDropPool(rows, 0, all).map((c) => c.cardId), [10, 11]);
assert.deepEqual(resolveDropPool(rows, 1, all).map((c) => c.cardId), [10, 11], "실버는 브론즈 묶음 그대로");
assert.deepEqual(resolveDropPool(rows, 2, all).map((c) => c.cardId), [20, 21], "골드는 브론즈와 합산하지 않는다");
assert.deepEqual(resolveDropPool(rows, 4, all).map((c) => c.cardId), [30]);

// 카탈로그에 없는 행은 풀에서 빠지지만, 등급 선택은 그 전에 끝난다
// — 골드 묶음이 통째로 미해석이면 브론즈로 내려가지 않고 빈 풀이 된다(클라 PackSpec 와 같다).
assert.deepEqual(resolveDropPool(rows, 2, catalog(10, 11)), [], "등급 선택은 카탈로그보다 먼저다");

// 가중치 0·음수는 균등 1
assert.deepEqual(
  resolveDropPool([drop(1, "Bronze", 10, 0), drop(2, "Bronze", 11, -3)], 0, all).map((c) => c.weight),
  [1, 1]);

// 만족하는 등급이 없으면 빈 풀
assert.deepEqual(resolveDropPool([drop(1, "Diamond", 30, 1)], 0, all), []);

// ── 추첨: 가중치 경계 ────────────────────────────────────────────────────────
// 합 10 = [1, 5, 4]. roll 0 → 첫째, roll 1 → 둘째(경계), roll 5 → 둘째의 끝, roll 6 → 셋째.
const weighted = [{cardId: 1, weight: 1}, {cardId: 2, weight: 5}, {cardId: 3, weight: 4}];
const weightedCatalog = catalog(1, 2, 3);
const pickOne = (rollValue) => drawPack(
  weighted, 1, false, weightedCatalog, new Set(), scriptedRoll([rollValue]))[0].cardId;
assert.equal(pickOne(0), 1);
assert.equal(pickOne(1), 2);
assert.equal(pickOne(5), 2);
assert.equal(pickOne(6), 3);
assert.equal(pickOne(9), 3);

// ── 추첨: 비복원은 뽑을 때마다 잔여 합을 다시 센다 ───────────────────────────
// 1회차 합 10 → 0 을 굴려 cardId 1(weight 1) 제거. 2회차 합은 9여야 한다(10 이면 캐시한 것).
const uniqueRoll = scriptedRoll([0, 0, 0]);
const uniqueDrawn = drawPack(weighted, 3, true, weightedCatalog, new Set(), uniqueRoll);
assert.deepEqual(uniqueRoll.seen, [10, 9, 4], "비복원 잔여 가중치 합이 재계산되지 않았다");
assert.deepEqual(uniqueDrawn.map((c) => c.cardId), [1, 2, 3]);

// 복원 추첨은 합이 줄지 않는다
const repeatRoll = scriptedRoll([0, 0]);
drawPack(weighted, 2, false, weightedCatalog, new Set(), repeatRoll);
assert.deepEqual(repeatRoll.seen, [10, 10]);

// ── 추첨: uniqueDraw 장수 clamp ──────────────────────────────────────────────
const clampRoll = scriptedRoll([0, 0, 0]);
const clamped = drawPack(weighted, 99, true, weightedCatalog, new Set(), clampRoll);
assert.equal(clamped.length, 3, "비복원은 풀 크기를 넘겨 뽑을 수 없다");
assert.equal(clampRoll.seen.length, 3);

// 복원은 clamp 하지 않는다
assert.equal(drawPack(weighted, 5, false, weightedCatalog, new Set(),
  scriptedRoll([0, 0, 0, 0, 0])).length, 5);

// ── 추첨: 신규/중복 판정과 간식 ──────────────────────────────────────────────
const owned = new Set([2]);
const graded = drawPack(weighted, 2, false, weightedCatalog, owned, scriptedRoll([0, 1]));
assert.deepEqual(graded, [
  {cardId: 1, isNew: true, snack: 0},
  {cardId: 2, isNew: false, snack: SNACK_PER_DUPLICATE},
]);
assert.deepEqual([...owned].sort((a, b) => a - b), [1, 2], "신규 카드가 소유 집합에 얹혀야 한다");

// 한 팩 안에서 같은 카드를 두 번 뽑으면 두 번째는 중복이다
const twice = drawPack([{cardId: 7, weight: 1}], 2, false, catalog(7), new Set(), scriptedRoll([0, 0]));
assert.deepEqual(twice.map((c) => c.isNew), [true, false]);
assert.equal(twice[1].snack, SNACK_PER_DUPLICATE);

// ── 추첨: 카탈로그 미포함 id 는 뽑힌 "뒤" 버린다(뽑기 1회 소비) ──────────────
// 클라 CardPackOpener.Draw 의 continue 와 같다 — 장수가 줄고, 비복원이면 그 자리가 소비된다.
const dirty = [{cardId: 1, weight: 1}, {cardId: 99, weight: 1}];
const dirtyDrawn = drawPack(dirty, 2, true, catalog(1), new Set(), scriptedRoll([1, 0]));
assert.deepEqual(dirtyDrawn.map((c) => c.cardId), [1], "미포함 카드는 장수만 줄인다");

// ── 빈 풀 ────────────────────────────────────────────────────────────────────
assert.deepEqual(drawPack([], 3, false, all, new Set(), scriptedRoll([])), []);

// ── 소유 슬롯 ───────────────────────────────────────────────────────────────
// 소유는 기존 순서를 유지하고 신규만 뒤에 붙인다(중복 지급은 안 붙는다).
assert.deepEqual(readOwnedIds({cardIds: [3, 1, 3, 0, -2, 1]}), [3, 1]);
assert.deepEqual(
  buildOwnershipSlot([3, 1], [
    {cardId: 1, isNew: false, snack: 1},
    {cardId: 9, isNew: true, snack: 0},
    {cardId: 9, isNew: false, snack: 1},
  ]),
  {cardIds: [3, 1, 9]});

// 재화는 scripts/test-currency.js, 카드 성장·먹이는 scripts/test-growth.js 가 맡는다.

console.log("test-open-pack: ok");
