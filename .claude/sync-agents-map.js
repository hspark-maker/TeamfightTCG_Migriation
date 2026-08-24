#!/usr/bin/env node
"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "..");
const mapFile = path.join(__dirname, "orch-feature-map.md");
const agentsFile = path.join(root, "AGENTS.md");
const backupFile = path.join(__dirname, "agents-map-inline.bak");
const START = "<!-- orch:map-inline:start -->";
const END = "<!-- orch:map-inline:end -->";
const MAX_PROJECT_DOC_BYTES = 32768;
const BLOCK_RE = /<!-- orch:map-inline:start -->[\s\S]*?<!-- orch:map-inline:end -->/;

function normalized(text) {
  return text.replace(/\r\n/g, "\n").replace(/\r/g, "").trimEnd();
}

function inlineBody(agents) {
  assert.equal(agents.split(START).length - 1, 1, "AGENTS.md 지도 시작 마커는 정확히 하나여야 합니다");
  assert.equal(agents.split(END).length - 1, 1, "AGENTS.md 지도 끝 마커는 정확히 하나여야 합니다");
  const match = agents.match(BLOCK_RE);
  assert.ok(match, "AGENTS.md에 지도 생성 마커가 없습니다");
  return match[0].slice(START.length, -END.length).trim();
}

function assertAgentsMapSync() {
  const map = fs.readFileSync(mapFile, "utf8");
  const agents = fs.readFileSync(agentsFile, "utf8");
  assert.ok(!map.includes(START) && !map.includes(END), "기능 지도 진실원에 AGENTS 생성 마커가 들어갔습니다");
  assert.equal(normalized(inlineBody(agents)), normalized(map), "AGENTS.md 인라인 지도가 진실원과 다릅니다: node .claude/sync-agents-map.js 실행 필요");
}

function atomicWrite(file, content) {
  const temp = file + `.tmp-${process.pid}-${Date.now()}`;
  try {
    fs.writeFileSync(temp, content, "utf8");
    fs.renameSync(temp, file);
  } finally {
    try { fs.unlinkSync(temp); } catch { /* rename 성공 또는 best-effort cleanup */ }
  }
}

function syncAgentsMap() {
  const original = fs.readFileSync(agentsFile, "utf8");
  inlineBody(original);
  const eol = original.includes("\r\n") ? "\r\n" : "\n";
  const mapSource = fs.readFileSync(mapFile, "utf8");
  assert.ok(!mapSource.includes(START) && !mapSource.includes(END), "기능 지도 진실원에 AGENTS 생성 마커가 들어갔습니다");
  const map = normalized(mapSource).replace(/\n/g, eol);
  const block = `${START}${eol}${map}${eol}${END}`;
  const next = normalized(original.replace(BLOCK_RE, block)).replace(/\n/g, eol) + eol;
  assert.ok(Buffer.byteLength(next) <= MAX_PROJECT_DOC_BYTES, `AGENTS.md가 Codex 기본 project_doc_max_bytes(${MAX_PROJECT_DOC_BYTES})를 넘습니다`);
  if (next === original) {
    console.log("AGENTS map inline unchanged");
    return;
  }
  atomicWrite(backupFile, original);
  atomicWrite(agentsFile, next);
  console.log(`AGENTS map inline synced: ${Buffer.byteLength(map)} bytes`);
}

if (require.main === module) {
  try { syncAgentsMap(); }
  catch (error) { console.error(String(error && error.message || error)); process.exitCode = 1; }
}

module.exports = { END, MAX_PROJECT_DOC_BYTES, START, assertAgentsMapSync, syncAgentsMap };
