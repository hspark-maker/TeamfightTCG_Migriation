const assert = require("node:assert/strict");
const {buildFreshAccountSlots, STARTER_GOLD, DECK_SLOT_COUNT,
  STARTER_DECK_NAME, STARTER_DECK_SIZE} = require("../lib/save/freshAccount.js");
const {resolveStarterCardsFromRows, FALLBACK_STARTER_CARD_IDS,
  parseGrade} = require("../lib/save/starterPool.js");

const STARTER = [1, 28, 20, 6, 11, 30];
const drop = (id, minGrade, cardId) =>
  ({id, minGrade, cardId});

// --- 문서 모양: Tools/firestore-rules-tests/fixtures/saveDocument.js 의 serverFreshAccountDocument 와 쌍둥이 ---
const slots = buildFreshAccountSlots(STARTER);

assert.deepEqual(Object.keys(slots).sort(), [
  "albumReward", "cardGrowth", "currency", "deck", "keywordGrowth",
  "ownership", "profile", "rank", "tournament", "tutorial"].sort());

// 룰의 isValidSave 가 재화 4키를 정확히 요구한다 — 하나라도 어긋나면 이후 저장이 영구 거부된다.
assert.deepEqual(Object.keys(slots.currency.balances).sort(), ["Diamond", "Energy", "Gold", "Shard"]);
assert.equal(slots.currency.balances.Gold, STARTER_GOLD);
assert.equal(slots.currency.balances.Diamond, 0);
assert.equal(slots.currency.balances.Energy, 0);
assert.equal(slots.currency.balances.Shard, 0);

assert.deepEqual(slots.ownership.cardIds, STARTER);

// 클라 DeckSaveManager.NormalizedSlots 가 항상 6으로 패딩한다 — 길이가 다르면 첫 저장이 모양을 바꾼다.
assert.equal(slots.deck.slots.length, DECK_SLOT_COUNT);
assert.equal(slots.deck.slots[0].name, STARTER_DECK_NAME);
assert.deepEqual(slots.deck.slots[0].cardIds, STARTER);
assert.equal(slots.deck.slots[0].imageKey, "");
for (let i = 1; i < DECK_SLOT_COUNT; i++) {
  assert.deepEqual(slots.deck.slots[i], {name: "", cardIds: [], imageKey: ""});
}

assert.deepEqual(slots.cardGrowth, {entries: {}});
assert.deepEqual(slots.keywordGrowth, {levels: {}});
assert.deepEqual(slots.rank, {points: 0, claimedTiers: []});
assert.deepEqual(slots.albumReward, {claimedKeys: []});
assert.deepEqual(slots.tournament,
  {clearedNodeIds: [], claimedChapterIds: [], pendingRewardNodeId: ""});

// -1 은 클라 TutorialSaveData 의 초기화자다. 0 으로 두면 되감기 판정이 달라진다.
assert.equal(slots.tutorial.lastBootChapterIndex, -1);
assert.equal(slots.tutorial.lastBootStepIndex, -1);
assert.deepEqual(slots.tutorial, {
  outgameCompleted: false, chapterIndex: 0, chapterStepIndex: 0, stepId: 0,
  lastBootChapterIndex: -1, lastBootStepIndex: -1, sameCoordBootCount: 0,
  completedTriggers: [],
});

// null 이 설계다 — ProfileManager 가 IsNullOrEmpty 폴백으로 기본 아바타를 고른다.
assert.deepEqual(slots.profile, {nickname: null, avatarId: null, frameId: null});

// 입력 배열을 그대로 물지 않는다(호출부가 나중에 고쳐도 문서가 안 바뀐다).
const mutable = [...STARTER];
const copied = buildFreshAccountSlots(mutable);
mutable[0] = 999;
assert.equal(copied.ownership.cardIds[0], STARTER[0]);

// --- 풀 해석: 클라 PackSpec.ResolveDrops + StarterDeck.TakeDeckCards 재현 ---
const bronze6 = STARTER.map((cardId, i) => drop(i + 1, "Bronze", cardId));
assert.deepEqual(resolveStarterCardsFromRows(bronze6, 0), STARTER);

// 문서 id 는 정수 오름차순이 진실이다 — 문자열 정렬이면 "10" 이 "2" 앞에 서서 순서가 갈린다.
const shuffled = [drop(10, "Bronze", 30), drop(2, "Bronze", 28), drop(1, "Bronze", 1),
  drop(3, "Bronze", 20), drop(4, "Bronze", 6), drop(5, "Bronze", 11)];
assert.deepEqual(resolveStarterCardsFromRows(shuffled, 0), [1, 28, 20, 6, 11, 30]);

// 만족하는 등급 중 가장 높은 하나만 쓴다 — 하위 합산 없음.
const mixed = [...STARTER.map((cardId, i) => drop(i + 1, "Bronze", cardId)),
  ...[41, 42, 43, 44, 45, 46].map((cardId, i) => drop(i + 10, "Silver", cardId))];
