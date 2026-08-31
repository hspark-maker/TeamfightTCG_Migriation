// grantTutorialCards 순수 모듈 회귀. 에뮬레이터 없이 lib/ 를 직접 require 한다(test-claim-reward.js 관용구).
//
// 여기서 지키는 것은 네 가지다.
//  1) 지급은 CardPackDrop 풀 **전량**이다 — 추첨이 아니므로 drawCount·weight 가 결과를 바꾸면 안 된다.
//  2) 유료 팩 packId 로는 못 받는다. price 를 **못 읽은 팩도 유료로 본다** — 이 판정이 지급 경로의
//     유일한 권한 게이트라 표 결손 앞에서 열리면 상점 팩이 통째로 공짜가 된다.
//  3) 카탈로그 밖 cardId 는 소유에 붙지 않는다 — openPack 이 drawPack 에서 거는 것과 같은 문턱이다.
//  4) 재호출이 무해하다 — 이 명령에는 낙인이 없어서 멱등성이 유일한 안전장치다.
const assert = require("node:assert/strict");
const {
  packGrantCardIds,
  judgeTutorialGrant,
} = require("../lib/packs/tutorialGrantPack.js");
const {
  buildOwnershipSlotFromIds,
  readOwnedIds,
} = require("../lib/packs/packSlots.js");

// CardPackDrop 한 줄. 실제 시트의 컬럼 이름 그대로다(id | packId | minGrade | cardId | weight).
const drop = (id, packId, cardId, weight = 1) => ({id, packId, minGrade: "", cardId, weight});
// CardPack 한 줄 중 판정이 보는 것만. price 0 이 곧 "튜토리얼이 줘도 되는 팩" 이다.
const pack = (packId, price, priceAuthored = true) => ({packId, price, priceAuthored,
  priceType: "Gold", drawCount: 6, uniqueDraw: true, refundType: "Snack", refundAmount: 0, minRankGrade: ""});
// packSpecReader.readDropRows 와 같은 규약 — 표 전량에서 이 팩 행만 남긴다(행은 id 오름차순).
const dropsOf = (table, packId) => table.filter((row) => row.packId === packId);

const CATALOG = new Set([1, 3, 4, 6, 11, 20, 26, 28, 30]);

// ── packId 로 카드 수집: 드롭 행 순서 그대로, 다른 팩은 섞이지 않는다 ────────
const TABLE = [
  drop(1, "StarterPack", 1),
  drop(2, "StarterPack", 28),
  drop(3, "KeywordDeck", 4, 7),
  drop(4, "StarterPack", 20),
  drop(5, "StarterPack", 6),
  drop(6, "StarterPack", 11),
  drop(7, "StarterPack", 30),
  drop(8, "KeywordDeck", 26, 0),
  drop(9, "GoldPack", 3),
  drop(10, "GhostPack", 777),
];
{
  assert.deepEqual(packGrantCardIds(dropsOf(TABLE, "StarterPack"), CATALOG), [1, 28, 20, 6, 11, 30]);
  assert.deepEqual(packGrantCardIds(dropsOf(TABLE, "KeywordDeck"), CATALOG), [4, 26],
    "가중치는 지급 목록을 바꾸지 않는다");

  const verdict = judgeTutorialGrant(pack("StarterPack", 0), dropsOf(TABLE, "StarterPack"), CATALOG);
  assert.equal(verdict.ok, true);
  assert.deepEqual(verdict.cardIds, [1, 28, 20, 6, 11, 30]);
}

// ── 못 읽는 id · 카탈로그 밖 카드는 버린다 ──────────────────────────────────
{
  assert.deepEqual(packGrantCardIds([
    drop(1, "P", 6), drop(2, "P", 0), drop(3, "P", -7),
    drop(4, "P", Number.NaN), drop(5, "P", 6), drop(6, "P", 999), drop(7, "P", 11),
  ], CATALOG), [6, 11], "0 이하·비정수·중복·카탈로그 밖이 모두 빠진다");
  assert.deepEqual(packGrantCardIds([], CATALOG), []);

  // 카탈로그가 비면 줄 수 있는 카드도 없다(Card 표를 못 읽은 상황).
  assert.deepEqual(packGrantCardIds(dropsOf(TABLE, "StarterPack"), new Set()), []);
}

