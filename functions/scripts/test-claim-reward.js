// claimReward 순수 모듈 회귀. 에뮬레이터 없이 lib/ 를 직접 require 한다(test-currency.js 관용구).
//
// 여기서 지키는 것은 세 가지다.
//  1) 보상 축이 안 섞인다 — Rank/"1" 과 Tournament/"1" 은 남남이다. 섞이면 티어 보상이 정점에서 나온다.
//  2) 지급 순서가 order 로 결정된다 — 랭크 티어는 Gold+Diamond 두 줄이고 order 1·2 로 갈린다.
//  3) parseRewardRows 가 여전히 Battle 행을 읽는다 — 깨지면 submitMatchResult 가 통째로 죽는다.
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const {
  parseRankGradeRows,
  requiredPointsForTier,
  rankTierCount,
  computeCurrencyPayout,
  computeRankPayout,
  DIVISIONS_PER_GRADE,
} = require("../lib/payout.js");
const {
  parseRewardRows,
  resolveRewards,
  judgeRewardClaim,
  appendClaimedTier,
  isChapterOwnerId,
  MAX_CLAIMED_TIERS,
} = require("../lib/rewardTable.js");
const {
  parseAlbumEntryRows,
  parseAlbumThemeRows,
  lockedThemeIds,
  parseAlbumScope,
  albumScopeCardIds,
  isCompleted,
  missingCount,
} = require("../lib/completionTable.js");
// TournamentChapter 표는 tournamentTable 로 이사했다(표 하나에 파서 하나).
const {
  parseChapterNodeRows,
  chapterNodeIds,
} = require("../lib/tournamentTable.js");

// 표 한 줄. 실제 Reward 시트의 컬럼 이름 그대로다(id | ownerType | ownerId | order | rewardType | rewardId | amount).
const row = (id, ownerType, ownerId, order, rewardId, amount, rewardType = "Currency") =>
  ({id, ownerType, ownerId, order, rewardType, rewardId, amount});

// ── 축 분리: ownerType 이 다르면 같은 ownerId 라도 안 섞인다 ──────────────────
{
  const rows = parseRewardRows([
    row(1, "Rank", "1", 1, "Gold", 100),
    row(2, "Tournament", "1", 1, "Diamond", 7),
    row(3, "Album", "1", 1, "Shard", 9),
  ]);
  assert.deepEqual(resolveRewards(rows, "Rank", "1").gains, [{currency: "Gold", amount: 100}]);
  assert.deepEqual(resolveRewards(rows, "Tournament", "1").gains, [{currency: "Diamond", amount: 7}]);
  assert.deepEqual(resolveRewards(rows, "Album", "1").gains, [{currency: "Shard", amount: 9}]);

  // ownerId 는 문자열 그대로 대조한다 — "1" 과 "01" 은 다른 소유자다.
  assert.deepEqual(resolveRewards(rows, "Rank", "01").gains, []);
  assert.deepEqual(resolveRewards(rows, "Rank", "").gains, []);
}

// ── 도감 3단 키: 콜론·슬래시는 뜻이 없다, 순수 문자열 비교다 ─────────────────
{
  // 클라 AlbumSection.RewardKey 세 모양이 그대로 ownerId 다("b" · "t:테마" · "p:테마/페이지").
  const rows = parseRewardRows([
    row(1, "Album", "b", 1, "Diamond", 50),
    row(2, "Album", "t:Theme_Nature", 1, "Gold", 300),
    row(3, "Album", "p:Theme_Nature/P1", 1, "Gold", 100),
    row(4, "Album", "p:Theme_Nature/P2", 1, "Gold", 100),
  ]);
  assert.deepEqual(resolveRewards(rows, "Album", "b").gains, [{currency: "Diamond", amount: 50}]);
  assert.deepEqual(resolveRewards(rows, "Album", "t:Theme_Nature").gains, [{currency: "Gold", amount: 300}]);
  assert.deepEqual(resolveRewards(rows, "Album", "p:Theme_Nature/P1").gains, [{currency: "Gold", amount: 100}]);

  // 접두사·구분자를 빼거나 바꾸면 남남이다 — 테마 키가 페이지 보상을 먹지 않는다.
  assert.deepEqual(resolveRewards(rows, "Album", "Theme_Nature").gains, []);
  assert.deepEqual(resolveRewards(rows, "Album", "t:Theme_Nature/P1").gains, []);
  assert.deepEqual(resolveRewards(rows, "Album", "p:Theme_Nature").gains, []);
  assert.deepEqual(resolveRewards(rows, "Album", "P:Theme_Nature/P1").gains, [], "대소문자도 그대로 대조한다");

  // 도감 키는 토너먼트·랭크 축과 절대 섞이지 않는다.
  assert.deepEqual(resolveRewards(rows, "Tournament", "b").gains, []);
}