assert.deepEqual(resolveStarterCardsFromRows(mixed, 0), STARTER, "Bronze 계정은 Silver 행을 못 본다");
assert.deepEqual(resolveStarterCardsFromRows(mixed, 1), [41, 42, 43, 44, 45, 46],
  "Silver 계정은 Silver 행만 쓴다(Bronze 합산 없음)");

// 중복 cardId 는 클라 TakeDeckCards 처럼 건너뛴다.
const dupes = [drop(1, "Bronze", 1), drop(2, "Bronze", 1), drop(3, "Bronze", 28),
  drop(4, "Bronze", 20), drop(5, "Bronze", 6), drop(6, "Bronze", 11), drop(7, "Bronze", 30)];
assert.deepEqual(resolveStarterCardsFromRows(dupes, 0), STARTER);

// 6장을 못 채우면 부분 지급 대신 빈 배열 → 호출부가 폴백으로 간다.
assert.deepEqual(resolveStarterCardsFromRows(bronze6.slice(0, 5), 0), []);
assert.deepEqual(resolveStarterCardsFromRows([], 0), []);

// 못 읽는 등급은 최하위로 떨어진다(클라 Enum.TryParse 실패 경로와 같다).
assert.deepEqual(
  resolveStarterCardsFromRows(STARTER.map((c, i) => drop(i + 1, "???", c)), 0), STARTER);

// 잘못된 cardId 는 건너뛴다 — 6장을 못 채우면 폴백으로 간다.
assert.deepEqual(resolveStarterCardsFromRows(
  [drop(1, "Bronze", 0), ...bronze6.slice(1)], 0), []);

// 7행이면 앞 6장만.
assert.deepEqual(resolveStarterCardsFromRows([...bronze6, drop(7, "Bronze", 77)], 0), STARTER);

assert.equal(FALLBACK_STARTER_CARD_IDS.length, STARTER_DECK_SIZE);
assert.deepEqual(FALLBACK_STARTER_CARD_IDS, STARTER,
  "폴백 상수는 StarterPack.asset 의 poolIds 와 같아야 한다");

console.log("test-fresh-account: ok");

// --- 카탈로그 존재 검사: 클라 PackSpec 의 CardCatalog.Contains 재현 ---
const CATALOG = new Set(STARTER);
const bronzeRows = STARTER.map((cardId, i) => drop(i + 1, "Bronze", cardId));

assert.deepEqual(resolveStarterCardsFromRows(bronzeRows, 0, CATALOG), STARTER);

// 카탈로그에 없는 카드는 건너뛴다 — 실리면 클라 IsSlotValid 가 그 덱을 무효로 봐 덱 0개로 부팅된다.
const withGhost = [drop(1, "Bronze", 777), ...bronzeRows.map((r, i) => drop(i + 2, "Bronze", r.cardId))];
assert.deepEqual(resolveStarterCardsFromRows(withGhost, 0, CATALOG), STARTER,
  "카탈로그 밖 카드를 건너뛰고 뒤 행으로 6장을 채운다");

// 걸러낸 뒤 6장을 못 채우면 부분 지급 대신 빈 배열 → 호출부가 폴백으로 간다.
assert.deepEqual(resolveStarterCardsFromRows(withGhost.slice(0, 6), 0, CATALOG), []);

// --- 등급 파싱: 클라 Enum.TryParse 는 이름과 숫자 문자열을 모두 받는다 ---
assert.equal(parseGrade("Bronze"), 0);
assert.equal(parseGrade("Diamond"), 4);
assert.equal(parseGrade("1"), 1, "숫자로 저작된 시트도 같은 등급으로 읽는다");
assert.equal(parseGrade("???"), 0, "못 읽으면 최하위");
assert.equal(parseGrade("99"), 0, "정의 범위 밖 정수는 등급이 아니다");

// 숫자 저작 시트도 이름 저작과 같은 행을 고른다.
const numericGrades = [...STARTER.map((c, i) => drop(i + 1, "0", c)),
  ...[41, 42, 43, 44, 45, 46].map((c, i) => drop(i + 10, "1", c))];
assert.deepEqual(resolveStarterCardsFromRows(numericGrades, 0), STARTER);
assert.deepEqual(resolveStarterCardsFromRows(numericGrades, 1), [41, 42, 43, 44, 45, 46]);

// --- id 를 못 읽는 행: 정렬 비교자가 NaN 을 뱉어 순서가 미정의가 된다. 버려야 한다 ---
const badId = [{id: Number.NaN, minGrade: "Bronze", cardId: 99}, ...bronzeRows];
assert.deepEqual(resolveStarterCardsFromRows(badId, 0, CATALOG), STARTER);
assert.deepEqual(resolveStarterCardsFromRows(
  [{id: Number.NaN, minGrade: "Bronze", cardId: 1}], 0), [], "id 불량만 있으면 폴백으로 간다");

console.log("test-fresh-account (extended): ok");
