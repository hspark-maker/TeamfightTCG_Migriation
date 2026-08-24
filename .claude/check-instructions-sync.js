#!/usr/bin/env node
"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { END, MAX_PROJECT_DOC_BYTES, assertAgentsMapSync } = require("./sync-agents-map.js");

const root = path.resolve(__dirname, "..");
const agents = fs.readFileSync(path.join(root, "AGENTS.md"), "utf8").replace(/\r\n/g, "\n");
const claude = fs.readFileSync(path.join(root, "CLAUDE.md"), "utf8").replace(/\r\n/g, "\n");
const MAP_HEADING = "## 기능 지도 — 상주 컨텍스트";
const GENERATED_NOTE = "<!-- 기능 지도 생성 블록은 직접 수정하지 말고 node .claude/sync-agents-map.js 를 실행한다. -->\n";

function normalizedCommonPrefix(text) {
  return text.slice(0, text.indexOf(MAP_HEADING))
    .replace(GENERATED_NOTE, "")
    .split("\n")
    .filter((line) => !line.includes("`outgame-engineer`"))
    .join("\n")
    .trim();
}

function normalizedTail(text, marker) {
  return text.slice(text.indexOf(marker) + marker.length)
    .replace(/`(Glob|rg --files)`/g, "$1")
    .replace(/rg --files\s*로/g, "rg --files 로")
    .trim();
}

function checkInstructionsSync() {
  assert.ok(agents.includes(MAP_HEADING), "AGENTS.md 지도 절 제목이 다릅니다");
  assert.ok(claude.includes(`${MAP_HEADING}\n\n자체 코드가`), "CLAUDE.md 지도 절 제목이 다릅니다");
  assert.ok(claude.includes("@.claude/orch-feature-map.md"), "CLAUDE.md 지도 import가 없습니다");
  assert.doesNotMatch(agents, /orch-feature-map\.md` 를 먼저 Read/, "AGENTS.md에 옛 지도 Read 지시가 남았습니다");
  assert.equal(normalizedCommonPrefix(agents), normalizedCommonPrefix(claude), "지도 밖 공통 지침이 어긋났습니다");
  assert.equal(normalizedTail(agents, END), normalizedTail(claude, "@.claude/orch-feature-map.md"), "지도 뒤 공통 지침이 어긋났습니다");
  assert.ok(Buffer.byteLength(agents) <= MAX_PROJECT_DOC_BYTES, `AGENTS.md가 Codex 기본 project_doc_max_bytes(${MAX_PROJECT_DOC_BYTES})를 넘었습니다`);
  assertAgentsMapSync();
  console.log(`instruction sync ok: AGENTS ${Buffer.byteLength(agents)} bytes`);
}

if (require.main === module) checkInstructionsSync();

module.exports = { checkInstructionsSync };
