#!/usr/bin/env node
"use strict";

const crypto = require("node:crypto");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

const {
  MAP_NAME,
  MAP_SCOPE_OUTSIDE_MARKER,
  isOutsideMapScope,
  isMapReadTool,
  shouldGate,
  mapReadSucceeded,
  mentionsMap,
} = require("../lib/search-detect.js");
const { lastRequestKey } = require("../lib/transcript.js");
const { buildMapExcerpt } = require("../lib/map-excerpt.js");

/* 요청 하나당 최대 차단 수. 요청이 바뀌면 다시 0 부터다. */
const MAX_BLOCKS = 3;
const STATE_MAX_AGE_MS = 24 * 60 * 60 * 1000;
const CLEANUP_INTERVAL_MS = 24 * 60 * 60 * 1000;
const CLEANUP_MARKER = ".last-cleanup";

/* 요청당 그냥 통과시킬 탐색 횟수. 실측: 탐색형 요청의 49%가 검색 1~2회로 끝난다 —
   그런 요청까지 막으면 지도 열람이 순수 오버헤드다. 반면 검색 8회 이상인 13%가 전체 검색의 39%를
   차지하므로, 세 번째 검색에서 막으면 헤매는 쪽만 걸린다. */
const FREE_SEARCHES = Number(process.env.MAP_GATE_FREE_SEARCHES ?? 2);

/* 저장소 루트 찾기.
   input.cwd 는 모델이 `cd` 하면 하위 디렉터리가 된다 — 실측 로그에 
   `.../Assets/.claude/orch-feature-map.md` 를 찾다 실패해 게이트가 조용히 꺼진 기록이 있다.
   그래서 CLAUDE_PROJECT_DIR 을 먼저 보고, 없으면 지도가 나올 때까지 위로 올라간다. */
function resolveProjectDir(start) {
  let dir = start ? path.resolve(start) : null;
  while (dir) {
    if (fs.existsSync(path.join(dir, ".claude", MAP_NAME))) return dir;
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  return null;
}

function statePath(stateRoot, projectDir, sessionId) {
  const key = crypto.createHash("sha256").update(`${path.resolve(projectDir)}\0${sessionId}`).digest("hex").slice(0, 32);
  return path.join(stateRoot, `${key}.json`);
}

function readState(file) {
  try { return JSON.parse(fs.readFileSync(file, "utf8")); } catch { return { requestKey: null, loaded: false, searches: 0, blocks: 0 }; }
}

function writeState(file, state) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, JSON.stringify(state));
}

function logFailOpen(stateRoot, message) {
  try {
    fs.mkdirSync(stateRoot, { recursive: true });
    fs.appendFileSync(path.join(stateRoot, "map-gate.log"), `${new Date().toISOString()} ${message}\n`);
  } catch { /* fail-open logging must never block work */ }
}

function pruneStateFiles(stateRoot, now = Date.now()) {
  fs.mkdirSync(stateRoot, { recursive: true });
  const marker = path.join(stateRoot, CLEANUP_MARKER);
  try {
    if (now - fs.statSync(marker).mtimeMs < CLEANUP_INTERVAL_MS) return 0;
  } catch { /* 첫 실행 또는 오래된 marker */ }

  let removed = 0;
  for (const entry of fs.readdirSync(stateRoot, { withFileTypes: true })) {
    if (!entry.isFile() || !entry.name.endsWith(".json")) continue;
    const file = path.join(stateRoot, entry.name);
    try {
      if (now - fs.statSync(file).mtimeMs > STATE_MAX_AGE_MS) {
        fs.unlinkSync(file);
        removed += 1;
      }
    } catch { /* 다른 hook 프로세스와 경합하면 다음 정리에서 처리 */ }
  }
  fs.writeFileSync(marker, new Date(now).toISOString());
  return removed;
}

function deny(excerpt) {
  const marker = `[MAP_GATE_EXCERPT_V1 hits=${excerpt.hits} shown=${excerpt.shown} weighted=${excerpt.weighted}]`;
  return {
    hookSpecificOutput: {
      hookEventName: "PreToolUse",
      permissionDecision: "deny",
      permissionDecisionReason: `${marker}\n지도에서 찾은 줄:\n${excerpt.text}\n여기서 해결되면 바로 진행하고, 아니면 검색을 계속하세요.`,
    },
  };
}

