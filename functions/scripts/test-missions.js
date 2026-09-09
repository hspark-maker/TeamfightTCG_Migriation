// 미션 집계·리셋·수령의 순수 회귀. 에뮬레이터 없이 lib/ 를 직접 require 한다(test-growth.js 관용구).
//
// 여기서 지키는 것은 다섯 축이다.
//  1) 리셋 경계 — dailyKey 가 바뀌면 진행도와 낙인이 **함께** 비고, 주간 축은 안 다친다
//  2) 진행도 상한 — target 을 넘겨 쌓여도 수령은 한 번뿐
//  3) 미완료 수령 거절
//  4) enabled=0 — 목록에서 빠지고 수령도 거절되지만 **진행도는 계속 쌓인다**
//  5) 접두 규칙 — bump 가 쓰는 키와 조회가 보는 키가 같다
//
// 중복 수령(재전송)은 mutateSave 의 영수증 배관이 막고 그 회귀는 test-receipt-idempotency.js 가
// 이미 진다. 여기서는 낙인이 두 번째 수령을 거절하는지만 본다.
const assert = require("node:assert/strict");

const {missionPeriod, MISSION_TIME_ZONE} = require("../lib/missions/period.js");
const {
  readMissions, applyPeriodReset, commitMissionBump, commitMissionClaim,
  progressKey, progressOf, isClaimed, missionResponse,
} = require("../lib/missions/missionStore.js");
const {
  missionCatalog, enabledMissions, findMission, missionCatalogIssues,
} = require("../lib/missions/catalog.js");
const {judgeMissionClaim} = require("../lib/missions/judgeMissionClaim.js");
const {judgeRewardClaim, parseRewardRows} = require("../lib/rewardTable.js");

const snapshotOf = (data) => ({exists: data !== undefined, data: () => data});
// 트랜잭션 스텁. commitMission* 는 set 한 번만 부른다.
const fakeTx = () => {
  const writes = [];
  return {writes, set: (ref, value) => writes.push({ref, value})};
};
const bumpOf = (state, period) => ({ref: "missions/current", state, period});

// ── 저작 검증: 적재 시점에 던지지 않고 목록으로 낸다 ─────────────────────────
// Cloud Functions 에서 적재 중 throw 는 그 인스턴스의 모든 callable 을 죽인다.
assert.deepEqual(missionCatalogIssues(), [], "기본 카탈로그에 저작 오류가 없어야 한다");
assert.deepEqual(
  missionCatalogIssues([{
    id: "weekly.oops", period: "daily", event: "OpenPack", target: 1,
    enabled: true, title: "제목", description: "설명", passExp: 1, sortOrder: 1,
  }]),
  ["Mission 'weekly.oops' must start with 'daily.'."],
  "접두사가 주기와 어긋나면 잡는다 — 안 잡으면 리셋 축이 뒤섞인다");
assert.equal(
  missionCatalogIssues([{
    id: "daily.nothing", period: "daily", event: "OpenPack", target: 1,
    enabled: true, title: "", description: "설명", passExp: 0, sortOrder: 1,
  }]).length, 1, "표시 문구가 비어 있는 미션은 저작 오류다");

// ── 기간 키: Asia/Seoul 05:00 경계 ───────────────────────────────────────────
assert.equal(MISSION_TIME_ZONE, "Asia/Seoul");
// 2026-09-04 04:59 KST = 2026-09-03T19:59Z → 아직 전날이다.
const beforeReset = missionPeriod(Date.parse("2026-09-03T19:59:00Z"));
const afterReset = missionPeriod(Date.parse("2026-09-03T20:01:00Z"));
assert.equal(beforeReset.daily, "2026-09-03");
assert.equal(afterReset.daily, "2026-09-04", "05:00 KST 를 넘기면 다음 날이다");
// 2026-09-03 은 목요일이라 05:00 경계를 넘어도 같은 주다.
assert.equal(beforeReset.weekly, afterReset.weekly, "같은 주 안에서는 주간 키가 안 바뀐다");
assert.ok(afterReset.dailyResetAtMs > Date.parse("2026-09-03T20:01:00Z"));

