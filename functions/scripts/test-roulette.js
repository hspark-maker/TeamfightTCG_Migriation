// 룰렛 순수 모듈 회귀. 에뮬레이터 없이 lib/ 를 직접 require 한다(test-open-pack.js 관용구).
//
// 여기서 지키는 것은 두 가지다.
//  1) 저작 결함이 조용히 상품이 되지 않는다 — 특히 RouletteTicket 상품(회전이 스스로를 재생산한다)과
//     parseCurrency 의 Gold 폴백(오타가 금화가 된다)이 막혀 있는가.
//  2) 비용 축은 fail-closed 다 — 헤더 행이 없거나 못 읽으면 과금하지 않고 판을 닫는다.
const assert = require("node:assert/strict");
const {
  ROULETTE_SLOT_COUNT,
  REWARD_TYPE_CURRENCY,
  effectiveWeight,
  resolveRouletteBoard,
  drawRouletteSlot,
} = require("../lib/roulette/rouletteDraw.js");

const BOARD_ID = "roulette_default";

// 정상 헤더. 덮어쓸 값만 넘겨 결함 헤더를 만든다(Roulette 표 한 행).
const header = (over = {}) => ({
  id: 1,
  rouletteId: BOARD_ID,
  displayName: "행운의 룰렛",
  priceType: "RouletteTicket",
  price: 1,
  sortOrder: 0,
  ...over,
});

// 정상 칸 행 하나. 덮어쓸 값만 넘겨 결함 행을 만든다(RouletteSlot 표 한 행).
const row = (id, over = {}) => ({
  id,
  rouletteId: BOARD_ID,
  slotIndex: id - 1,
  rewardType: REWARD_TYPE_CURRENCY,
  rewardId: "Gold",
  amount: 100,
  weight: 1,
  ...over,
});

// 난수를 대신하는 대본. roll 이 무엇을 받았는지도 남겨 "유효 가중치 합"을 검사한다.
function scriptedRoll(values) {
  const seen = [];
  let i = 0;
  const fn = (max) => {
    seen.push(max);
    const value = values[i++];
    assert.ok(value !== undefined, "대본보다 많이 뽑았다");
    return value;
  };
  fn.seen = seen;
  return fn;
}

// ── 상수: 클라 RouletteConfig.SLOT_COUNT 와 같아야 한다 ──────────────────────
assert.equal(ROULETTE_SLOT_COUNT, 8, "판 그림이 8쐐기다 — 바꾸면 클라 저작 검증과 갈린다");

// ── 유효 가중치: 0·음수는 균등 1 ────────────────────────────────────────────
assert.equal(effectiveWeight(3), 3);
assert.equal(effectiveWeight(1), 1);
assert.equal(effectiveWeight(0), 1, "0 은 '안 나오게 막는 수단'이 아니다");
assert.equal(effectiveWeight(-5), 1);

// ── 정상 판 ──────────────────────────────────────────────────────────────────
{
  const board = resolveRouletteBoard(header(), [1, 2, 3].map((id) => row(id)));

  assert.equal(board.rouletteId, BOARD_ID);
  assert.equal(board.priceType, "RouletteTicket");
  assert.equal(board.price, 1);
  assert.equal(board.droppedRows, 0);
  assert.deepEqual(board.slots.map((s) => s.slotIndex), [0, 1, 2], "칸 순서는 칸 표 id 오름차순이다");
  assert.deepEqual(board.slots[0], {slotIndex: 0, currency: "Gold", amount: 100, weight: 1});
}

// 칸 행이 0개라도 헤더가 있으면 판은 선다 — 빈 풀 판정은 호출부(EmptyPool)의 몫이다.
{
  const board = resolveRouletteBoard(header(), []);
  assert.notEqual(board, null);
  assert.equal(board.slots.length, 0);
  assert.equal(board.droppedRows, 0);
}

// ── 헤더: 없거나 못 읽으면 과금하지 않고 접는다 ──────────────────────────────
assert.equal(resolveRouletteBoard(null, [row(1)]), null,
  "그 판이 Roulette 표에 없으면 비용 축을 알 수 없다");
assert.equal(resolveRouletteBoard(undefined, [row(1)]), null,
  "리더가 null 을 준다는 계약이지만 undefined 도 판을 닫는다");
assert.equal(resolveRouletteBoard(header({price: 0}), [row(1)]), null,
  "0원 회전은 무한 회전이다");
assert.equal(resolveRouletteBoard(header({price: -1}), [row(1)]), null);
assert.equal(resolveRouletteBoard(header({price: 1.5}), [row(1)]), null,
  "비정수 비용은 차감할 수 없다");
assert.equal(resolveRouletteBoard(header({priceType: "Ticket"}), [row(1)]), null,
  "모르는 재화는 Gold 로 폴백하지 않는다");
assert.equal(resolveRouletteBoard(header({priceType: "roulettETICKET"}), [row(1)]), null,
  "재화 키는 대소문자까지 정확해야 한다");