// ── 챕터는 ownerType 을 정점과 공유하고 chapter_ 접두사로만 갈린다 ───────────
{
  const rows = parseRewardRows([
    row(1, "Tournament", "node_01", 1, "Gold", 200),
    row(2, "Tournament", "chapter_01", 1, "Diamond", 30),
  ]);
  assert.deepEqual(resolveRewards(rows, "Tournament", "chapter_01").gains, [{currency: "Diamond", amount: 30}]);
  assert.deepEqual(resolveRewards(rows, "Tournament", "node_01").gains, [{currency: "Gold", amount: 200}]);

  assert.equal(isChapterOwnerId("chapter_01"), true);
  assert.equal(isChapterOwnerId("node_01"), false);
  assert.equal(isChapterOwnerId("Chapter_01"), false, "접두사는 대소문자를 가린다");
}

// ── 순서: order 오름차순, 동률은 id ──────────────────────────────────────────
{
  // 문서 순서가 뒤죽박죽이어도 order 가 이긴다.
  const rows = parseRewardRows([
    row(9, "Rank", "0", 2, "Diamond", 3),
    row(4, "Rank", "0", 1, "Gold", 100),
  ]);
  assert.deepEqual(resolveRewards(rows, "Rank", "0").gains,
    [{currency: "Gold", amount: 100}, {currency: "Diamond", amount: 3}],
    "실측 Rank/\"0\" = Gold 100 + Diamond 3");
}
{
  // order 가 같으면 뒤 줄을 버린다(클라 RewardSpec 이 중복 order 를 건너뛰는 것과 같은 규칙).
  // 남는 쪽은 id 가 작은 줄이다 — 정렬 동률이 id 로 갈리기 때문이다.
  const rows = parseRewardRows([
    row(20, "Rank", "5", 1, "Diamond", 3),
    row(10, "Rank", "5", 1, "Gold", 100),
  ]);
  const resolved = resolveRewards(rows, "Rank", "5");
  assert.deepEqual(resolved.gains, [{currency: "Gold", amount: 100}]);
  assert.deepEqual(resolved.dropped.map((d) => [d.id, d.reason]), [[20, "DuplicateOrder"]]);
}

// ── 버리기: 모르는 rewardType · 모르는 재화 · 0 이하 ─────────────────────────
{
  const rows = parseRewardRows([
    row(1, "Tournament", "node_01", 1, "Gold", 200),
    row(2, "Tournament", "node_01", 2, "12", 1, "Card"),
    row(3, "Tournament", "node_01", 3, "Ruby", 5),
    row(4, "Tournament", "node_01", 4, "Gold", 0),
    row(5, "Tournament", "node_01", 5, "Gold", -50),
    row(6, "Tournament", "node_01", 6, "", 10),
    row(7, "Tournament", "node_01", 7, "currency", 10),
    row(8, "Tournament", "node_01", 8, "gOLD", 5),
  ]);
  const resolved = resolveRewards(rows, "Tournament", "node_01");

  // 재화 이름은 대소문자를 안 가린다(클라 CurrencyCode.TryParse 가 ignoreCase 다).
  assert.deepEqual(resolved.gains,
    [{currency: "Gold", amount: 200}, {currency: "Gold", amount: 5}],
    "실측 Tournament/node_01 = Gold 200");
  assert.deepEqual(resolved.dropped.map((d) => [d.id, d.reason]), [
    [2, "UnknownRewardType"],
    [3, "UnknownCurrency"],
    [4, "NonPositiveAmount"],
    [5, "NonPositiveAmount"],
    [6, "UnknownCurrency"],
    [7, "UnknownCurrency"],
  ], "카드 보상이 저작되면 UnknownRewardType 으로 드러나야 한다");
}
{
  // rewardType 은 대소문자를 가린다 — 클라가 Enum.TryParse(ignoreCase:false) 로 읽는다.
  const rows = parseRewardRows([row(1, "Rank", "2", 1, "Gold", 100, "currency")]);
  assert.deepEqual(resolveRewards(rows, "Rank", "2").gains, []);
  assert.equal(resolveRewards(rows, "Rank", "2").dropped[0].reason, "UnknownRewardType");
}