// ── 읽기: 문서 부재는 정상 ───────────────────────────────────────────────────
assert.deepEqual(readMissions(snapshotOf(undefined)), {
  dailyKey: "", weeklyKey: "", progress: {}, claimed: {}, passExp: 0,
}, "ensureAccount 가 이 문서를 만들지 않으므로 부재가 기본 상태다");

// ── 1) 리셋 경계: 진행도와 낙인이 함께, 축별로 ───────────────────────────────
const stale = readMissions(snapshotOf({
  dailyKey: "2026-09-03", weeklyKey: "2026-08-31",
  progress: {"daily.OpenPack": 5, "weekly.OpenPack": 12},
  claimed: {"daily.openPack1": true, "weekly.openPack10": true},
  passExp: 70,
}));
// 일일만 갈린 경우 — 주간은 그대로여야 한다.
const dailyOnly = applyPeriodReset(stale, {
  daily: "2026-09-04", weekly: "2026-08-31", dailyResetAtMs: 0, weeklyResetAtMs: 0,
});
assert.equal(dailyOnly.progress["daily.OpenPack"], undefined, "일일 진행도는 비운다");
assert.equal(dailyOnly.claimed["daily.openPack1"], undefined, "일일 낙인도 함께 비운다");
assert.equal(dailyOnly.progress["weekly.OpenPack"], 12, "주간 진행도는 안 다친다");
assert.equal(dailyOnly.claimed["weekly.openPack10"], true, "주간 낙인도 안 다친다");
assert.equal(dailyOnly.passExp, 70, "패스 경험치는 주기 리셋을 타지 않는다");
// 둘 다 갈린 경우.
const bothReset = applyPeriodReset(stale, {
  daily: "2026-09-04", weekly: "2026-09-07", dailyResetAtMs: 0, weeklyResetAtMs: 0,
});
assert.deepEqual(bothReset.progress, {});
assert.deepEqual(bothReset.claimed, {});
// 기간이 같으면 손대지 않는다.
assert.equal(applyPeriodReset(stale, {
  daily: "2026-09-03", weekly: "2026-08-31", dailyResetAtMs: 0, weeklyResetAtMs: 0,
}), stale, "기간이 같으면 새 객체를 만들지 않는다");

// ── 레거시 문서 폴백 ─────────────────────────────────────────────────────────
const legacy = readMissions(snapshotOf({
  dailyPeriod: "2026-09-03", weeklyPeriod: "2026-08-31",
  daily: {OpenPack: 2}, weekly: {OpenPack: 9},
  claimed: ["daily.openPack1"], battlePassXp: 30,
}));
assert.equal(legacy.dailyKey, "2026-09-03", "옛 dailyPeriod 를 읽는다");
assert.equal(legacy.progress["daily.OpenPack"], 2, "옛 daily 맵을 접두 키로 접는다");
assert.equal(legacy.progress["weekly.OpenPack"], 9);
assert.equal(legacy.claimed["daily.openPack1"], true, "배열 낙인을 맵으로 접는다");
assert.equal(legacy.passExp, 30, "옛 battlePassXp 를 읽는다");

// ── 5) 접두 규칙: bump 가 쓴 키를 조회가 그대로 본다 ─────────────────────────
const period = missionPeriod(Date.parse("2026-09-04T03:00:00Z"));
const bump = bumpOf(applyPeriodReset(readMissions(snapshotOf(undefined)), period), period);
const tx = fakeTx();
commitMissionBump(tx, bump, "OpenPack", 1, "now");
assert.equal(bump.state.progress[progressKey("daily", "OpenPack")], 1);
assert.equal(bump.state.progress[progressKey("weekly", "OpenPack")], 1,
  "한 번의 행동이 일일·주간에 모두 잡힌다");
assert.equal(tx.writes.length, 1, "쓰기는 한 번이다");
assert.equal(tx.writes[0].value.dailyKey, period.daily, "문서에는 이번 기간 키가 실린다");
assert.equal(progressOf(bump.state, findMission("daily.openPack1")), 1);

