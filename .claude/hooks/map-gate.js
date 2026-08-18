#!/usr/bin/env node
"use strict";

const crypto = require("node:crypto");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

const {
  MAP_NAME,
  isMapReadTool,
  isSearchTool,
  mapReadSucceeded,
  mentionsMap,
} = require("../lib/search-detect.js");

const MAX_BLOCKS = 3;

function statePath(stateRoot, projectDir, sessionId) {
  const key = crypto.createHash("sha256").update(`${path.resolve(projectDir)}\0${sessionId}`).digest("hex").slice(0, 32);
  return path.join(stateRoot, `${key}.json`);
}

function readState(file) {
  try { return JSON.parse(fs.readFileSync(file, "utf8")); } catch { return { loaded: false, blocks: 0 }; }
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

function deny() {
  return {
    hookSpecificOutput: {
      hookEventName: "PreToolUse",
      permissionDecision: "deny",
      permissionDecisionReason: "위치 탐색 전에 .claude/orch-feature-map.md를 Read하세요. 지도가 해당 영역을 다루지 않으면 이후 검색을 계속하세요.",
    },
  };
}

function processHook(input, options = {}) {
  const stateRoot = options.stateRoot || path.join(os.tmpdir(), "orch-map-gate");
  const projectDir = options.projectDir || input.cwd || process.env.CLAUDE_PROJECT_DIR;
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
  const event = input.hook_event_name;
  const toolName = input.tool_name;
  const toolInput = input.tool_input || {};

  if (event === "PostToolUse") {
    if (isMapReadTool(toolName) && mentionsMap(toolInput) && mapReadSucceeded(input.tool_response)) {
      try { writeState(file, { loaded: true, blocks: state.blocks || 0 }); }
      catch { logFailOpen(stateRoot, "fail-open: could not record successful map read"); }
    }
    return null;
  }

  if (event !== "PreToolUse" || state.loaded || !isSearchTool(toolName, toolInput)) return null;
  if ((state.blocks || 0) >= MAX_BLOCKS) {
    logFailOpen(stateRoot, `fail-open: ${MAX_BLOCKS} consecutive blocks (${sessionId})`);
    return null;
  }

  try { writeState(file, { loaded: false, blocks: (state.blocks || 0) + 1 }); }
  catch {
    logFailOpen(stateRoot, "fail-open: could not persist block count");
    return null;
  }
  return deny();
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

module.exports = { MAX_BLOCKS, isSearchTool, mapReadSucceeded, mentionsMap, processHook, statePath };