assert.equal(resolveRouletteBoard(header({priceType: ""}), [row(1)]), null);

// 헤더는 칸 표와 독립이다 — 칸이 전부 결함이어도 비용 축은 그대로 읽힌다.
{
  const board = resolveRouletteBoard(header(), [row(1, {rewardType: "Card"}), row(2)]);
  assert.equal(board.price, 1);
  assert.equal(board.droppedRows, 1);
  assert.equal(board.slots.length, 1);
}

// 티켓 아닌 재화로 도는 판(다이아 회전 등)은 막지 않는다
{
  const board = resolveRouletteBoard(header({priceType: "Diamond", price: 30}), [row(1)]);
  assert.equal(board.priceType, "Diamond");
  assert.equal(board.price, 30);
}

// ── 칸 행 드롭 ───────────────────────────────────────────────────────────────
for (const [label, over] of [
  ["rewardType 불일치", {rewardType: "Card"}],
  ["amount 0", {amount: 0}],
  ["amount 음수", {amount: -3}],
  ["amount 소수", {amount: 1.5}],
  ["미지 rewardId", {rewardId: "Cookie"}],
  ["rewardId 대소문자 어긋남", {rewardId: "gold"}],
  ["RouletteTicket 상품", {rewardId: "RouletteTicket"}],
  ["slotIndex 음수", {slotIndex: -1}],
  ["slotIndex 상한 밖", {slotIndex: ROULETTE_SLOT_COUNT}],
  ["slotIndex 소수", {slotIndex: 0.5}],
]) {
  const board = resolveRouletteBoard(header(), [row(1, over), row(2)]);
  assert.equal(board.droppedRows, 1, `${label} 행이 버려져야 한다`);
  assert.deepEqual(board.slots.map((s) => s.slotIndex), [1], `${label}: 남는 칸은 정상 행 하나다`);
}

// rewardType 은 대소문자를 안 가린다(저작 표기 흔들림까지 버리면 판이 통째로 빈다).
assert.equal(resolveRouletteBoard(header(), [row(1, {rewardType: "currency"})]).droppedRows, 0);
assert.equal(resolveRouletteBoard(header(), [row(1, {rewardType: " Currency "})]).droppedRows, 0);

// 같은 자리를 두 행이 주장하면 id 낮은 쪽을 남긴다
{
  const board = resolveRouletteBoard(header(),
    [row(1, {amount: 10}), row(2, {slotIndex: 0, amount: 20})]);
  assert.equal(board.droppedRows, 1);
  assert.deepEqual(board.slots.map((s) => s.amount), [10], "id 낮은 쪽이 남는다");
}

// 칸이 전부 버려지면 slots 는 비지만 판 자체는 선다
{
  const board = resolveRouletteBoard(header(), [row(1, {rewardId: "RouletteTicket"})]);
  assert.notEqual(board, null);
  assert.equal(board.slots.length, 0);
  assert.equal(board.droppedRows, 1);
}

// ── weight 0 폴백은 판을 세울 때가 아니라 뽑을 때 걸린다 ─────────────────────
{
  const board = resolveRouletteBoard(header(), [row(1, {weight: 0})]);
  assert.equal(board.slots[0].weight, 0, "저작값은 그대로 싣는다 — 규칙의 주인은 effectiveWeight 하나다");

  const roll = scriptedRoll([0]);
  assert.equal(drawRouletteSlot(board.slots, roll).slotIndex, 0);
  assert.deepEqual(roll.seen, [1], "가중치 0 은 1 로 세어 합이 1 이다");
}

// ── 추첨: 가중치 경계 ────────────────────────────────────────────────────────
// 합 10 = [1, 5, 4]. roll 0 → 첫째, roll 1 → 둘째(경계), roll 5 → 둘째의 끝, roll 6 → 셋째.
{
  const slots = resolveRouletteBoard(header(), [
    row(1, {weight: 1}), row(2, {weight: 5}), row(3, {weight: 4}),
  ]).slots;
  const pick = (value) => drawRouletteSlot(slots, scriptedRoll([value])).slotIndex;

  assert.equal(pick(0), 0, "누적합 경계: 0");
  assert.equal(pick(1), 1);
  assert.equal(pick(5), 1);
  assert.equal(pick(6), 2);
  assert.equal(pick(9), 2, "누적합 sum-1 은 마지막 칸이다");

  const roll = scriptedRoll([0]);
  drawRouletteSlot(slots, roll);
  assert.deepEqual(roll.seen, [10], "roll 은 유효 가중치 합을 받는다");

  // 음수는 구조상 오지 않지만, 와도 첫 칸에서 멈춰야 한다(뽑히지 않고 빠져나가면 응답이 빈다).
  assert.equal(drawRouletteSlot(slots, scriptedRoll([-1])).slotIndex, 0);
}

// 칸이 없으면 null — 호출부가 EmptyPool 로 접는다
assert.equal(drawRouletteSlot([], scriptedRoll([])), null);

console.log("test-roulette: ok");
