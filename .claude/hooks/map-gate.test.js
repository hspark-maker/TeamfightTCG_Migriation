#!/usr/bin/env node
"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { processHook, FREE_SEARCHES, pruneStateFiles, shouldGate } = require("./map-gate.js");
const { MAP_SCOPE_OUTSIDE_MARKER, bashSearch, isOutsideMapScope, powershellSearch, mapReadSucceeded } = require("../lib/search-detect.js");

const root = fs.mkdtempSync(path.join(os.tmpdir(), "map-gate-test-"));
const projectDir = path.join(root, "project");
const stateRoot = path.join(root, "state");
fs.mkdirSync(path.join(projectDir, ".claude"), { recursive: true });

/* 실측 스키마 — Claude Code 의 Bash 결과에는 exitCode 도 is_error 도 없다.
   합성 객체로 테스트하면 실패 판정이 거짓 통과한다. 이 파일의 핵심 회귀점. */
const bashOk = (stdout) => ({ stdout, stderr: "", interrupted: false, isImage: false, noOutputExpected: false });
const bashFail = (message) => bashOk(message);
const mapBody = [
  "# 기능 지도", "## 전투 (`Battle/`)",
  "- 카드 표시: `CardView` · `CardDecorView`", "- 덱: `DeckBuilderUI`", "- 시너지: `LegacySynergyEffect`",
].join("\n").padEnd(400, " 전투 시너지 카드 멀티플레이 세이브 재화 보상 카드팩 컬렉션 덱 성장 랭크 튜토리얼");
fs.writeFileSync(path.join(projectDir, ".claude", "orch-feature-map.md"), mapBody);
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

/* 요청당 무료 탐색을 다 쓴다. 게이트는 세 번째 검색부터 막으므로 테스트도 같은 전제를 밟아야 한다. */
function exhaustFree(session = "s1", extra = {}) {
  for (let i = 0; i < FREE_SEARCHES; i += 1) {
    const result = processHook({
      hook_event_name: "PreToolUse", tool_name: "Grep", tool_input: { pattern: `warmup${i}` },
      session_id: session, cwd: projectDir, ...extra,
    }, { projectDir, stateRoot });
    assert.equal(result, null, "무료 구간은 통과해야 한다");
  }
}

let passed = 0;
function check(label, fn) { fn(); passed += 1; console.log(`  ok  ${label}`); }

