const assert = require("node:assert/strict");
const {buildFreshAccountSlots, buildFreshAccountBalances, STARTER_GOLD, DECK_SLOT_COUNT,
  STARTER_DECK_NAME, STARTER_DECK_SIZE} = require("../lib/save/freshAccount.js");
const {resolveStarterCardsFromRows, FALLBACK_STARTER_CARD_IDS,
  parseGrade} = require("../lib/save/starterPool.js");
const {generateNickname} = require("../lib/profile/generateNickname.js");
const {NICKNAME_MAX_LENGTH, NICKNAME_MODIFIERS,
  NICKNAME_NOUNS} = require("../lib/profile/nicknameWords.js");

const STARTER = [1, 28, 20, 6, 11, 30];
const drop = (id, minGrade, cardId) =>
  ({id, minGrade, cardId});

// --- 문서 모양: Tools/firestore-rules-tests/fixtures/saveDocument.js 의 serverFreshAccountDocument 와 쌍둥이 ---
const slots = buildFreshAccountSlots(STARTER);

assert.deepEqual(Object.keys(slots).sort(), [
  "albumReward", "cardGrowth", "deck", "keywordGrowth",
  "ownership", "profile", "rank", "tournament", "tutorial"].sort());

// v8 부터 재화는 세이브를 떠났다 — 슬롯이 남으면 지갑과 세이브가 둘 다 잔액을 주장한다.
assert.equal(slots.currency, undefined, "currency 슬롯은 지갑 문서로 갔다");

// 스타터 골드는 세이브와 같은 트랜잭션에서 서는 지갑의 최초 잔액이다.
// 두 문서가 갈라지면 초기화의 ensureWallet 이 0 잔액 지갑을 세워 이 골드가 영영 사라진다.
const balances = buildFreshAccountBalances();
assert.deepEqual(Object.keys(balances).sort(), ["Diamond", "Energy", "Gold", "Shard"],
  "룰이 재화 4키를 정확히 요구한다");
assert.equal(balances.Gold, STARTER_GOLD);
assert.equal(balances.Diamond, 0);
assert.equal(balances.Energy, 0);
assert.equal(balances.Shard, 0);

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
  {clearedNodeIds: [], claimedChapterIds: [], seenUnlockIds: [], pendingRewardNodeId: ""});

// -1 은 클라 TutorialSaveData 의 초기화자다. 0 으로 두면 되감기 판정이 달라진다.
assert.equal(slots.tutorial.lastBootChapterIndex, -1);
assert.equal(slots.tutorial.lastBootStepIndex, -1);
assert.deepEqual(slots.tutorial, {
  outgameCompleted: false, chapterIndex: 0, chapterStepIndex: 0, stepId: 0,
  lastBootChapterIndex: -1, lastBootStepIndex: -1, sameCoordBootCount: 0,
  completedTriggers: [],
});

// 닉네임은 계정이 생기는 이 자리에서 굳는다 — 클라는 뽑지 않고 문서 값을 읽는다.
// 아바타·프레임만 null 이 설계다(ProfileManager 가 IsNullOrEmpty 폴백으로 기본 id 를 고른다).
assert.equal(slots.profile.avatarId, null);
assert.equal(slots.profile.frameId, null);
assert.equal(typeof slots.profile.nickname, "string");
assert.ok(slots.profile.nickname.length > 0 &&
  slots.profile.nickname.length <= NICKNAME_MAX_LENGTH,
"닉네임은 1..12 자 — 넘으면 클라 SanitizeNickname 이 저장값을 잘라 표시한다");

// 낱말표 추첨은 주입 축이라 값이 고정된다 — 문서에 그대로 실리는지만 본다.
assert.equal(buildFreshAccountSlots(STARTER, "푸른 여우").profile.nickname,
  "푸른 여우");

// 표는 100x100 이고 어떤 조합도 상한을 넘지 않아야 한다 — 넘는 낱말이 들어오면 재추첨이 늘고
// 최악에는 명사 하나로 떨어진다. 표를 늘릴 때 여기서 잡힌다.
for (const modifier of NICKNAME_MODIFIERS) {
  for (const noun of NICKNAME_NOUNS) {
    assert.ok(modifier.length + 1 + noun.length <= NICKNAME_MAX_LENGTH,
      `낱말 조합이 길다: ${modifier} ${noun}`);
  }
}

// 추첨기는 주입 가능하다 — 0 고정이면 표의 첫 낱말 한 벌이 나온다.
assert.equal(generateNickname(() => 0), `${NICKNAME_MODIFIERS[0]} ${NICKNAME_NOUNS[0]}`);

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
