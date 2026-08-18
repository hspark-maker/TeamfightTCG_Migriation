#!/usr/bin/env node
"use strict";

/**
 * 지도 응답력 A/B — 과거 세션의 **첫 검색**(게이트가 차단할 바로 그 호출)을 지도가 받아낼 수 있나.
 *
 *   node .claude/report-map-answerability.js [비교할지도.md ...]
 *
 * 인자를 주면 현재 지도와 나란히 비교한다. 같은 질문 집합에 지도만 바꿔 재는 A/B다.
 * "지도에 그 단어가 있나"까지만 본다 — 답이 정확한지는 재지 못한다. 상한값으로 읽어라.
 */
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { isSearchTool } = require("./lib/search-detect.js");

const root = path.resolve(__dirname, "..");
/* 한글 기획 용어는 2글자가 많다(성벽·무리·도발). 라틴은 4글자 미만이면 잡음이라 컷을 나눈다. */
const LATIN = /[a-z][a-z0-9_]{3,}/g;
const HANGUL = /[가-힣]{2,}/g;
const STOP = new Set([
  "assets", "scripts", "name", "grep", "find", "include", "head", "type", "null", "true", "false",
  "echo", "print", "path", "file", "files", "iname", "case", "sort", "uniq", "tail", "while", "done",
  "then", "else", "users", "cookapps", "teamfighttcg", "migriation", "claude", "text", "line", "with",
]);

function transcripts(projectDir) {
  const projectsDir = path.join(os.homedir(), ".claude", "projects");
  const needle = path.basename(projectDir).replace(/_/g, "-").toLowerCase();
  return fs.readdirSync(projectsDir, { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && entry.name.toLowerCase().includes(needle))
    .flatMap((entry) => {
      const dir = path.join(projectsDir, entry.name);
      return fs.readdirSync(dir).filter((f) => f.endsWith(".jsonl")).map((f) => path.join(dir, f));
    });
}

function firstSearch(file) {
  for (const line of fs.readFileSync(file, "utf8").split(/\r?\n/)) {
    if (!line.trim()) continue;
    let event;
    try { event = JSON.parse(line); } catch { continue; }
    if (event.type !== "assistant" || !Array.isArray(event.message?.content)) continue;
    for (const part of event.message.content) {
      if (part.type === "tool_use" && isSearchTool(part.name, part.input || {})) return part.input || {};
    }
  }
  return null;
}

function terms(input) {
  const raw = String(input.pattern || input.command || input.file_pattern || "").toLowerCase();
  const latin = (raw.match(LATIN) || []).filter((t) => !STOP.has(t));
  return [...new Set([...latin, ...(raw.match(HANGUL) || [])])];
}

const questions = transcripts(root).map(firstSearch).filter(Boolean).map((input) => ({
  input, terms: terms(input),
})).filter((q) => q.terms.length);

const candidates = [path.join(__dirname, "orch-feature-map.md"), ...process.argv.slice(2)];

console.log(`첫 검색 표본 ${questions.length}개 (검색어를 뽑을 수 있는 것만)\n`);
console.log("지도                              bytes   추정토큰   응답가능   비율");
const results = [];
for (const file of candidates) {
  const text = fs.readFileSync(file, "utf8").toLowerCase();
  const answered = questions.filter((q) => q.terms.some((t) => text.includes(t)));
  const bytes = Buffer.byteLength(text, "utf8");
  // 실측 환산: 10,527 bytes ≈ 2,619 토큰 (한글 위주 마크다운)
  const tokens = Math.round(bytes * 0.2488);
  results.push({ file, answered: answered.length, tokens });
  console.log(
    path.basename(file).padEnd(30) +
    String(bytes).padStart(7) +
    String(tokens).padStart(10) +
    String(answered.length).padStart(10) +
    `   ${(100 * answered.length / questions.length).toFixed(0)}%`
  );
}

if (results.length > 1) {
  const [now, ...rest] = results;
  for (const other of rest) {
    const gained = now.answered - other.answered;
    const cost = now.tokens - other.tokens;
    console.log(`\n${path.basename(other.file)} 대비: 응답 +${gained}건, 토큰 +${cost}`);
    if (gained > 0) console.log(`  질문 1건 더 받는 데 ${Math.round(cost / gained)} 토큰`);
  }
}

const stillMissing = questions.filter((q) => {
  const text = fs.readFileSync(candidates[0], "utf8").toLowerCase();
  return !q.terms.some((t) => text.includes(t));
});
console.log(`\n현재 지도가 못 받는 ${stillMissing.length}건:`);
for (const q of stillMissing.slice(0, 15)) {
  console.log("  - " + String(q.input.pattern || q.input.command).replace(/\s+/g, " ").slice(0, 76));
}
