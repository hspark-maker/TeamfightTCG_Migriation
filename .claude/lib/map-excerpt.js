#!/usr/bin/env node
"use strict";

const fs = require("node:fs");
const { SEARCH_COMMANDS, isSearchTool } = require("./search-detect.js");
const { isRealUser } = require("./transcript.js");

const MAX_LINES = 5;
const MAX_CHARS = 1200;
const TAIL_BYTES = 256 * 1024;
const FREQUENCY_CAP = 3;
/* 검색어가 지도 전반에 흔하면 상위 5줄은 정답일 근거가 없다.
   실측: `grep "class Card"` 의 Card 하나가 지도 33줄에 걸려 무관한 발췌가 나갔다.
   매칭되는 검색어가 **전부** 이보다 흔하면 지도가 좁히지 못한 것으로 보고 통과시킨다
   — 엉뚱한 5줄은 지도가 없는 것보다 나쁘다. 과거 발췌의 2.5%가 여기 해당한다. */
const BROAD_TERM_LINES = 10;
const WORD = /[A-Za-z_][A-Za-z0-9_]{2,}|[가-힣]{2,}/g;
const STOP = new Set([
  // 셸·도구 어휘
  "assets", "scripts", "include", "files", "matches", "content", "output", "pattern", "path",
  "grep", "egrep", "fgrep", "find", "glob", "select", "string", "recurse", "recursive",
  "head", "tail", "sort", "type", "name", "true", "false", "file", "directory", "command",
  "prefab", "asset", "meta", "unity", "claude", "orch", "result", "results",
  /* C# 언어 키워드. 이게 빠져 있어 `grep "class Card\|public Card"` 의 class·public 이
     검색어가 됐고, 지도 33줄에 매칭돼 질문과 무관한 발췌가 실제로 나갔다(2026-08-19 09:19). */
  "class", "struct", "interface", "enum", "namespace", "using", "public", "private",
  "protected", "internal", "static", "readonly", "const", "abstract", "sealed", "partial",
  "virtual", "override", "return", "void", "null", "this", "base", "new", "var",
  "async", "await", "get", "set", "value", "params", "where", "select", "from",
  "int", "bool", "float", "double", "byte", "char", "long", "short", "object",
]);

function shellWords(text) {
  return [...String(text).matchAll(/"([^"]*)"|'([^']*)'|([^\s]+)/g)]
    .map((match) => match[1] ?? match[2] ?? match[3]);
}

function shellSegments(command) {
  const segments = [];
  let quote = null;
  let current = "";
  for (const char of String(command || "")) {
    if (quote) {
      current += char;
      if (char === quote) quote = null;
      continue;
    }
    if (char === "'" || char === '"') { quote = char; current += char; continue; }
    if (/[|;&\r\n]/.test(char)) {
      if (current.trim()) segments.push(current);
      current = "";
      continue;
    }
    current += char;
  }
  if (current.trim()) segments.push(current);
  return segments;
}

/* 명령마다 검색어가 있는 자리가 다르다.
   grep 계열은 "첫 비플래그 인자"가 패턴이고, find 계열은 -name/-iname 뒤 값이 이름 패턴이다.
   find 규칙이 없던 동안 실측으로 게이트 대상의 9.3%가 검색어 없이 통과했다(표본 전부 find). */
const PATTERN_FIRST = /(?:^|\s)(?:(?:sudo\s+)?(?:rg|grep|egrep|fgrep|ack|ag|fd)(?:\.exe)?|git\s+grep|Select-String|sls)\s+([\s\S]*)/i;
const NAME_FLAGGED = /(?:^|\s)(?:(?:sudo\s+)?find(?:\.exe)?)\s+([\s\S]*)/i;
const DIR_FLAGGED = /(?:^|\s)(?:(?:sudo\s+)?dir(?:\.exe)?)\s+([\s\S]*)/i;
/* -path/-wholename 은 경로 필터라 개념 검색어가 아니다 — 포함하면 `-path "*Battle*"` 의
   Battle 이 지도 20줄에 매칭돼 엉뚱한 발췌가 나간다(grep 쪽에서 이미 고친 오탐과 같은 것). */
const NAME_FLAG = /^(?:-i?name|-i?regex)$/i;
const PATTERN_FIRST_COMMANDS = new Set(["rg", "grep", "egrep", "fgrep", "fd", "ack", "ag", "git grep"]);
const NAME_FLAGGED_COMMANDS = new Set(["find"]);
const PATH_PATTERN_COMMANDS = new Set(["dir"]);
const NO_SEMANTIC_QUERY_COMMANDS = new Set(["ls"]);

