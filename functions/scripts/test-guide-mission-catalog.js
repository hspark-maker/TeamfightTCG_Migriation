"use strict";

const assert = require("node:assert/strict");
const {test} = require("node:test");
const {parseMissionCatalog} = require("../lib/missions/catalog");
const {judgeMissionClaim} = require("../lib/missions/judgeMissionClaim");
const {applyPeriodReset, missionResponse} = require("../lib/missions/missionStore");

const row = (id, sortOrder, guideActId = 10, guideActName = "출발") => ({
  missionId: "guide." + id, period: "guide", eventKey: "Guide." + id,
  targetCount: 1, title: "미션 " + id, description: "목표 " + id,
  passExp: 0, sortOrder, enabled: 1, guideActId, guideActName,
});

test("authored act names and membership survive parsing in arbitrary row order", () => {
  const catalog = parseMissionCatalog([row("c", "3", "20", "에이스"),
    row("a", "1", "10", "새 출발"), row("b", "2", "10", "새 출발")]);
  assert.deepEqual(catalog.map((mission) => [mission.id, mission.guideActId, mission.guideActName]),
    [["guide.c", 20, "에이스"], ["guide.a", 10, "새 출발"], ["guide.b", 10, "새 출발"]]);
});

test("active guides require positive integer act IDs and nonempty names", () => {
  for (const id of [undefined, 0, -1, 1.5, "", "bad", Number.MAX_SAFE_INTEGER + 1]) {
    const mission = {...row("a", 1), guideActId: id};
    assert.throws(() => parseMissionCatalog([mission]), /invalid guideActId/);
  }
  for (const name of [undefined, "", "  "]) {
    assert.throws(() => parseMissionCatalog([{...row("a", 1), guideActName: name}]), /empty guideActName/);
  }
});

test("conflicting names, duplicate guide orders, and interleaved acts are rejected", () => {
  assert.throws(() => parseMissionCatalog([row("a", 1), row("b", 2, 10, "다른 이름")]), /conflicting names/);
  assert.throws(() => parseMissionCatalog([row("a", 1), row("b", 1)]), /Duplicated guide sortOrder/);
  assert.throws(() => parseMissionCatalog([
    row("c", 3), row("a", 1), row("b", 2, 20, "다음 막"),
  ]), /non-contiguous/);
});

test("disabled guides and daily/weekly missions do not participate in act validation", () => {
  const disabled = {...row("disabled", 1, 0, ""), enabled: 0};
  const ordinary = ["daily", "weekly"].map((period) => ({
    ...row("a", 1, 0, ""), missionId: period + ".a", period,
  }));
  const catalog = parseMissionCatalog([row("a", 1), disabled, ...ordinary]);
  assert.equal(catalog.length, 4);
  assert.doesNotThrow(() => parseMissionCatalog([row("a", 1), row("c", 3),
    {...row("b", 2, 20, "다음 막"), enabled: 0}]));
});

const legacyRow = (id, sortOrder) => {
  const mission = row(id, sortOrder);
  delete mission.guideActId;
  delete mission.guideActName;
  return mission;
};
const legacyOptions = {requireGuideActs: false};

test("legacy missing act columns are accepted only with explicit compatibility option", () => {
  const rows = [legacyRow("a", 1), legacyRow("b", 2)];
  assert.throws(() => parseMissionCatalog(rows), /invalid guideActId/);
  assert.throws(() => parseMissionCatalog(rows, {requireGuideActs: true}), /invalid guideActId/);
  const catalog = parseMissionCatalog(rows, legacyOptions);
  assert.deepEqual(catalog.map((mission) => [mission.guideActId, mission.guideActName]), [[0, ""], [0, ""]]);
});

test("legacy mode still rejects present but invalid or incomplete act metadata", () => {
  for (const metadata of [
    {guideActId: 0, guideActName: ""}, {guideActId: null, guideActName: null},
    {guideActId: 10}, {guideActName: "출발"}, {guideActId: undefined},
    {guideActId: 10, guideActName: " "}, {guideActId: -1, guideActName: "출발"},
  ]) {
    assert.throws(() => parseMissionCatalog([{...legacyRow("a", 1), ...metadata}], legacyOptions),
      /invalid guideActId|empty guideActName/);
  }
});

test("legacy mode retains duplicate order and authored act validation", () => {
  assert.throws(() => parseMissionCatalog([legacyRow("a", 1), legacyRow("b", 1)], legacyOptions),
    /Duplicated guide sortOrder/);
  assert.throws(() => parseMissionCatalog([row("a", 1), row("b", 2, 10, "다른 이름")], legacyOptions),
    /conflicting names/);
  assert.throws(() => parseMissionCatalog([
    row("a", 1), row("b", 2, 20, "다음 막"), row("c", 3),
  ], legacyOptions), /non-contiguous/);
  assert.doesNotThrow(() => parseMissionCatalog([
    legacyRow("a", 1), row("b", 2), legacyRow("c", 3),
  ], legacyOptions));
});

test("reordering and moving missions preserve claims and use the newly authored unlock order", () => {
  const before = parseMissionCatalog([row("a", 1), row("b", 2), row("c", 3, 20, "에이스")]);
  const after = parseMissionCatalog([
    row("c", 1, 20, "새 에이스"), row("a", 2, 20, "새 에이스"), row("b", 3, 10, "출발"),
  ]);
  const period = {daily: "new-day", weekly: "new-week", dailyResetAtMs: 1, weeklyResetAtMs: 2};
  const stored = {dailyKey: "old-day", weeklyKey: "old-week", passExp: 0,
    progress: {"guide.Guide.a": 1, "guide.Guide.b": 1, "guide.Guide.c": 1},
    claimed: {"guide.a": true}};
  const snapshot = structuredClone(stored);
  const state = applyPeriodReset(stored, period);
  assert.equal(judgeMissionClaim("guide.b", state, before).allow, true);
  assert.equal(judgeMissionClaim("guide.c", state, before).reason, "NotEligible");
  assert.equal(judgeMissionClaim("guide.a", state, after).reason, "AlreadyClaimed");
  assert.equal(judgeMissionClaim("guide.b", state, after).reason, "NotEligible");
  assert.equal(judgeMissionClaim("guide.c", state, after).allow, true);
  assert.deepEqual(missionResponse(state, period, after).claimed, {"guide.a": true});
  assert.deepEqual(stored, snapshot);
});