// ── 거절: 드롭 0행 · CardPack 미등재 · 유료 · price 결손 ─────────────────────
{
  assert.deepEqual(judgeTutorialGrant(pack("EmptyPack", 0), dropsOf(TABLE, "EmptyPack"), CATALOG),
    {ok: false, reason: "GrantNotFound"}, "드롭 풀이 비면 지급할 것이 없다");

  assert.deepEqual(judgeTutorialGrant(null, dropsOf(TABLE, "StarterPack"), CATALOG),
    {ok: false, reason: "GrantNotFound"}, "CardPack 에 없는 packId 는 팩이 아니다");

  // 드롭은 있는데 전부 카탈로그 밖 — 거르고 나면 0장이라 같은 사유로 떨어진다.
  assert.deepEqual(judgeTutorialGrant(pack("GhostPack", 0), dropsOf(TABLE, "GhostPack"), CATALOG),
    {ok: false, reason: "GrantNotFound"});

  assert.deepEqual(judgeTutorialGrant(pack("GoldPack", 300), dropsOf(TABLE, "GoldPack"), CATALOG),
    {ok: false, reason: "GrantNotAllowed"}, "유료 팩을 튜토리얼 지급으로 받을 수 없다");

  // price 셀이 비었거나 수가 아니면 price 는 0 으로 접히지만(openPack 거동) 무료로 보지 않는다.
  assert.deepEqual(judgeTutorialGrant(pack("GoldPack", 0, false), dropsOf(TABLE, "GoldPack"), CATALOG),
    {ok: false, reason: "GrantNotAllowed"}, "price 결손은 무료가 아니다");
  assert.deepEqual(judgeTutorialGrant(pack("GoldPack", Number.NaN, false), dropsOf(TABLE, "GoldPack"), CATALOG),
    {ok: false, reason: "GrantNotAllowed"});

  // 표를 통째로 못 읽어도 결과는 거절이다(호출부가 그때만 logger.error 를 먼저 남긴다).
  assert.deepEqual(judgeTutorialGrant(pack("StarterPack", 0), [], CATALOG),
    {ok: false, reason: "GrantNotFound"});
}

// ── price 결손 판정은 리더가 낸다 — 셀 모양별 대조 ──────────────────────────
{
  const {isPriceAuthored} = require("../lib/packs/tutorialGrantPack.js");
  // Number("") 가 0 이라, 이 구분이 없으면 빈 price 셀이 무료로 통과한다.
  for (const raw of [undefined, null, "", "   ", "free", Number.NaN, {}]) {
    assert.equal(isPriceAuthored(raw), false, `결손으로 읽혀야 한다: ${String(raw)}`);
  }
  for (const raw of [0, "0", 300, "300", -1]) {
    assert.equal(isPriceAuthored(raw), true, `읽혀야 한다: ${String(raw)}`);
  }
}

// ── 소유 슬롯: 기존 순서 보존 · 이미 가진 카드 skip · 멱등 ──────────────────
{
  const cardIds = packGrantCardIds(dropsOf(TABLE, "StarterPack"), CATALOG);
  const owned = [5, 3, 9];

  const once = buildOwnershipSlotFromIds(owned, cardIds);
  assert.deepEqual(once.cardIds, [5, 3, 9, 1, 28, 20, 6, 11, 30], "기존 소유 순서 뒤에 신규만 붙는다");

  // 2회 호출: 증분 0 이고 슬롯도 그대로 — 낙인이 없으므로 여기서 무해해져야 한다.
  const twice = buildOwnershipSlotFromIds(once.cardIds, cardIds);
  assert.deepEqual(twice.cardIds, once.cardIds);
  assert.deepEqual(cardIds.filter((id) => !new Set(once.cardIds).has(id)), [],
    "두 번째 호출의 granted 는 비어야 한다");

  // 이미 가진 카드는 조용히 skip 한다 — 일부만 겹쳐도 남은 것만 붙는다.
  assert.deepEqual(buildOwnershipSlotFromIds([28, 6], cardIds).cardIds, [28, 6, 1, 20, 11, 30]);

  // 문서에서 읽어 온 소유도 같은 규약이다(readOwnedIds → 슬롯 빌더).
  const fromDocument = readOwnedIds({cardIds: [5, "3", 3, 0, 9]});
  assert.deepEqual(buildOwnershipSlotFromIds(fromDocument, cardIds).cardIds, once.cardIds);
}

console.log("test-tutorial-grant: ok");