// ── id·order 결측: 던지지 않고 0 으로 읽는다 ─────────────────────────────────
{
  // finiteInteger 로 받으면 여기서 throw 하고, 그 순간 Battle 행 파싱까지 죽어 submitMatchResult 가 멈춘다.
  const rows = parseRewardRows([
    {ownerType: "Rank", ownerId: "3", rewardType: "Currency", rewardId: "Gold", amount: 100},
  ]);
  assert.equal(rows[0].id, 0);
  assert.equal(rows[0].order, 0);
  assert.deepEqual(resolveRewards(rows, "Rank", "3").gains, [{currency: "Gold", amount: 100}]);
}

// ── 회귀: parseRewardRows 가 여전히 Battle 행을 먹인다 ───────────────────────
{
  const battle = parseRewardRows([
    row(81, "Battle", "win.perCard", 1, "Gold", 20),
    row(82, "Battle", "win.floor", 1, "Gold", 50),
    row(83, "Battle", "lose.flat", 1, "Gold", 30),
  ]);
  assert.deepEqual(computeCurrencyPayout(true, 4, battle), {currency: "Gold", amount: 80});
  assert.deepEqual(computeCurrencyPayout(true, 1, battle), {currency: "Gold", amount: 50}, "바닥이 이긴다");
  assert.deepEqual(computeCurrencyPayout(false, 0, battle), {currency: "Gold", amount: 30});

  // Battle 행은 ownerType 축이 달라 정적 보상 해석에 절대 섞이지 않는다.
  assert.deepEqual(resolveRewards(battle, "Rank", "win.perCard").gains, []);
}

// ── Battle 지급 경계: 생존 0·6 · 패배 · 표 사고 ─────────────────────────────
{
  const battle = parseRewardRows([
    row(81, "Battle", "win.perCard", 1, "Gold", 20),
    row(82, "Battle", "win.floor", 1, "Gold", 50),
    row(83, "Battle", "lose.flat", 1, "Gold", 30),
  ]);

  // 전멸승은 perCard 가 0 이라 바닥이 유일한 지급원이다 — 여기가 0 이 되면 지급 없는 승리가 생긴다.
  assert.deepEqual(computeCurrencyPayout(true, 0, battle), {currency: "Gold", amount: 50}, "생존 0 승리는 바닥이 이긴다");

  // 잠금 덱 전원 생존(6장)이 claimBattleReward 클램프의 상한과 같은 수다.
  assert.deepEqual(computeCurrencyPayout(true, 6, battle), {currency: "Gold", amount: 120}, "생존 6 = 6 x 20");

  // 패배는 생존 수를 보지 않는다 — 정액이다.
  for (const remaining of [0, 1, 6]) {
    assert.deepEqual(computeCurrencyPayout(false, remaining, battle), {currency: "Gold", amount: 30},
      `패배 지급은 생존 ${remaining} 과 무관하다`);
  }

  // 음수·소수는 순수 모듈이 던진다. 클램프는 command 몫이라 여기서 관대해지면 안 된다.
  assert.throws(() => computeCurrencyPayout(true, -1, battle), /invalid remaining cards/);
  assert.throws(() => computeCurrencyPayout(true, 1.5, battle), /invalid remaining cards/);
}
{
  // 표 사고 3종. 전부 던져야 claimBattleReward 가 RewardUnavailable 거절로 바꾼다 —
  // 조용히 0 을 돌려주면 지급 없는 문서 쓰기로 revision 만 오른다.
  assert.throws(() => computeCurrencyPayout(true, 3, parseRewardRows([])), /invalid Battle reward row/,
    "표가 비면 던진다");

  const noFloor = parseRewardRows([
    row(81, "Battle", "win.perCard", 1, "Gold", 20),
    row(83, "Battle", "lose.flat", 1, "Gold", 30),
  ]);
  assert.throws(() => computeCurrencyPayout(true, 3, noFloor), /invalid Battle reward row: win\.floor/,
    "win.floor 행이 없으면 던진다");
  assert.deepEqual(computeCurrencyPayout(false, 3, noFloor), {currency: "Gold", amount: 30},
    "패배는 승리 행을 보지 않는다");

  // 승리 두 줄의 재화가 갈리면 어느 쪽으로 줘도 틀린다 — 섞지 말고 던진다.
  const mixed = parseRewardRows([
    row(81, "Battle", "win.perCard", 1, "Gold", 20),
    row(82, "Battle", "win.floor", 1, "Diamond", 50),
  ]);
  assert.throws(() => computeCurrencyPayout(true, 3, mixed), /Battle win reward currency mismatch/);
}

