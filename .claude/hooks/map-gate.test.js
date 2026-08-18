#!/usr/bin/env node
"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { processHook } = require("./map-gate.js");
const { bashSearch, powershellSearch, mapReadSucceeded } = require("../lib/search-detect.js");

const root = fs.mkdtempSync(path.join(os.tmpdir(), "map-gate-test-"));
const projectDir = path.join(root, "project");
const stateRoot = path.join(root, "state");
fs.mkdirSync(path.join(projectDir, ".claude"), { recursive: true });
fs.writeFileSync(path.join(projectDir, ".claude", "orch-feature-map.md"), "# map\n");

/* 실측 스키마 — Claude Code 의 Bash 결과에는 exitCode 도 is_error 도 없다.
   합성 객체로 테스트하면 실패 판정이 거짓 통과한다. 이 파일의 핵심 회귀점. */
const bashOk = (stdout) => ({ stdout, stderr: "", interrupted: false, isImage: false, noOutputExpected: false });
const bashFail = (message) => bashOk(message);
const mapBody = "# 기능 지도\n".padEnd(400, "전투 시너지 카드 멀티플레이 세이브 재화 보상 카드팩 컬렉션 덱 성장 랭크 튜토리얼 ");
const readOk = { file: { filePath: ".claude/orch-feature-map.md", content: mapBody, numLines: 40 } };

function hook(event, tool, input, response, session = "s1") {
  return processHook({
    hook_event_name: event,
    tool_name: tool,
    tool_input: input,
    tool_response: response,
    session_id: session,
    cwd: projectDir,
  }, { projectDir, stateRoot });
}

const denied = (result) => Boolean(result) && result.hookSpecificOutput.permissionDecision === "deny";

let passed = 0;
function check(label, fn) { fn(); passed += 1; console.log(`  ok  ${label}`); }

try {
  check("1. 지도 미열람 상태의 첫 Grep 은 차단된다", () => {
    assert.ok(denied(hook("PreToolUse", "Grep", { pattern: "Card" })));
  });

  check("2. 지도 Read 자체는 차단되지 않는다", () => {
    assert.equal(hook("PreToolUse", "Read", { file_path: ".claude/orch-feature-map.md" }), null);
  });

  check("3. Read 성공 기록 후 탐색이 열린다", () => {
    hook("PostToolUse", "Read", { file_path: ".claude/orch-feature-map.md" }, readOk);
    assert.equal(hook("PreToolUse", "Grep", { pattern: "Card" }), null);
  });

  check("4. Bash(cat) 로 읽어도 열린다 — 실측 열람 3회 중 2회가 이 경로", () => {
    hook("PostToolUse", "Bash", { command: "cat .claude/orch-feature-map.md" }, bashOk(mapBody), "bash-read");
    assert.equal(hook("PreToolUse", "Glob", { pattern: "**/*.cs" }, undefined, "bash-read"), null);
  });

  check("5. Bash 지도 읽기 실패는 기록되지 않는다 (exitCode 없는 실측 스키마)", () => {
    hook("PostToolUse", "Bash", { command: "cat .claude/orch-feature-map.md" },
      bashFail("cat: .claude/orch-feature-map.md: No such file or directory"), "bash-fail");
    assert.ok(denied(hook("PreToolUse", "Grep", { pattern: "Card" }, undefined, "bash-fail")));
  });

  check("6. 내용이 거의 없는 출력도 열람으로 치지 않는다", () => {
    hook("PostToolUse", "Bash", { command: "grep lobby .claude/orch-feature-map.md" }, bashOk(""), "empty-grep");
    assert.ok(denied(hook("PreToolUse", "Grep", { pattern: "Card" }, undefined, "empty-grep")));
  });

  check("7. Read 실패(is_error)도 기록되지 않는다", () => {
    hook("PostToolUse", "Read", { file_path: ".claude/orch-feature-map.md" }, { is_error: true }, "failed-read");
    assert.ok(denied(hook("PreToolUse", "Grep", { pattern: "Card" }, undefined, "failed-read")));
  });

  check("8. 빌드·테스트·git 은 막히지 않는다", () => {
    for (const [command, label] of [
      ["git status", "git"], ["npm test", "npm"], ["dotnet build", "dotnet"],
      ["Unity -batchmode -quit", "unity"], ["ls -la", "ls-la"], ["ls -lr", "ls-reverse"],
      ["sed -n 1,50p Assets/Scripts/Boot.cs", "sed"],
    ]) assert.equal(hook("PreToolUse", "Bash", { command }, undefined, label), null, `${command} 미차단`);
  });

  check("9. 연속 3회 차단 뒤 fail-open (교착 방지)", () => {
    for (let i = 0; i < 3; i += 1) {
      assert.ok(denied(hook("PreToolUse", "Grep", { pattern: "x" }, undefined, "cap")), `${i + 1}회차 차단`);
    }
    assert.equal(hook("PreToolUse", "Grep", { pattern: "x" }, undefined, "cap"), null);
  });

  check("10. Bash 탐색 변형이 모두 잡힌다", () => {
    for (const command of [
      "rg Card Assets/Scripts",
      "git status; rg Card Assets/Scripts",
      "ls -R Assets/Scripts",
      "git grep CardView",
      "find . -name '*.cs'",
      "cat foo | grep Card",
    ]) assert.ok(bashSearch(command), `${command} 은 탐색`);
    for (const command of ["ls -la", "ls -lr", "git status", "npm test"]) {
      assert.equal(bashSearch(command), false, `${command} 은 탐색 아님`);
    }
  });

  check("11. PowerShell 우회 경로가 막힌다", () => {
    for (const command of [
      "Select-String -Pattern CardView -Path Assets",
      "Get-ChildItem Assets -Recurse -Filter *.cs",
      "gci . -r",
    ]) assert.ok(powershellSearch(command), `${command} 은 탐색`);
    assert.equal(powershellSearch("Get-Content foo.txt"), false);
    assert.ok(denied(hook("PreToolUse", "PowerShell",
      { command: "Select-String -Pattern CardView -Path Assets" }, undefined, "ps")));
  });

  check("12. 지도 자신을 겨냥한 검색은 허용된다", () => {
    assert.equal(hook("PreToolUse", "Bash",
      { command: "rg lobby .claude/orch-feature-map.md" }, undefined, "map-search"), null);
  });

  check("13. 판정 유틸 단위 — 성공/실패 경계", () => {
    assert.equal(mapReadSucceeded(undefined), false);
    assert.equal(mapReadSucceeded(bashOk("short")), false);
    assert.equal(mapReadSucceeded(bashOk(mapBody)), true);
    assert.equal(mapReadSucceeded({ ...bashOk(mapBody), interrupted: true }), false);
    assert.equal(mapReadSucceeded(readOk), true);
  });

  console.log(`map-gate tests: ${passed}/13 passed (subagent context: 미검증 — sidechain 기록 없음)`);
} finally {
  fs.rmSync(root, { recursive: true, force: true });
}