// ── 3) 미완료 수령 거절 ──────────────────────────────────────────────────────
const notYet = judgeMissionClaim("weekly.openPack10", bump.state);
assert.equal(notYet.allow, false);
assert.equal(notYet.reason, "NotEligible");
assert.equal(notYet.progress, 1, "같은 OpenPack 이벤트를 쓰는 단계형 미션은 진행도를 공유한다");
// 없는 미션과 꺼진 미션을 가른다 — 운영이 로그로 구분해야 한다.
assert.equal(judgeMissionClaim("daily.nope", bump.state).reason, "MissionNotFound");

// ── 4) enabled=0 ─────────────────────────────────────────────────────────────
const disabled = missionCatalog().find((m) => !m.enabled);
assert.ok(disabled, "전투 축이 꺼진 채로 저작돼 있어야 한다(켜기만 하면 붙는 구조의 근거)");
assert.equal(enabledMissions().some((m) => m.id === disabled.id), false,
  "꺼진 미션은 목록에서 빠진다");
// 진행도는 그래도 쌓인다 — bump 는 enabled 를 보지 않는다.
const disabledBump = bumpOf(applyPeriodReset(readMissions(snapshotOf(undefined)), period), period);
commitMissionBump(fakeTx(), disabledBump, disabled.event, disabled.target, "now");
assert.equal(progressOf(disabledBump.state, disabled), disabled.target,
  "꺼져 있어도 진행도는 쌓인다 — 안 그러면 켜는 날 전 유저가 0부터 시작한다");
// 그런데도 수령은 거절된다. 목록에서만 빼면 구 클라가 id 를 직접 보내 받는다.
assert.equal(judgeMissionClaim(disabled.id, disabledBump.state).reason, "MissionDisabled");

// ── 2) 진행도 상한: 초과 누적이 중복 수령으로 이어지지 않는다 ────────────────
const target = findMission("daily.openPack1");
commitMissionBump(fakeTx(), bump, "OpenPack", 9, "now");
assert.equal(progressOf(bump.state, target), 10, "목표를 넘겨 쌓인다");
assert.equal(judgeMissionClaim(target.id, bump.state).allow, true, "달성했으므로 한 번은 통과한다");
commitMissionClaim(fakeTx(), bump, target.id, target.passExp, "now");
assert.equal(isClaimed(bump.state, target.id), true);
assert.equal(bump.state.passExp, target.passExp,
  "패스 경험치가 낙인과 같은 자리에서 붙는다");
const second = judgeMissionClaim(target.id, bump.state);
assert.equal(second.allow, false);
assert.equal(second.reason, "AlreadyClaimed", "진행도가 10이어도 두 번째 수령은 낙인이 막는다");
// 수령이 카운터를 깎지 않는다 — 깎으면 같은 이벤트를 세는 주간 미션이 함께 무너진다.
assert.equal(progressOf(bump.state, findMission("weekly.openPack10")), 10);

// ── 응답 봉투: 참조를 그대로 싣지 않는다 ─────────────────────────────────────
const envelope = missionResponse(bump.state, period);
assert.equal(envelope.dailyKey, period.daily);
assert.equal(envelope.passExp, target.passExp);
commitMissionBump(fakeTx(), bump, "OpenPack", 1, "now");
assert.equal(envelope.progress[progressKey("daily", "OpenPack")], 10,
  "봉투는 만든 시점의 값을 유지한다(맵을 복사해서 낸다)");

// ── Mission 재화 보상은 Reward 표가 진실원 ────────────────────────────────
const rewardRows = parseRewardRows([
  {id: 85, ownerType: "Mission", ownerId: "daily.openPack1", order: 1,
    rewardType: "Currency", rewardId: "Gold", amount: 120},
]);
const reward = judgeRewardClaim(rewardRows, "Mission", "daily.openPack1");
assert.equal(reward.allow, true);
assert.deepEqual(reward.gains, [{currency: "Gold", amount: 120}]);
assert.equal(judgeRewardClaim(rewardRows, "Mission", "daily.missing").allow, false,
  "Mission 시트와 짝이 없는 Reward 행은 수령을 막는다");

