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
  parseRewardRows,
  parseRankGradeRows,
  resolveRewards,
  requiredPointsForTier,
  rankTierCount,
  computeCurrencyPayout,
  computeRankPayout,
  DIVISIONS_PER_GRADE,
} = require("../lib/payout.js");

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