// ── 표를 못 읽음 vs 그 행만 없음 ────────────────────────────────────────────
{
  // Reward 표가 통째로 비었다 = 업로드/배포 사고. 어떤 소유자에게도 자격을 잴 수 없다.
  // 여기서 토너먼트를 통과시키면 클리어 낙인만 남고 재수령이 AlreadyClaimed 로 막혀 보상을 영영 못 받는다.
  const empty = parseRewardRows([]);

  const rank = judgeRewardClaim(empty, "Rank", "3");
  assert.equal(rank.allow, false, "표가 비면 랭크도 거절");
  assert.equal(rank.specEmpty, true);
  assert.equal(rank.reason, "NotEligible");

  const tournament = judgeRewardClaim(empty, "Tournament", "node_01");
  assert.equal(tournament.allow, false, "표가 비면 토너먼트도 거절 — 클리어 낙인을 남기지 않는다");
  assert.equal(tournament.specEmpty, true);
  assert.equal(tournament.reason, "NotEligible");
  assert.deepEqual(tournament.gains, []);

  for (const [ownerType, ownerId] of [["Album", "p:Theme_Nature/P1"], ["Tournament", "chapter_01"]]) {
    const judged = judgeRewardClaim(empty, ownerType, ownerId);
    assert.equal(judged.allow, false, `표가 비면 ${ownerType}/${ownerId} 도 거절`);
    assert.equal(judged.specEmpty, true);
    assert.equal(judged.reason, "NotEligible");
  }
}
{
  // 표는 읽혔고 그 ownerId 행만 없다 = 저작 규약. 토너먼트는 지급 0건으로 통과해 해금만 넘긴다
  // (막으면 그 정점이 영영 RewardPending 으로 굳는다). 랭크는 넘길 진행이 없어 거절이 맞다.
  const rows = parseRewardRows([row(1, "Tournament", "node_01", 1, "Gold", 200)]);

  const unauthored = judgeRewardClaim(rows, "Tournament", "node_09");
  assert.equal(unauthored.allow, true, "미저작 정점은 여전히 통과한다");
  assert.equal(unauthored.authored, false);
  assert.deepEqual(unauthored.gains, []);

  const authored = judgeRewardClaim(rows, "Tournament", "node_01");
  assert.equal(authored.allow, true);
  assert.equal(authored.authored, true);
  assert.deepEqual(authored.gains, [{currency: "Gold", amount: 200}]);

  const rank = judgeRewardClaim(rows, "Rank", "3");
  assert.equal(rank.allow, false);
  assert.equal(rank.specEmpty, false, "표는 읽혔다 — 사고가 아니라 미저작이다");
  assert.equal(rank.reason, "RewardNotFound");

  // 도감·챕터는 랭크 편이다 — 넘길 진행이 없고 낙인만 남아 나중에 저작해도 AlreadyClaimed 로 막힌다.
  for (const [ownerType, ownerId] of [["Album", "b"], ["Album", "t:Theme_Nature"], ["Tournament", "chapter_01"]]) {
    const judged = judgeRewardClaim(rows, ownerType, ownerId);
    assert.equal(judged.allow, false, `미저작 ${ownerType}/${ownerId} 은 거절이다`);
    assert.equal(judged.specEmpty, false);
    assert.equal(judged.reason, "RewardNotFound");
  }

  // 정점은 접두사가 chapter_ 가 아니라서 통과 편에 남는다 — 챕터를 가르며 정점을 같이 막지 않았는지 본다.
  assert.equal(judgeRewardClaim(rows, "Tournament", "node_24").allow, true, "미저작 정점은 여전히 통과한다");
  assert.equal(judgeRewardClaim(rows, "Tournament", "chapter_01").allow, false);
}

