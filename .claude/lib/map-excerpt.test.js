#!/usr/bin/env node
"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { MAX_CHARS, MAX_LINES, buildMapExcerpt, searchCommandPolicy, searchTerms } = require("./map-excerpt.js");
const { SEARCH_COMMANDS } = require("./search-detect.js");

const root = fs.mkdtempSync(path.join(os.tmpdir(), "map-excerpt-test-"));
const mapFile = path.join(root, "map.md");
const transcript = path.join(root, "session.jsonl");

try {
  for (const command of SEARCH_COMMANDS) {
    assert.ok(searchCommandPolicy(command), `${command} must have an explicit query extraction policy`);
  }
  assert.deepEqual(searchTerms("Bash", { command: "dir /s /b *CardView*.cs" }), ["CardView"]);
  assert.deepEqual(searchTerms("Bash", { command: "dir /s /b C:\\repo\\Assets\\Scripts\\*CardView*.cs" }), ["CardView"]);
  assert.deepEqual(searchTerms("PowerShell", { command: "dir -Recurse -Filter *ZzzNope*.cs" }), ["ZzzNope"]);
  fs.writeFileSync(mapFile, [
    "# 지도", "## 전투 (`Battle/`)",
    "- 카드 표시: `CardView`", "- 카드 장식: `CardDecorView`", "- 카드 데이터: `CardData`",
    "- 카드 풀: `CardPool`", "- 카드 선택: `CardPicker`", "- 유산 카드: `LegacySynergyEffect` · `CardView`",
  ].join("\n"));
  const events = [
    { type: "user", uuid: "r1", message: { role: "user", content: "유산 카드 작업" } },
    ...Array.from({ length: 5 }, (_, index) => ({
      type: "assistant", requestId: `a${index}`,
      message: { content: [{ type: "tool_use", name: "Grep", input: { pattern: "Legacy" } }] },
    })),
  ];
  fs.writeFileSync(transcript, events.map((event) => JSON.stringify(event)).join("\n"));

  assert.deepEqual(searchTerms("Grep", { pattern: "CardView|도발", path: "Assets/Scripts/Battle" }), ["CardView", "도발"]);
  assert.deepEqual(searchTerms("Bash", { command: "grep -rn 'CardView|도발' Assets/Scripts/Battle" }), ["CardView", "도발"]);
  const result = buildMapExcerpt(mapFile, "Grep", { pattern: "Card" }, transcript);
  assert.equal(result.hits, 6);
  assert.ok(result.text.split("\n")[0].includes("LegacySynergyEffect"), "세션 빈발 Legacy가 동률 후보를 올린다");
  assert.ok(result.weighted);
  assert.ok(result.shown <= MAX_LINES);
  assert.ok(result.text.length <= MAX_CHARS);
  assert.equal(buildMapExcerpt(mapFile, "Grep", { pattern: "없는개념", path: "Assets/Scripts/Battle" }, transcript).hits, 0,
    "경로의 Battle을 검색어로 오인하지 않음");
  const weightedMap = path.join(root, "weighted-map.md");
  fs.writeFileSync(weightedMap, [
    "# 지도", "## 후보", ...Array.from({ length: 5 }, (_, index) =>
      `- 일반 ${index}: Card Card \`CardType${index}\``),
    "- 유산 후보: `LegacyEffect` · `CardOnlyOnce`",
  ].join("\n"));
  const unselectedWeight = buildMapExcerpt(weightedMap, "Grep", { pattern: "Card" }, transcript);
  assert.equal(unselectedWeight.shown, 5);
  assert.equal(unselectedWeight.weighted, false, "표시되지 않은 후보의 가중치는 마커에 반영하지 않음");
  // find 계열 회귀 — 이 규칙이 없던 동안 게이트 대상의 9.3%가 검색어 없이 통과했다.
  assert.deepEqual(searchTerms("Bash", { command: 'find Assets/Scripts -iname "*Undead*"' }), ["Undead"]);
  assert.deepEqual(searchTerms("Bash", { command: 'find Assets/Scripts -iname "HintArrow*" -o -iname "SwipeGuide*"' }),
    ["HintArrow", "SwipeGuide"]);
  // -path 는 경로 필터라 개념 검색어가 아니다(포함하면 Battle 이 지도 20줄에 매칭된다).
  assert.deepEqual(searchTerms("Bash", { command: 'find . -name "*.cs" -path "*Battle*"' }), []);
  // Unity_Grep 은 args 로 온다.
  assert.deepEqual(searchTerms("mcp__unity-mcp__Unity_Grep", { args: "-l CardView" }), ["CardView"]);
  // hits 0 의 두 원인이 구분돼야 한다.
  assert.equal(buildMapExcerpt(mapFile, "Bash", { command: 'find . -path "*X*"' }, null).reason, "no-terms");
  assert.equal(buildMapExcerpt(mapFile, "Grep", { pattern: "ZzzNope" }, null).reason, "no-map-match");
  // C# 키워드 회귀 — class/public 이 검색어로 새어 지도 33줄에 매칭되고 무관한 발췌가 실제로 나갔다.
  assert.deepEqual(searchTerms("Bash", { command: 'grep -n "class Card" Assets/Table/SpecDatas.cs' }), ["Card"]);
  assert.deepEqual(searchTerms("Bash", { command: 'grep -rn "public class" Assets/Scripts' }), []);
  assert.deepEqual(searchTerms("Grep", { pattern: "public static void Foo" }), ["Foo"]);
  // 실제 타입 이름은 살아남아야 한다.
  assert.deepEqual(searchTerms("Bash", { command: "grep -rn TurnRunner Assets/Scripts" }), ["TurnRunner"]);
  // too-broad 회귀 — 매칭되는 검색어가 전부 흔하면 발췌 대신 통과한다.
  {
    const broad = ["# map"].concat(Array.from({ length: 12 }, (_, i) => `- 구역 ${i}: \`Card${i}\` 관련`));
    const broadFile = path.join(root, "broad.md");
    fs.writeFileSync(broadFile, broad.join(String.fromCharCode(10)));
    const wide = buildMapExcerpt(broadFile, "Grep", { pattern: "Card" }, null);
    assert.equal(wide.reason, "too-broad", "흔한 검색어 하나만 남으면 통과");
    assert.equal(wide.shown, 0);
    assert.ok(wide.hits > 0, "hits 는 로그용으로 실제 값을 유지한다");
    // 좁은 검색어가 하나라도 있으면 발췌를 만든다.
    const narrow = buildMapExcerpt(broadFile, "Grep", { pattern: "Card|Card7" }, null);
    assert.equal(narrow.reason, "hit");
  }
  console.log("map-excerpt tests: query-first ranking, capped session weight, limits passed");
} finally {
  fs.rmSync(root, { recursive: true, force: true });
}
