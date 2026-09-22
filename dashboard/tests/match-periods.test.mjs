import test from "node:test";
import assert from "node:assert/strict";
import { presetRange, rangeError, groupDaily } from "../src/match-periods.ts";

const now = Date.parse("2026-09-22T16:30:00Z"); // Korean local date is already Sep 23.

test("calendar presets use UTC dates and Monday weeks across month and year boundaries", () => {
  assert.deepEqual(presetRange("yesterday", now), { startDate: "2026-09-21", endDate: "2026-09-21" });
  assert.deepEqual(presetRange("week", now), { startDate: "2026-09-21", endDate: "2026-09-22" });
  assert.deepEqual(presetRange("month", now), { startDate: "2026-09-01", endDate: "2026-09-22" });
  const boundary = Date.parse("2027-01-01T01:00:00Z");
  assert.deepEqual(presetRange("yesterday", boundary), { startDate: "2026-12-31", endDate: "2026-12-31" });
  assert.deepEqual(presetRange("week", boundary), { startDate: "2026-12-28", endDate: "2027-01-01" });
  assert.deepEqual(presetRange("month", boundary), { startDate: "2027-01-01", endDate: "2027-01-01" });
});

test("custom date validation rejects missing, malformed, rollover, reversed and future dates", () => {
  for (const startDate of ["", "2026-9-01", "not-a-date", "2026-02-30", "2025-02-29", "2026-13-01"]) {
    assert.notEqual(rangeError({ startDate, endDate: "2026-09-22" }, now), null, startDate);
  }
  assert.match(rangeError({ startDate: "2026-09-22", endDate: "2026-09-21" }, now), /늦을 수/);
  assert.match(rangeError({ startDate: "2026-09-22", endDate: "2026-09-23" }, now), /미래/);
  assert.equal(rangeError({ startDate: "2024-02-29", endDate: "2024-02-29" }, now), null);
  assert.equal(rangeError({ startDate: "2026-09-22", endDate: "2026-09-22" }, now), null);
});

test("custom ranges count both endpoints and permit exactly 90 days", () => {
  assert.equal(rangeError({ startDate: "2026-06-25", endDate: "2026-09-22" }, now), null);
  assert.match(rangeError({ startDate: "2026-06-24", endDate: "2026-09-22" }, now), /90일/);
});

test("weekly grouping keeps measured zero, unknown days and partial known subtotals distinct", () => {
  const daily = [
    { day: "2026-09-28", exists: true, settled: 0 },
    { day: "2026-09-25", exists: true, settled: 8 },
    { day: "2026-09-24", exists: true, settled: null },
    { day: "2026-09-23", exists: false, settled: 999 },
    { day: "2026-09-22", exists: true, settled: 0 },
    { day: "2026-09-21", exists: true, settled: 10 },
    { day: "2026-09-20", exists: false, settled: null },
  ];
  const before = structuredClone(daily);
  assert.deepEqual(groupDaily(daily, "week"), [
    { key: "2026-09-14", startDate: "2026-09-20", endDate: "2026-09-20", expectedDays: 1, knownDays: 0, settled: null },
    { key: "2026-09-21", startDate: "2026-09-21", endDate: "2026-09-25", expectedDays: 5, knownDays: 3, settled: 18 },
    { key: "2026-09-28", startDate: "2026-09-28", endDate: "2026-09-28", expectedDays: 1, knownDays: 1, settled: 0 },
  ]);
  assert.deepEqual(daily, before);
});

test("month grouping sums only requested days and keeps partial first and last months", () => {
  assert.deepEqual(groupDaily([
    { day: "2026-12-30", exists: true, settled: 11 },
    { day: "2026-12-31", exists: false, settled: null },
    { day: "2027-01-01", exists: true, settled: 2 },
    { day: "2027-01-02", exists: true, settled: 3 },
  ], "month"), [
    { key: "2026-12", startDate: "2026-12-30", endDate: "2026-12-31", knownDays: 1, expectedDays: 2, settled: 11 },
    { key: "2027-01", startDate: "2027-01-01", endDate: "2027-01-02", knownDays: 2, expectedDays: 2, settled: 5 },
  ]);
  assert.deepEqual(groupDaily([], "month"), []);
});

test("daily grouping sorts chronologically without turning missing counts into zero", () => {
  assert.deepEqual(groupDaily([
    { day: "2026-09-22", exists: false, settled: null },
    { day: "2026-09-21", exists: true, settled: 0 },
  ], "day"), [
    { key: "2026-09-21", startDate: "2026-09-21", endDate: "2026-09-21", knownDays: 1, expectedDays: 1, settled: 0 },
    { key: "2026-09-22", startDate: "2026-09-22", endDate: "2026-09-22", knownDays: 0, expectedDays: 1, settled: null },
  ]);
});