// ── 도감 낙인 키 해석: 못 읽는 모양은 추측하지 않는다 ───────────────────────
{
  assert.deepEqual(parseAlbumScope("b"), {kind: "album"});
  assert.deepEqual(parseAlbumScope("t:Theme_Nature"), {kind: "theme", themeId: "Theme_Nature"});
  assert.deepEqual(parseAlbumScope("p:Theme_Nature/P1"),
    {kind: "page", themeId: "Theme_Nature", pageId: "P1"});

  // 모양이 아니면 null — claimReward 가 이걸 보고 RewardNotFound 로 떨어뜨린다.
  for (const bad of ["", "B", "Theme_Nature", "t:", "p:", "p:/P1", "p:Theme_Nature/", "p:A/B/C", "x:A", "bb"]) {
    assert.equal(parseAlbumScope(bad), null, `'${bad}' 는 도감 낙인 키가 아니다`);
  }
}

// ── 도감 완성 모수: 소유로 매번 다시 잰다 ───────────────────────────────────
{
  // AlbumEntry 실제 컬럼 그대로(id | themeId | pageId | cardId | order).
  const entries = parseAlbumEntryRows([
    {id: 1, themeId: "Theme_Nature", pageId: "P1", cardId: 11, order: 1},
    {id: 2, themeId: "Theme_Nature", pageId: "P1", cardId: 12, order: 2},
    {id: 3, themeId: "Theme_Nature", pageId: "P2", cardId: 13, order: 1},
    {id: 4, themeId: "Theme_Fire", pageId: "P1", cardId: 14, order: 1},
    // 못 읽는 줄은 모수에서 빠진다 — 넣으면 영영 완성되지 않는 페이지가 생긴다.
    {id: 5, themeId: "", pageId: "P1", cardId: 15, order: 1},
    {id: 6, themeId: "Theme_Fire", pageId: "P2", cardId: 0, order: 1},
  ]);
  assert.equal(entries.length, 4, "빈 키·0 이하 카드 id 줄은 버린다");

  const open = new Set();
  const page = albumScopeCardIds(entries, parseAlbumScope("p:Theme_Nature/P1"), open);
  const theme = albumScopeCardIds(entries, parseAlbumScope("t:Theme_Nature"), open);
  const album = albumScopeCardIds(entries, parseAlbumScope("b"), open);
  assert.deepEqual(page, [11, 12]);
  assert.deepEqual(theme, [11, 12, 13]);
  assert.deepEqual(album, [11, 12, 13, 14]);

  assert.equal(isCompleted(page, new Set([11, 12, 99])), true, "여분 소유는 완성을 막지 않는다");
  assert.equal(isCompleted(page, new Set([11])), false);
  assert.equal(missingCount(page, new Set([11])), 1);
  assert.equal(isCompleted(theme, new Set([11, 12])), false, "페이지만 채운 것은 테마 완성이 아니다");
  assert.equal(isCompleted(album, new Set([11, 12, 13, 14])), true);

  // 표에 없는 범위는 모수 0 이고, 모수 0 은 완성이 아니다 — 빈 집합을 "다 모았다"로 읽으면 보상이 샌다.
  assert.deepEqual(albumScopeCardIds(entries, parseAlbumScope("t:Theme_Void"), open), []);
  assert.equal(isCompleted([], new Set([11, 12, 13, 14])), false, "모수 0 은 완성이 아니다");
  assert.equal(isCompleted([], new Set()), false);

  // ── 준비 중 테마: 클라와 같은 축으로 모수에서 빠진다 ──────────────────────
  // AlbumThemeInfo 실제 컬럼 그대로(id | themeId | order | locked | displayName | description).
  const themes = parseAlbumThemeRows([
    {id: 1, themeId: "Theme_Nature", order: 1, locked: 0, displayName: "자연", description: ""},
    {id: 2, themeId: "Theme_Fire", order: 2, locked: 1, displayName: "불꽃", description: "준비 중"},
    // 테마 키가 빈 줄은 버린다 — 남기면 잠금 판정이 오염된다.
    {id: 3, themeId: "", order: 3, locked: 1, displayName: "", description: ""},
  ]);
  assert.equal(themes.length, 2, "빈 테마 키 줄은 버린다");

  const locked = lockedThemeIds(themes);
  assert.deepEqual([...locked], ["Theme_Fire"]);

  // 전체("b")는 준비 중 테마의 칸을 요구하지 않는다 — 요구하면 완성이 영영 불가능해진다.
  assert.deepEqual(albumScopeCardIds(entries, parseAlbumScope("b"), locked), [11, 12, 13],
    "준비 중 테마 카드(14)는 전체 모수에서 빠진다");
  assert.equal(isCompleted(albumScopeCardIds(entries, parseAlbumScope("b"), locked), new Set([11, 12, 13])),
    true, "공개 테마만 다 모으면 전체 완성이다");

  // 준비 중 테마를 직접 가리키는 키는 모수 0 이고, 모수 0 은 완성이 아니다 —
  // 조작 호출로 준비 중 테마의 보상을 긁어 가는 길이 함께 닫힌다.
  assert.deepEqual(albumScopeCardIds(entries, parseAlbumScope("t:Theme_Fire"), locked), []);
  assert.deepEqual(albumScopeCardIds(entries, parseAlbumScope("p:Theme_Fire/P1"), locked), []);
  assert.equal(isCompleted(albumScopeCardIds(entries, parseAlbumScope("t:Theme_Fire"), locked), new Set([14])),
    false, "준비 중 테마는 카드를 다 가져도 완성이 아니다");

  // 공개 테마 쪽 판정은 잠금 축이 생겨도 그대로다.
  assert.deepEqual(albumScopeCardIds(entries, parseAlbumScope("t:Theme_Nature"), locked), [11, 12, 13]);
  assert.deepEqual(albumScopeCardIds(entries, parseAlbumScope("p:Theme_Nature/P1"), locked), [11, 12]);
}