/** SEARCH_COMMANDS의 각 항목이 어떤 추출 정책을 갖는지 테스트 가능한 형태로 노출한다. */
function searchCommandPolicy(command) {
  if (!SEARCH_COMMANDS.includes(command)) return null;
  if (PATTERN_FIRST_COMMANDS.has(command)) return "pattern-first";
  if (NAME_FLAGGED_COMMANDS.has(command)) return "name-flagged";
  if (PATH_PATTERN_COMMANDS.has(command)) return "path-pattern";
  if (NO_SEMANTIC_QUERY_COMMANDS.has(command)) return "no-semantic-query";
  return null;
}

/** find 계열: 이름 플래그 뒤의 값만 검색어로 본다. 경로 인자는 검색어가 아니다. */
function nameFlagQueries(rest) {
  const words = shellWords(rest);
  const found = [];
  for (let index = 0; index < words.length; index += 1) {
    if (NAME_FLAG.test(words[index]) && words[index + 1]) {
      found.push(words[index + 1]);
      index += 1;
    }
  }
  return found;
}

function dirQueries(rest) {
  const words = shellWords(rest);
  const found = [];
  const basename = (word) => String(word).split(/[\\/]/).pop();
  for (let index = 0; index < words.length; index += 1) {
    const word = words[index];
    if (/^-(?:Filter|Include)$/i.test(word) && words[index + 1]) {
      found.push(basename(words[index + 1]));
      index += 1;
      continue;
    }
    if (/^-(?:Path|LiteralPath)$/i.test(word) && words[index + 1]) {
      index += 1;
      continue;
    }
    if (/^[-/]/.test(word)) continue;
    found.push(basename(word));
  }
  return found;
}

function commandQueries(command) {
  const queries = [];
  for (const part of shellSegments(command)) {
    const dirMatch = part.match(DIR_FLAGGED);
    if (dirMatch) {
      queries.push(...dirQueries(dirMatch[1]));
      continue;
    }
    const nameMatch = part.match(NAME_FLAGGED);
    if (nameMatch) {
      queries.push(...nameFlagQueries(nameMatch[1]));
      continue;
    }
    const match = part.match(PATTERN_FIRST);
    if (!match) continue;
    const words = shellWords(match[1]);
    for (let index = 0; index < words.length; index += 1) {
      const word = words[index];
      if (/^(?:-e|--regexp|-Pattern)$/i.test(word)) {
        if (words[index + 1]) queries.push(words[index + 1]);
        break;
      }
      if (/^(?:-g|--glob|--type|-t|--include|--exclude|-Path)$/i.test(word)) { index += 1; continue; }
      if (word.startsWith("-")) continue;
      queries.push(word);
      break;
    }
  }
  return queries.join(" ");
}

function queryText(toolName, input) {
  const toolInput = input || {};
  if (toolName === "Bash" || toolName === "PowerShell") return commandQueries(toolInput.command);
  for (const key of ["pattern", "query", "search_term", "searchTerm", "text"]) {
    if (typeof toolInput[key] === "string") return toolInput[key];
  }
  // Unity_Grep 은 실측상 {"args":"-l s_anyDragging"} 로 온다 — 플래그를 걷어낸 나머지가 검색어다.
  if (typeof toolInput.args === "string") {
    return shellWords(toolInput.args).filter((word) => !word.startsWith("-")).join(" ");
  }
  return "";
}

function searchTerms(toolName, input) {
  const text = queryText(toolName, input);
  const terms = [];
  const seen = new Set();
  for (const match of text.match(WORD) || []) {
    const key = match.toLowerCase();
    if (STOP.has(key) || seen.has(key)) continue;
    seen.add(key);
    terms.push(match);
  }
  return terms;
}

function readTail(file) {
  const handle = fs.openSync(file, "r");
  try {
    const size = fs.fstatSync(handle).size;
    const length = Math.min(size, TAIL_BYTES);
    const buffer = Buffer.alloc(length);
    fs.readSync(handle, buffer, 0, length, size - length);
    return buffer.toString("utf8");
  } finally {
    fs.closeSync(handle);
  }
}

function addTerms(target, terms) {
  for (const term of terms) {
    const key = term.toLowerCase();
    target.set(key, Math.min(FREQUENCY_CAP, (target.get(key) || 0) + 1));
  }
}

