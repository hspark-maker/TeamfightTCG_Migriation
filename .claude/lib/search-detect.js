#!/usr/bin/env node
"use strict";

/**
 * 탐색 도구 판정 — 게이트(.claude/hooks/map-gate.js)와 리포트(.claude/report-map-savings.js)가
 * **같은 정의**를 쓰도록 한곳에 둔다.
 *
 * 왜 공유하나: 게이트가 막는 것과 리포트가 "검색"으로 세는 것이 어긋나면
 * 준수율 지표가 조용히 거짓말을 한다. 한쪽 정규식만 고치는 사고를 막는다.
 *
 * DETECTOR_VERSION 을 올리면 기준선을 다시 만들어야 한다 — 분류 기준이 바뀐 기준선끼리는
 * 비교할 수 없다. 기준선 JSON 에 이 값을 함께 적는다.
 */

const DETECTOR_VERSION = 2;

const MAP_NAME = "orch-feature-map.md";

const SEARCH_TOOLS = new Set([
  "Grep",
  "Glob",
  "mcp__unity-mcp__Unity_Grep",
  "mcp__unity-mcp__Unity_FindInFile",
  "mcp__unity-mcp__Unity_FindProjectAssets",
]);

/* 탐색으로 치는 셸 명령의 단일 목록.
   map-excerpt.js 가 이 목록을 읽어 "추출 규칙이 없는 명령"을 테스트에서 잡는다 —
   한쪽에만 명령이 추가되면 "탐색으로 판정되는데 검색어는 못 뽑는" 사각지대가 생긴다.
   실측: find 가 여기에만 있어 게이트 대상의 9.3%가 무조건 통과했다. */
const SEARCH_COMMANDS = ["rg", "grep", "egrep", "fgrep", "find", "fd", "ack", "ag", "git grep", "dir", "ls"];

/* ls 는 -R 만 재귀다. -r 은 역순 정렬이라 대소문자를 구분해야 오탐이 없다.
   그래서 이 정규식에는 /i 를 붙이면 안 된다. */