try {
  check("1. 무료 구간을 넘긴 Grep 은 차단된다", () => {
    exhaustFree();
    const result = hook("PreToolUse", "Grep", { pattern: "Card" });
    assert.ok(denied(result));
    assert.match(result.hookSpecificOutput.permissionDecisionReason, /\[MAP_GATE_EXCERPT_V1 hits=\d+/);
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
    exhaustFree("bash-fail");
    hook("PostToolUse", "Bash", { command: "cat .claude/orch-feature-map.md" },
      bashFail("cat: .claude/orch-feature-map.md: No such file or directory"), "bash-fail");
    assert.ok(denied(hook("PreToolUse", "Grep", { pattern: "Card" }, undefined, "bash-fail")));
  });

  check("6. 내용이 거의 없는 출력도 열람으로 치지 않는다", () => {
    exhaustFree("empty-grep");
    hook("PostToolUse", "Bash", { command: "grep lobby .claude/orch-feature-map.md" }, bashOk(""), "empty-grep");
    assert.ok(denied(hook("PreToolUse", "Grep", { pattern: "Card" }, undefined, "empty-grep")));
  });

  check("7. Read 실패(is_error)도 기록되지 않는다", () => {
    exhaustFree("failed-read");
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

  check("9. 지도 0건은 통과하고, 발췌를 받은 요청은 재시도가 열린다", () => {
    exhaustFree("excerpt");
    assert.equal(hook("PreToolUse", "Grep", { pattern: "UnfindableConcept" }, undefined, "excerpt"), null);
    assert.ok(denied(hook("PreToolUse", "Grep", { pattern: "CardView" }, undefined, "excerpt")));
    assert.equal(hook("PreToolUse", "Grep", { pattern: "CardView" }, undefined, "excerpt"), null);
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
    exhaustFree("ps");
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

  check("14. 요청이 바뀌면 해제가 풀린다 — 세션 단위 해제의 구멍", () => {
    const transcript = path.join(root, "session.jsonl");
    const user = (uuid, text) => JSON.stringify({ type: "user", uuid, message: { role: "user", content: text } });
    const call = (event, tool, input, response) => processHook({
      hook_event_name: event, tool_name: tool, tool_input: input, tool_response: response,
      session_id: "scoped", cwd: projectDir, transcript_path: transcript,
    }, { projectDir, stateRoot });

    fs.writeFileSync(transcript, user("req-1", "첫 요청"));
    exhaustFree("scoped", { transcript_path: transcript });
    assert.ok(denied(call("PreToolUse", "Grep", { pattern: "Card" })), "요청1 무료 구간 뒤 차단");
    call("PostToolUse", "Read", { file_path: ".claude/orch-feature-map.md" }, readOk);
    assert.equal(call("PreToolUse", "Grep", { pattern: "Card" }), null, "요청1 해제");

    fs.appendFileSync(transcript, String.fromCharCode(10) + user("req-2", "다른 주제 요청"));
    exhaustFree("scoped", { transcript_path: transcript });
    assert.ok(denied(call("PreToolUse", "Grep", { pattern: "Deck" })), "요청2 는 다시 차단");
    call("PostToolUse", "Bash", { command: "cat .claude/orch-feature-map.md" }, bashOk(mapBody));
    assert.equal(call("PreToolUse", "Glob", { pattern: "**/*.cs" }), null, "요청2 해제");
  });

  check("15. 주입 메시지는 요청 경계가 아니다", () => {
    const transcript = path.join(root, "noise.jsonl");
    const line = (uuid, text) => JSON.stringify({ type: "user", uuid, message: { role: "user", content: text } });
    const call = (event, tool, input, response) => processHook({
      hook_event_name: event, tool_name: tool, tool_input: input, tool_response: response,
      session_id: "noise", cwd: projectDir, transcript_path: transcript,
    }, { projectDir, stateRoot });

    fs.writeFileSync(transcript, line("r-1", "진짜 요청"));
    exhaustFree("noise", { transcript_path: transcript });
    assert.ok(denied(call("PreToolUse", "Grep", { pattern: "Card" })));
    call("PostToolUse", "Read", { file_path: ".claude/orch-feature-map.md" }, readOk);
    fs.appendFileSync(transcript, String.fromCharCode(10) + line("r-2", "<system-reminder>주입</system-reminder>"));
    assert.equal(call("PreToolUse", "Grep", { pattern: "Card" }), null, "주입은 경계가 아니므로 계속 열려 있어야 한다");
  });

  check("16. 요청당 무료 탐색은 막지 않는다 — 검색 1~2회로 끝나는 49% 를 위한 여유", () => {
    for (let i = 0; i < FREE_SEARCHES; i += 1) {
      assert.equal(hook("PreToolUse", "Grep", { pattern: `free${i}` }, undefined, "free"), null);
    }
    assert.ok(denied(hook("PreToolUse", "Grep", { pattern: "CardView" }, undefined, "free")), "무료 구간 초과분은 지도 매칭 시 차단");
  });

  check("17. 지도가 못 받는 대상(프리팹·SO·guid·git)은 막지 않는다", () => {
    const exempt = [
      { command: "grep -n m_Name Assets/Assets/Prefabs/UI/CardView.prefab" },
      { command: "grep -rn displayName Assets/SO/Synergies" },
      { command: "grep -rl 4c4b3cb345915fd48ad7bfcc494749d9 Assets" },
      { command: "git --no-pager diff --stat -- Assets | grep -i synergy" },
    ];
    for (const input of exempt) {
      // 무료 구간을 이미 넘긴 세션에서도 통과해야 예외가 성립한다.
      assert.equal(hook("PreToolUse", "Bash", input, undefined, "exempt"), null,
        `${input.command} 은 지도 범위 밖`);
    }
    assert.equal(shouldGate("Glob", { pattern: "**/FX_*.prefab" }), false);

    // 코드가 명시되면 에셋 경로가 섞여 있어도 게이트 대상이다.
    assert.ok(shouldGate("Bash", { command: "grep -rn Crown Assets/Scripts --include=*.cs; ls Assets/SO" }));
    // 대상이 불분명하면 보수적으로 막는다.
    assert.ok(shouldGate("Bash", { command: "rg Crown" }));

    exhaustFree("exempt2");
    assert.ok(denied(hook("PreToolUse", "Grep", { pattern: "CardView", glob: "*.cs" }, undefined, "exempt2")),
      "코드 검색은 그대로 막힌다");
  });

  check("18. stale state cleanup is daily and leaves fresh state", () => {
    const cleanupRoot = path.join(root, "cleanup-state");
    fs.mkdirSync(cleanupRoot, { recursive: true });
    const stale = path.join(cleanupRoot, "stale.json");
    const fresh = path.join(cleanupRoot, "fresh.json");
    fs.writeFileSync(stale, "{}");
    fs.writeFileSync(fresh, "{}");
    const now = Date.now();
    fs.utimesSync(stale, new Date(now - 2 * 24 * 60 * 60 * 1000), new Date(now - 2 * 24 * 60 * 60 * 1000));
    assert.equal(pruneStateFiles(cleanupRoot, now), 1);
    assert.equal(fs.existsSync(stale), false);
    assert.equal(fs.existsSync(fresh), true);
    const throttled = path.join(cleanupRoot, "throttled.json");
    fs.writeFileSync(throttled, "{}");
    fs.utimesSync(throttled, new Date(now - 2 * 24 * 60 * 60 * 1000), new Date(now - 2 * 24 * 60 * 60 * 1000));
    assert.equal(pruneStateFiles(cleanupRoot, now + 1), 0);
    assert.equal(fs.existsSync(throttled), true, "cleanup marker limits pruning to once per day");
    assert.equal(isOutsideMapScope({ path: "Assets/Table/SpecDatas.cs" }), true);
    assert.equal(isOutsideMapScope({ path: "Assets/Scripts/Battle/Card.cs" }), false);
    assert.equal(isOutsideMapScope({ path: "Assets\\Scripts\\Battle\\Card.cs" }), false);
    assert.equal(isOutsideMapScope({ path: "Assets/SO/Card.asset" }), false);
  });

  check("19. outside-map .cs search writes observation marker without changing policy", () => {
    assert.equal(hook("PreToolUse", "Bash", { command: "rg Card Assets/Table/SpecDatas.cs" }, undefined, "scope-observe"), null);
    const log = fs.readFileSync(path.join(stateRoot, "map-gate.log"), "utf8");
    assert.ok(log.includes(MAP_SCOPE_OUTSIDE_MARKER));
  });

  console.log(`map-gate tests: ${passed}/19 passed (subagent context: 미검증 — sidechain 기록 없음)`);
} finally {
  fs.rmSync(root, { recursive: true, force: true });
}