function sessionFrequencies(transcriptPath) {
  const events = [];
  if (!transcriptPath) return { request: new Map(), session: new Map() };
  try {
    for (const line of readTail(transcriptPath).split(/\r?\n/)) {
      if (!line.trim()) continue;
      try { events.push(JSON.parse(line)); } catch { /* 잘린 첫 줄 또는 기록 중인 마지막 줄 */ }
    }
  } catch { return { request: new Map(), session: new Map() }; }

  let requestStart = 0;
  for (let index = 0; index < events.length; index += 1) {
    if (isRealUser(events[index])) requestStart = index + 1;
  }
  const request = new Map();
  const session = new Map();
  for (let index = 0; index < events.length; index += 1) {
    const event = events[index];
    const content = event && event.type === "assistant" && event.message && event.message.content;
    if (!Array.isArray(content)) continue;
    for (const part of content) {
      if (!part || part.type !== "tool_use" || !isSearchTool(part.name, part.input || {})) continue;
      const terms = searchTerms(part.name, part.input);
      addTerms(session, terms);
      if (index >= requestStart) addTerms(request, terms);
    }
  }
  return { request, session };
}

function occurrences(text, term) {
  let count = 0;
  let from = 0;
  while ((from = text.indexOf(term, from)) >= 0) { count += 1; from += term.length; }
  return count;
}

function buildMapExcerpt(mapFile, toolName, toolInput, transcriptPath) {
  const queryTerms = searchTerms(toolName, toolInput);
  /* hits 0 은 "지도에 없음"과 "검색어를 못 뽑음" 두 가지다. 뭉치면 추출기 버그가
     게이트 무력화로 조용히 번역된다 — 호출부가 구분하도록 reason 을 함께 준다. */
  if (!queryTerms.length) return { terms: [], reason: "no-terms", hits: 0, shown: 0, weighted: false, text: "" };
  const history = sessionFrequencies(transcriptPath);
  const lines = fs.readFileSync(mapFile, "utf8").split(/\r?\n/);

  /* 검색어별 매칭 줄 수 중 가장 좁은 값. 0매칭 검색어는 특정성 신호가 아니라 제외한다
     (지도에 없는 단어가 min 을 0 으로 끌어내려 판정을 무력화한다). */
  const bodyLower = lines
    .map((line) => line.trim().toLowerCase())
    .filter((line) => line && !line.startsWith("<!--"));
  const termLineCounts = queryTerms
    .map((term) => bodyLower.filter((line) => line.includes(term.toLowerCase())).length)
    .filter((count) => count > 0);
  const narrowest = termLineCounts.length ? Math.min(...termLineCounts) : 0;

  const candidates = [];
  let header = "";

  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index].trim();
    if (line.startsWith("## ")) header = line.replace(/^##\s+/, "").replace(/\s+\(`.*$/, "");
    if (!line || line.startsWith("<!--")) continue;
    const lower = line.toLowerCase();
    let score = 0;
    let queryHits = 0;
    for (const term of queryTerms) {
      const key = term.toLowerCase();
      const count = occurrences(lower, key);
      if (!count) continue;
      queryHits += count;
      score += count * 100 + Math.min(key.length, 20);
      if (lower.includes("`" + key + "`")) score += 30;
    }
    if (!queryHits) continue;

    let historyBoost = 0;
    for (const [term, count] of history.request) {
      if (lower.includes(term)) historyBoost += count * 10;
    }
    for (const [term, count] of history.session) {
      if (lower.includes(term)) historyBoost += count * 2;
    }
    score += historyBoost;
    if (line.startsWith("## ")) score += 5;
    candidates.push({ index, line, header, score, historyBoost });
  }

  if (narrowest > BROAD_TERM_LINES) {
    return {
      terms: queryTerms, narrowest, reason: "too-broad",
      hits: candidates.length, shown: 0, weighted: false, text: "",
    };
  }

  candidates.sort((a, b) => b.score - a.score || a.index - b.index);
  const selected = [];
  let weighted = false;
  let chars = 0;
  for (const candidate of candidates) {
    if (selected.length >= MAX_LINES) break;
    const prefix = `L${candidate.index + 1}${candidate.header ? ` [${candidate.header}]` : ""} `;
    let rendered = prefix + candidate.line;
    const remaining = MAX_CHARS - chars - (selected.length ? 1 : 0);
    if (remaining <= 0) break;
    if (rendered.length > remaining) rendered = rendered.slice(0, Math.max(0, remaining - 1)) + "…";
    selected.push(rendered);
    weighted = weighted || candidate.historyBoost > 0;
    chars += rendered.length + (selected.length > 1 ? 1 : 0);
  }
  return {
    terms: queryTerms,
    narrowest,
    reason: candidates.length ? "hit" : "no-map-match",
    hits: candidates.length,
    shown: selected.length,
    weighted,
    text: selected.join("\n"),
  };
}

module.exports = {
  FREQUENCY_CAP, MAX_CHARS, MAX_LINES,
  BROAD_TERM_LINES, NAME_FLAG, PATTERN_FIRST,
  buildMapExcerpt, commandQueries, dirQueries, nameFlagQueries, queryText, searchCommandPolicy, searchTerms, sessionFrequencies, shellSegments, shellWords,
};
