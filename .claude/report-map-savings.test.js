#!/usr/bin/env node
"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { isRealUser, parseTranscript } = require("./report-map-savings.js");

const user = (content, timestamp) => ({
  type: "user", timestamp, sessionId: "test-session",
  message: { role: "user", content },
});
const tool = (name, input, requestId) => ({
  type: "assistant", requestId, isSidechain: false,
  message: {
    usage: { input_tokens: 10, cache_read_input_tokens: 20, output_tokens: 5 },
    content: [{ type: "tool_use", id: `${requestId}-tool`, name, input }],
  },
});
const toolResult = (content, toolUseId) => ({
  type: "user", message: { role: "user", content: [{ type: "tool_result", tool_use_id: toolUseId, content }] },
});

assert.equal(isRealUser(user("<task-notification>done</task-notification>", "2026-08-15T00:00:00Z")), false);
assert.equal(isRealUser(user("<system-reminder>note</system-reminder>", "2026-08-15T00:00:00Z")), false);
assert.equal(isRealUser(user("<current_user_request>진짜 요청</current_user_request>", "2026-08-15T00:00:00Z")), true);

const file = path.join(fs.mkdtempSync(path.join(os.tmpdir(), "map-report-test-")), "session.jsonl");
try {
  const events = [
    user("첫 요청", "2026-08-15T00:00:00Z"),
    tool("Read", { file_path: ".claude/orch-feature-map.md" }, "r1"),
    toolResult("# map\n".padEnd(300, "지도"), "r1-tool"),
    user("<system-reminder>경계를 만들면 안 됨</system-reminder>", "2026-08-15T00:00:01Z"),
    tool("Grep", { pattern: "Card" }, "r2"),
    user("둘째 요청", "2026-08-15T00:01:00Z"),
    tool("Glob", { pattern: "**/*.cs" }, "r3"),
  ];
  fs.writeFileSync(file, events.map((event) => JSON.stringify(event)).join("\n"));
  const GATE_AT = Date.parse("2026-08-18T06:40:00Z");
  const samples = parseTranscript(file, Date.parse("2026-08-14T06:18:00Z"), GATE_AT);
  assert.equal(samples.length, 2);
  assert.equal(samples[0].group, "게이트전·선로드(세그내)");
  assert.equal(samples[0].inputTokens, 60, "서로 다른 requestId usage 합계");
  assert.equal(samples[1].group, "게이트전·선로드(이월)");
  assert.equal(samples[1].searchesBeforeMap, 0);
  // 게이트 이후 표본은 다른 그룹으로 갈라져야 한다 — 개입이 섞이면 비교가 무의미해진다.
  const postGate = [user("게이트 이후 요청", "2026-08-18T08:00:00Z"), tool("Grep", { pattern: "Card" }, "r9")];
  fs.writeFileSync(file, postGate.map((event) => JSON.stringify(event)).join(String.fromCharCode(10)));
  const after = parseTranscript(file, Date.parse("2026-08-14T06:18:00Z"), GATE_AT);
  assert.equal(after[0].group, "게이트후·미로드", "게이트 이후는 별도 그룹");
  const excerptEvents = [
    user("발췌 요청", "2026-08-18T08:10:00Z"),
    tool("Grep", { pattern: "Card" }, "r10"),
    toolResult("[MAP_GATE_EXCERPT_V1 hits=3 shown=3 weighted=true]\n지도에서 찾은 줄"),
    tool("Grep", { pattern: "CardView" }, "r11"),
    tool("Read", { file_path: ".claude/orch-feature-map.md" }, "r12"),
    toolResult("# map\n".padEnd(300, "지도"), "r12-tool"),
    tool("Read", { file_path: ".claude/orch-feature-map.md" }, "r13"),
    toolResult("No such file or directory", "r13-tool"),
  ];
  fs.writeFileSync(file, excerptEvents.map((event) => JSON.stringify(event)).join(String.fromCharCode(10)));
  const excerpt = parseTranscript(file, Date.parse("2026-08-14T06:18:00Z"), GATE_AT);
  assert.equal(excerpt[0].group, "게이트후·늦게 로드", "발췌는 현재 요청의 지도 로드로 집계");
  assert.equal(excerpt[0].excerpts, 1);
  assert.equal(excerpt[0].mapReads, 1, "성공한 지도 열람만 집계");
  assert.equal(excerpt[0].mapReadsAfterExcerpt, 1, "발췌 이후 성공한 열람을 별도 집계");
  assert.equal(excerpt[0].searchesBeforeMap, 1, "발췌 뒤 검색은 로드 전 검색으로 세지 않음");
  console.log("map-savings tests: noise filter + map carry + gate era passed");
} finally {
  fs.rmSync(path.dirname(file), { recursive: true, force: true });
}