// ── 챕터 완주 모수: 클리어 정점으로 잰다 ────────────────────────────────────
{
  const chapters = parseChapterNodeRows([
    {id: 1, chapterId: "chapter_01", nodeId: "node_01", order: 1},
    {id: 2, chapterId: "chapter_01", nodeId: "node_02", order: 2},
    {id: 3, chapterId: "chapter_02", nodeId: "node_03", order: 1},
    {id: 4, chapterId: "chapter_02", nodeId: "", order: 2},
  ]);
  assert.equal(chapters.length, 3, "빈 정점 키 줄은 버린다");

  const first = chapterNodeIds(chapters, "chapter_01");
  assert.deepEqual(first, ["node_01", "node_02"]);
  assert.equal(isCompleted(first, new Set(["node_01"])), false);
  assert.equal(isCompleted(first, new Set(["node_01", "node_02"])), true);
  assert.equal(isCompleted(first, new Set(["node_02", "node_01", "node_03"])), true, "순서는 상관없다");

  // 저작되지 않은 챕터는 모수 0 이라 완주가 아니다.
  assert.deepEqual(chapterNodeIds(chapters, "chapter_09"), []);
  assert.equal(isCompleted(chapterNodeIds(chapters, "chapter_09"), new Set(["node_01"])), false);
}

