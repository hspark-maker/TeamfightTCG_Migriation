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
    content: [{ type: "tool_use", name, input }],
  },
});

assert.equal(isRealUser(user("<task-notification>done</task-notification>", "2026-08-15T00:00:00Z")), false);
assert.equal(isRealUser(user("<system-reminder>note</system-reminder>", "2026-08-15T00:00:00Z")), false);
assert.equal(isRealUser(user("<current_user_request>진짜 요청</current_user_request>", "2026-08-15T00:00:00Z")), true);

const file = path.join(fs.mkdtempSync(path.join(os.tmpdir(), "map-report-test-")), "session.jsonl");
try {
  const events = [
    user("첫 요청", "2026-08-15T00:00:00Z"),
    tool("Read", { file_path: ".claude/orch-feature-map.md" }, "r1"),
    user("<system-reminder>경계를 만들면 안 됨</system-reminder>", "2026-08-15T00:00:01Z"),
    tool("Grep", { pattern: "Card" }, "r2"),
    user("둘째 요청", "2026-08-15T00:01:00Z"),
    tool("Glob", { pattern: "**/*.cs" }, "r3"),
  ];
  fs.writeFileSync(file, events.map((event) => JSON.stringify(event)).join("\n"));
  const samples = parseTranscript(file, Date.parse("2026-08-14T06:18:00Z"));
  assert.equal(samples.length, 2);
  assert.equal(samples[0].group, "선로드(세그내)");
  assert.equal(samples[0].inputTokens, 60, "서로 다른 requestId usage 합계");
  assert.equal(samples[1].group, "선로드(이월)");
  assert.equal(samples[1].searchesBeforeMap, 0);
  console.log("map-savings tests: noise filter + map carry passed");
} finally {
  fs.rmSync(path.dirname(file), { recursive: true, force: true });
}