// ── 시트 ↔ 런타임 카탈로그 이중 진실원 방어 ──────────────────────────────────
// Mission 시트가 서버 상수(catalog.ts)와 손으로 동기화되는 동안, 둘이 갈리면
// 화면에 보이는 목표와 집행되는 목표가 달라진다. 그 어긋남은 유저 거절로만 드러나므로
// 여기서 행 단위로 묶는다. 시트 연동이 끝나면 이 블록은 통째로 사라진다.
//
// 파서는 일부러 단순하다 — 이 두 시트에는 따옴표로 감싼 필드가 없다. 생기면 여기가 먼저 깨진다.
const fs = require("node:fs");
const path = require("node:path");

const readSheet = (name) => {
  const text = fs.readFileSync(
    path.join(__dirname, "..", "..", "docs", "SpecData", `${name}_sheet.csv`), "utf8");
  // BOM 은 코드포인트로 지운다 — 정규식 이스케이프가 편집 과정에서 깨지기 쉽다.
  const body = text.charCodeAt(0) === 0xFEFF ? text.slice(1) : text;
  const lines = body.split(String.fromCharCode(10))
    .map((line) => line.replace(String.fromCharCode(13), ""))
    .filter((line) => line.length > 0);
  const fields = lines[1].split(",");
  // 0=한글설명 1=필드명 2=타입, 그 뒤가 데이터다.
  return lines.slice(3).map((line) => {
    const cells = line.split(",");
    return Object.fromEntries(fields.map((key, i) => [key, cells[i] ?? ""]));
  });
};

const sheetMissions = readSheet("Mission");
const sheetRewards = readSheet("Reward").filter((row) => ["Mission", "Guide"].includes(row.ownerType));

assert.equal(sheetMissions.length, missionCatalog().length,
  "Mission 시트 행 수와 런타임 카탈로그 수가 같아야 한다");
for (const row of sheetMissions) {
  const mission = findMission(row.missionId);
  assert.ok(mission, `시트의 ${row.missionId} 가 런타임 카탈로그에 없다`);
  assert.equal(String(mission.enabled ? 1 : 0), row.enabled, `${row.missionId} enabled 불일치`);
  assert.equal(mission.period, row.period, `${row.missionId} period 불일치`);
  // 시트 열은 eventKey 다 — C# 생성 코드에서 `event` 가 예약어라 필드로 못 쓴다.
  // 런타임 카탈로그(catalog.ts)의 필드명은 TS 라 제약이 없어 event 그대로 둔다.
  assert.equal(mission.event, row.eventKey, `${row.missionId} event 불일치`);
  assert.equal(String(mission.target), row.targetCount, `${row.missionId} targetCount 불일치`);
  assert.equal(mission.title, row.title, `${row.missionId} title 불일치`);
  assert.equal(mission.description, row.description, `${row.missionId} description 불일치`);
  assert.equal(String(mission.passExp), row.passExp, `${row.missionId} passExp 불일치`);
  assert.equal(String(mission.sortOrder), row.sortOrder, `${row.missionId} sortOrder 불일치`);
}

// 짝 검사: 보상 행이 없으면 켜는 순간 RewardNotFound 로 막힌다. 꺼진 미션도 미리 저작해야 한다.
const rewardOwners = new Set(sheetRewards.map((row) => row.ownerId));
for (const row of sheetMissions) {
  if (row.enabled !== "1") continue;
  assert.ok(rewardOwners.has(row.missionId),
    `Reward 시트에 ownerType=Mission / ownerId=${row.missionId} 행이 없다`);
}
const missionIds = new Set(sheetMissions.map((row) => row.missionId));
for (const owner of rewardOwners) {
  assert.ok(missionIds.has(owner), `Reward 의 Mission/${owner} 에 대응하는 MissionDef 행이 없다`);
}

// sortOrder 는 같은 주기 안에서 유일해야 한다 — 겹치면 화면 순서가 비결정적이다.
for (const kind of ["daily", "weekly", "guide"]) {
  const orders = sheetMissions.filter((row) => row.enabled === "1" && row.period === kind).map((row) => row.sortOrder);
  assert.equal(new Set(orders).size, orders.length, `${kind} sortOrder 가 중복된다`);
}

console.log("test-missions: ok");