function processHook(input, options = {}) {
  const stateRoot = options.stateRoot || path.join(os.tmpdir(), "orch-map-gate");
  try { pruneStateFiles(stateRoot); }
  catch (error) { logFailOpen(stateRoot, `cleanup failed (${error.message})`); }
  const projectDir = options.projectDir
    || resolveProjectDir(process.env.CLAUDE_PROJECT_DIR)
    || resolveProjectDir(input.cwd)
    || process.env.CLAUDE_PROJECT_DIR
    || input.cwd;
  const sessionId = input.session_id || process.env.CLAUDE_CODE_SESSION_ID;
  if (!projectDir || !sessionId) {
    logFailOpen(stateRoot, "fail-open: project or session identifier missing");
    return null;
  }

  const mapFile = path.join(projectDir, ".claude", MAP_NAME);
  if (!fs.existsSync(mapFile)) {
    logFailOpen(stateRoot, `fail-open: map missing (${mapFile})`);
    return null;
  }

  const file = statePath(stateRoot, projectDir, sessionId);
  const state = readState(file);
  /* 해제 범위는 세션이 아니라 요청이다. 한 세션 안에서도 주제가 바뀌면 지도를 다시 봐야 한다.
     실측: 첫 요청에서 해제된 세션의 세 번째 요청이 지도 없이 검색을 25회 돌았다. */
  const requestKey = lastRequestKey(input.transcript_path);
  if (requestKey && state.requestKey !== requestKey) {
    state.requestKey = requestKey;
    state.loaded = false;
    state.blocks = 0;
    state.searches = 0;
  }
  const event = input.hook_event_name;
  const toolName = input.tool_name;
  const toolInput = input.tool_input || {};

  if (event === "PostToolUse") {
    // 요청 경계 갱신만 하고 끝나는 경우에도 상태를 남겨야 다음 PreToolUse 가 같은 요청으로 본다.
    if (isMapReadTool(toolName) && mentionsMap(toolInput) && mapReadSucceeded(input.tool_response)) {
      try { writeState(file, { requestKey: state.requestKey || null, loaded: true, searches: state.searches || 0, blocks: state.blocks || 0 }); }
      catch { logFailOpen(stateRoot, "fail-open: could not record successful map read"); }
    }
    return null;
  }

  if (event !== "PreToolUse" || state.loaded || !shouldGate(toolName, toolInput)) return null;
  if (isOutsideMapScope(toolInput)) {
    logFailOpen(stateRoot, `observe: ${MAP_SCOPE_OUTSIDE_MARKER} (${toolName}) ${JSON.stringify(toolInput).slice(0, 140)}`);
  }

  const searches = (state.searches || 0) + 1;
  if (searches <= FREE_SEARCHES) {
    try { writeState(file, { ...state, searches }); } catch { /* 통과 경로는 실패해도 막지 않는다 */ }
    return null;
  }
  if ((state.blocks || 0) >= MAX_BLOCKS) {
    logFailOpen(stateRoot, `fail-open: ${MAX_BLOCKS} consecutive blocks (${sessionId})`);
    return null;
  }

  let excerpt;
  try { excerpt = buildMapExcerpt(mapFile, toolName, toolInput, input.transcript_path); }
  catch (error) {
    logFailOpen(stateRoot, `fail-open: map excerpt failed (${error.message})`);
    return null;
  }
  // 현재 검색어가 지도 본문에 없으면 지도 열람을 강제해도 이득이 없다.
  // too-broad = 지도가 좁히지 못했다. 0건과 같이 통과시킨다(엉뚱한 5줄은 지도 없는 것보다 나쁘다).
  if (!excerpt.hits || excerpt.reason === "too-broad") {
    /* 통과 사유를 구분해 남긴다. no-terms 는 "지도에 없다"가 아니라 **추출기가 검색어를 못 뽑았다**는
       뜻이라 게이트 구멍의 신호다 — 로그가 없으면 추출기 버그가 조용한 무력화로 번역된다. */
    if (excerpt.reason === "no-terms" || excerpt.reason === "too-broad") {
      logFailOpen(stateRoot, `pass: ${excerpt.reason} narrowest=${excerpt.narrowest ?? "-"} hits=${excerpt.hits} (${toolName}) ${JSON.stringify(toolInput).slice(0, 140)}`);
    }
    try { writeState(file, { ...state, searches }); } catch { /* 통과 경로는 실패해도 막지 않는다 */ }
    return null;
  }

  // 발췌가 지도 열람을 대신한다. 다음 재시도는 바로 통과시켜 별도 Read 왕복을 없앤다.
  try { writeState(file, { requestKey: state.requestKey || null, loaded: true, searches, blocks: (state.blocks || 0) + 1 }); }
  catch {
    logFailOpen(stateRoot, "fail-open: could not persist block count");
    return null;
  }
  return deny(excerpt);
}

function main() {
  let input = "";
  process.stdin.setEncoding("utf8");
  process.stdin.on("data", (chunk) => { input += chunk; });
  process.stdin.on("end", () => {
    try {
      // BOM 이 붙어 오면 JSON.parse 가 죽고 게이트가 조용히 꺼진다(실측 로그로 확인).
      const output = processHook(JSON.parse(input.replace(/^﻿/, "").trim()));
      if (output) process.stdout.write(JSON.stringify(output));
    } catch (error) {
      logFailOpen(path.join(os.tmpdir(), "orch-map-gate"), `fail-open: hook exception (${error.message})`);
    }
  });
}

if (require.main === module) main();

module.exports = {
  CLEANUP_INTERVAL_MS, FREE_SEARCHES, MAX_BLOCKS, STATE_MAX_AGE_MS,
  mapReadSucceeded, mentionsMap, processHook, pruneStateFiles, shouldGate, statePath,
};