const BASH_SEARCH =
  /(?:^|[|;&(]\s*)(?:(?:sudo\s+)?(?:rg|grep|egrep|fgrep|find|fd|ack|ag)(?:\.exe)?(?:\s|$)|git\s+grep(?:\s|$)|ls(?:\.exe)?\s+(?:[^\r\n;&|]*\s)?-{1,2}(?:R(?:\s|$)|recursive\b)|dir\s+[^\r\n;&|]*\/[sS](?:\s|$))/;

/* PowerShell 도구는 Bash 와 별개 도구다. Select-String 한 줄이면 게이트가 통째로 무의미해진다. */
const POWERSHELL_SEARCH =
  /(?:^|[|;&(]\s*)(?:Select-String|sls)\b|(?:Get-ChildItem|gci|dir|ls)\b[^\r\n;&|]*\s-(?:Recurse|r(?:\s|$))/i;

/* 지도를 읽었다고 볼 수 없는 출력. Bash 는 실패해도 exitCode 를 주지 않으므로 문구로 판정한다. */
const READ_FAILURE =
  /No such file|cannot open|cannot access|Permission denied|command not found|Is a directory|찾을 수 없습니다/i;

/* 지도 본문이 실제로 흘러왔는지 보는 최소 길이. cat/grep 어느 쪽이든 이보다는 길다. */
const MIN_MAP_OUTPUT = 200;

function mentionsMap(input) {
  return JSON.stringify(input || {}).toLowerCase().includes(MAP_NAME);
}

function bashSearch(command) {
  if (typeof command !== "string") return false;
  return BASH_SEARCH.test(command.trim());
}

function powershellSearch(command) {
  if (typeof command !== "string") return false;
  return POWERSHELL_SEARCH.test(command.trim());
}

function isSearchTool(toolName, toolInput) {
  // 지도 자신을 겨냥한 검색은 탐색이 아니라 지도 열람이다.
  if (mentionsMap(toolInput)) return false;
  if (SEARCH_TOOLS.has(toolName)) return true;
  const command = toolInput && toolInput.command;
  if (toolName === "Bash") return bashSearch(command);
  if (toolName === "PowerShell") return powershellSearch(command);
  return false;
}

/* 지도가 확실히 코드를 가리키는 신호. 이게 있으면 무조건 게이트 대상이다. */
const CODE_TARGET = /\.cs\b|Assets[\/\\]Scripts|--include=\*?\.?cs/i;

/* 지도가 담지 않는 것들. 지도는 `Assets/Scripts/` 의 C# 타입만 담는다 —
   프리팹·SO·이미지·guid·git 이력을 찾는 검색은 지도를 읽어도 답이 없다.
   실측: 왕관 연출 세션의 검색 61건 중 27건(44%)이 .prefab/.asset 대상이었고,
   그 요청들은 지도를 읽은 뒤에도 검색이 줄지 않았다. */
const NON_CODE_TARGET = new RegExp([
  "\\.(prefab|asset|meta|png|jpg|jpeg|psd|mat|anim|controller|unity|shader|shadergraph|ttf|otf|fbx|wav|mp3|spriteatlas)\\b",
  "Assets[\\/\\\\](Assets|SO|Fonts|Resources|Plugins|Photon|PurchasedAssets|AmplifyShaderEditor|GUIPackCartoon|Scenes)[\\/\\\\]",
  "\\bgit\\s+(?:--no-pager\\s+)?(?:diff|log|status|show|blame|ls-files|rev-list)\\b",
  "\\b[0-9a-f]{32}\\b",                       // Unity guid
  "(?:Library|Temp|Logs|obj|Build|Builds)[\\/\\\\]",
  "\\.(claude|orch)[\\/\\\\]",
].join("|"), "i");

/**
 * 이 탐색을 지도가 받아 줄 수 있나 — 게이트를 걸어도 되는 대상인지.
 *
 * 판단 불가면 **참**을 돌려 게이트를 건다(보수적). 지도 범위 밖 검색까지 막으면
 * "지도 읽어라 → 읽어도 없음 → 다시 검색"이 되어 지도 없을 때보다 비싸진다.
 */
function isMapAnswerable(toolInput) {
  const text = JSON.stringify(toolInput || {});
  if (CODE_TARGET.test(text)) return true;        // 코드가 명시됐으면 에셋 경로가 섞여 있어도 게이트
  return !NON_CODE_TARGET.test(text);
}

/** 게이트가 실제로 막아야 하는 호출 = 탐색이면서 지도가 답할 수 있는 것. */
function shouldGate(toolName, toolInput) {
  return isSearchTool(toolName, toolInput) && isMapAnswerable(toolInput);
}

/* 지도 열람이 성립하는 도구. Read 만 보면 안 된다 — 실측 열람 3회 중 2회가 Bash(cat/grep)였다. */
function isMapReadTool(toolName) {
  return toolName === "Read" || toolName === "Bash" || toolName === "PowerShell";
}

/**
 * 지도 열람이 **성공**했는지.
 *
 * Claude Code 의 Bash 결과 실측 스키마는 `{interrupted,isImage,noOutputExpected,stderr,stdout}` 로
 * exitCode 도 is_error 도 없다. 실패한 `cat` 도 같은 형태로 오고 에러 문구가 stdout 에 섞인다.
 * 따라서 필드가 아니라 내용으로 판정한다.
 */
function mapReadSucceeded(response) {
  if (response === undefined || response === null) return false;
  if (typeof response === "object") {
    if (response.is_error === true || response.isError === true || response.error) return false;
    if (Number.isFinite(response.exitCode) && response.exitCode !== 0) return false;
    if (response.interrupted === true) return false;
  }
  const text = typeof response === "string" ? response : JSON.stringify(response);
  if (READ_FAILURE.test(text)) return false;
  return text.length >= MIN_MAP_OUTPUT;
}

module.exports = {
  DETECTOR_VERSION,
  SEARCH_COMMANDS,
  isMapAnswerable,
  shouldGate,
  MAP_NAME,
  MIN_MAP_OUTPUT,
  SEARCH_TOOLS,
  bashSearch,
  isMapReadTool,
  isSearchTool,
  mapReadSucceeded,
  mentionsMap,
  powershellSearch,
};