// ── claimedTiers 상한: 룰(firestore.rules:98, size() <= 20)과 같이 움직인다 ──
{
  assert.equal(MAX_CLAIMED_TIERS, 20, "firestore.rules:98 의 claimedTiers.size() <= 20 과 같은 값이어야 한다");

  const filled = Array.from({length: MAX_CLAIMED_TIERS - 1}, (_, i) => i);
  assert.deepEqual(appendClaimedTier(filled, MAX_CLAIMED_TIERS - 1), [...filled, MAX_CLAIMED_TIERS - 1],
    "상한까지는 쓴다");

  // 상한에 도달한 뒤의 수령은 문서를 쓰지 않는다 — 21칸 문서를 쓰면 그 계정의 이후 클라 저장이 전부
  // PERMISSION_DENIED 가 되고 delete 도 룰에 막혀 복구 경로가 없다.
  const full = Array.from({length: MAX_CLAIMED_TIERS}, (_, i) => i);
  assert.equal(appendClaimedTier(full, MAX_CLAIMED_TIERS), null, "상한 초과는 null — 부르는 쪽이 거절한다");
}

// ── 티어 요구점수 유도 ──────────────────────────────────────────────────────
// 실측 RankGrade 5행: entryPoints [100,260,420,580,740] · pointsPerDivision 40 → 5등급 x 4단계 = 20티어.
const GRADES = parseRankGradeRows([100, 260, 420, 580, 740].map((entryPoints, id) =>
  ({id, entryPoints, pointsPerDivision: 40, winPoints: 10, losePoints: 10})));

assert.equal(DIVISIONS_PER_GRADE, 4);
assert.equal(rankTierCount(GRADES), 20);

assert.equal(requiredPointsForTier(0, GRADES), 100, "브론즈 1");
assert.equal(requiredPointsForTier(3, GRADES), 220, "브론즈 4 = 100 + 3*40");
assert.equal(requiredPointsForTier(4, GRADES), 260, "실버 1");
assert.equal(requiredPointsForTier(19, GRADES), 860, "다이아 4 = 740 + 3*40");

// 범위 밖은 null — claimReward 가 이걸 보고 RewardNotFound 로 떨어뜨린다.
assert.equal(requiredPointsForTier(-1, GRADES), null);
assert.equal(requiredPointsForTier(20, GRADES), null);
assert.equal(requiredPointsForTier(1.5, GRADES), null);
assert.equal(requiredPointsForTier(Number.NaN, GRADES), null);

// resolveTierIndex 의 역함수여야 한다 — 어긋나면 도달하지도 않은 티어가 수령 가능해진다.
for (let tier = 0; tier < rankTierCount(GRADES); tier++) {
  const required = requiredPointsForTier(tier, GRADES);
  assert.equal(computeRankPayout(required, true, GRADES).beforeTierIndex, tier,
    `티어 ${tier} 의 요구점수 ${required} 는 정확히 그 티어여야 한다`);
  if (tier > 0) {
    assert.equal(computeRankPayout(required - 1, true, GRADES).beforeTierIndex, tier - 1,
      `요구점수 -1 은 아직 티어 ${tier} 가 아니다`);
  }
}

// ── 임계치 드리프트: 위 상수 ↔ Assets/SO/Rank/RankConfig.asset ───────────────
// 클라 RankConfig.TryGetTier 가 진실원이다. 갈리면 서버가 자격을 다르게 재고,
// 유저에게는 "받을 수 있는데 안 받아지는" 티어로 보인다.
{
  const assetPath = path.join(__dirname, "..", "..", "Assets", "SO", "Rank", "RankConfig.asset");
  if (fs.existsSync(assetPath)) {
    const text = fs.readFileSync(assetPath, "utf8");
    const entryPoints = [...text.matchAll(/entryPoints:\s*(\d+)/g)].map((m) => Number(m[1]));
    const divisions = [...text.matchAll(/pointsPerDivision:\s*(\d+)/g)].map((m) => Number(m[1]));
    assert.equal(entryPoints.length, GRADES.length, "RankConfig.asset 의 등급 수가 달라졌다");
    for (let i = 0; i < GRADES.length; i++) {
      assert.equal(GRADES[i].entryPoints, entryPoints[i], `등급 ${i} entryPoints 드리프트`);
      assert.equal(GRADES[i].pointsPerDivision, divisions[i], `등급 ${i} pointsPerDivision 드리프트`);
    }
  } else {
    console.log("test-claim-reward: RankConfig.asset 없음 — 드리프트 대조 생략");
  }
}

console.log("test-claim-reward: ok");
