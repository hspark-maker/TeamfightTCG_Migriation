#!/usr/bin/env node
"use strict";

const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

const {
  DETECTOR_VERSION,
  isMapReadTool,
  isSearchTool,
  mapReadSucceeded,
  mentionsMap,
} = require("./lib/search-detect.js");
const { isRealUser, textContent } = require("./lib/transcript.js");
const EXCERPT_MARKER = "[MAP_GATE_EXCERPT_V1";

const DEFAULT_INSTALLED_AT = "2026-08-14T06:18:00.000Z";
/* 게이트가 settings.json 에 붙은 시각. --gate-at 으로 덮어쓴다. */
const DEFAULT_GATE_AT = "2026-08-18T06:40:00.000Z";   // 첫 실차단 06:41:46Z 로 확인
const EDIT_TOOLS = new Set(["Edit", "Write", "MultiEdit", "NotebookEdit"]);

function option(name, fallback) {
  const prefix = `--${name}=`;
  const value = process.argv.find((arg) => arg.startsWith(prefix));
  return value ? value.slice(prefix.length) : fallback;
}

function isSearch(call) {
  return Boolean(call) && isSearchTool(call.name, call.input);
}

function isEdit(call) {
  if (!call) return false;
  if (EDIT_TOOLS.has(call.name)) return true;
  const command = call.input && call.input.command;
  return call.name === "Bash" && typeof command === "string" && /(?:^|[|;&(]\s*)(?:sed|perl)\s+[^\r\n;&|]*\s-i(?:\s|$)/i.test(command);
}

function toolCalls(event) {
  const content = event && event.message && event.message.content;
  if (event.type !== "assistant" || !Array.isArray(content)) return [];
  return content.filter((part) => part && part.type === "tool_use")
    .map((part) => ({ id: part.id, name: part.name, input: part.input || {} }));
}

function toolResults(event) {
  const content = event && event.type === "user" && event.message && event.message.content;
  if (!Array.isArray(content)) return [];
  return content.filter((part) => part && part.type === "tool_result")
    .map((part) => ({ id: part.tool_use_id, content: part.content }));
}

function hasMapExcerpt(event) {
  const content = event && event.type === "user" && event.message && event.message.content;
  return Array.isArray(content) && content.some((part) =>
    part && part.type === "tool_result" && JSON.stringify(part.content || "").includes(EXCERPT_MARKER)
  );
}

function usageTokens(event) {
  const usage = event && event.message && event.message.usage;
  if (!usage) return { input: 0, output: 0 };
  return {
    input: (usage.input_tokens || 0) + (usage.cache_creation_input_tokens || 0) + (usage.cache_read_input_tokens || 0),
    output: usage.output_tokens || 0,
  };
}

function discoverTranscripts(projectDir) {
  const projectsDir = path.join(os.homedir(), ".claude", "projects");
  const needle = path.basename(projectDir).replace(/_/g, "-").toLowerCase();
  let dirs = [];
  try { dirs = fs.readdirSync(projectsDir, { withFileTypes: true }); } catch { return []; }
  return dirs
    .filter((entry) => entry.isDirectory() && entry.name.toLowerCase().includes(needle))
    .flatMap((entry) => {
      const dir = path.join(projectsDir, entry.name);
      return fs.readdirSync(dir, { withFileTypes: true })
        .filter((file) => file.isFile() && file.name.endsWith(".jsonl"))
        .map((file) => path.join(dir, file.name));
    });
}

function readEvents(file) {
  const events = [];
  for (const line of fs.readFileSync(file, "utf8").split(/\r?\n/)) {
    if (!line.trim()) continue;
    try { events.push(JSON.parse(line)); } catch { /* incomplete transcript tail */ }
  }
  return events;
}

function newSegment(event, carriedMap, sessionTurnsSoFar) {
  return {
    sessionId: event.sessionId || "",
    startedAt: event.timestamp || "",
    request: textContent(event.message.content).replace(/\s+/g, " ").slice(0, 120),
    carriedMap,
    startTurn: sessionTurnsSoFar,   // 세션 내 위치. 턴당 토큰은 이 값에 크게 좌우된다
    loadedWithin: false,
    loadedAfterSearch: false,
    excerpts: 0,
    mapReads: 0,
    mapReadsAfterExcerpt: 0,
    searches: 0,
    searchesBeforeMap: 0,
    tools: 0,
    turns: new Set(),
    usage: new Map(),
    firstSearchTurn: null,
    firstEditTurn: null,
  };
}

/* 지도 도입과 게이트 도입은 서로 다른 개입이다. 시점이 하나뿐이면 게이트 이후 세션이
   "지도는 있었지만 게이트는 없던" 구간과 한 그룹에 섞여 효과가 희석된다. */
function finish(segment, installedAt, gateAt) {
  if (!segment || segment.searches === 0) return null;
  const startedAt = Date.parse(segment.startedAt);
  let group;
  if (startedAt < installedAt) group = "미설치·미사용";
  else {
    const era = Number.isFinite(gateAt) && startedAt >= gateAt ? "게이트후" : "게이트전";
    let kind;
    if (segment.carriedMap) kind = "선로드(이월)";
    else if (segment.loadedWithin && !segment.loadedAfterSearch) kind = "선로드(세그내)";
    else if (segment.loadedWithin) kind = "늦게 로드";
    else kind = "미로드";
    group = `${era}·${kind}`;
  }
  const usage = [...segment.usage.values()];
  const turns = segment.turns.size;
  const inputTokens = usage.reduce((sum, value) => sum + value.input, 0);
  /* 세그먼트 합계는 "세션 어디쯤인가"가 지배한다 — 긴 세션 후반은 검색을 안 해도 비싸다.
     개입 효과를 보려면 왕복 1회의 값인 턴당으로 나눠야 한다. */
  return {
    ...segment,
    turns,
    inputTokens,
    inputPerTurn: turns ? Math.round(inputTokens / turns) : null,
    outputTokens: usage.reduce((sum, value) => sum + value.output, 0),
    group,
  };
}

function parseTranscript(file, installedAt, gateAt) {
  const samples = [];
  let mapLoaded = false;
  let segment = null;
  const sessionTurns = new Set();
  const pendingMapReads = new Map();
  for (const event of readEvents(file)) {
    if (isRealUser(event)) {
      const done = finish(segment, installedAt, gateAt);
      if (done) samples.push(done);
      segment = newSegment(event, mapLoaded, sessionTurns.size);
      continue;
    }
    if (!segment || event.isSidechain) continue;
    if (event.type === "assistant") {
      const turnId = event.requestId || event.message?.id || event.uuid;
      if (turnId) {
        segment.turns.add(turnId);
        sessionTurns.add(turnId);
        const current = usageTokens(event);
        const previous = segment.usage.get(turnId) || { input: 0, output: 0 };
        segment.usage.set(turnId, { input: Math.max(previous.input, current.input), output: Math.max(previous.output, current.output) });
      }
    }
    for (const call of toolCalls(event)) {
      segment.tools += 1;
      if (mentionsMap(call.input) && isMapReadTool(call.name)) {
        if (call.id) pendingMapReads.set(call.id, { segment, afterExcerpt: segment.excerpts > 0 });
      }
      if (isSearch(call)) {
        segment.searches += 1;
        if (!mapLoaded && segment.excerpts === 0) segment.searchesBeforeMap += 1;
        if (segment.firstSearchTurn === null) segment.firstSearchTurn = segment.turns.size;
      }
      if (isEdit(call) && segment.firstEditTurn === null) segment.firstEditTurn = segment.turns.size;
    }
    // 발췌는 요청 범위의 지도 열람을 대신하지만 세션 전체 mapLoaded로 이월하지 않는다.
    if (hasMapExcerpt(event)) {
      segment.excerpts += 1;
      if (!mapLoaded) {
        segment.loadedWithin = true;
        segment.loadedAfterSearch = segment.searches > 0;
      }
    }
    for (const result of toolResults(event)) {
      const pending = pendingMapReads.get(result.id);
      if (!pending) continue;
      pendingMapReads.delete(result.id);
      if (!mapReadSucceeded(result.content)) continue;
      pending.segment.mapReads += 1;
      if (pending.afterExcerpt) pending.segment.mapReadsAfterExcerpt += 1;
      if (!mapLoaded) {
        pending.segment.loadedWithin = true;
        pending.segment.loadedAfterSearch = pending.segment.searches > 0;
      }
      mapLoaded = true;
    }
  }
  const done = finish(segment, installedAt, gateAt);
  if (done) samples.push(done);
  return samples;
}

function percentile(values, fraction) {
  if (!values.length) return null;
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.ceil(fraction * sorted.length) - 1];
}

function stats(samples) {
  const groups = [
    "미설치·미사용",
    "게이트전·미로드", "게이트전·늦게 로드", "게이트전·선로드(세그내)", "게이트전·선로드(이월)",
    "게이트후·미로드", "게이트후·늦게 로드", "게이트후·선로드(세그내)", "게이트후·선로드(이월)",
  ];
  return groups.map((group) => {
    const rows = samples.filter((sample) => sample.group === group);
    if (!rows.length) return null;   // 아직 표본 없는 시기는 표에서 뺀다
    const metric = (name, fraction) => percentile(rows.map((row) => row[name]).filter(Number.isFinite), fraction);
    return {
      group, n: rows.length,
      searchMedian: metric("searches", 0.5), searchP75: metric("searches", 0.75),
      turnMedian: metric("turns", 0.5), turnP75: metric("turns", 0.75),
      inputMedian: metric("inputTokens", 0.5), inputP75: metric("inputTokens", 0.75),
      perTurnMedian: metric("inputPerTurn", 0.5), perTurnP75: metric("inputPerTurn", 0.75),
      startTurnMedian: metric("startTurn", 0.5),
      searchesBeforeMapMedian: metric("searchesBeforeMap", 0.5),
      firstSearchTurnMedian: metric("firstSearchTurn", 0.5),
      firstEditTurnMedian: metric("firstEditTurn", 0.5),
    };
  }).filter(Boolean);
}

function display(value) {
  return value === null ? "-" : Number(value).toLocaleString("ko-KR");
}

function main() {
  const projectDir = path.resolve(option("project", path.join(__dirname, "..")));
  const installedAtText = option("installed-at", DEFAULT_INSTALLED_AT);
  const installedAt = Date.parse(installedAtText);
  if (!Number.isFinite(installedAt)) throw new Error(`잘못된 --installed-at: ${installedAtText}`);
  const gateAtText = option("gate-at", DEFAULT_GATE_AT);
  const gateAt = Date.parse(gateAtText);
  if (!Number.isFinite(gateAt)) throw new Error(`잘못된 --gate-at: ${gateAtText}`);
  const files = discoverTranscripts(projectDir);
  if (!files.length) throw new Error(`트랜스크립트를 찾지 못했습니다: ${projectDir}`);
  const samples = files.flatMap((file) => parseTranscript(file, installedAt, gateAt));
  const summary = stats(samples);

  console.log("관측 차이 — 인과 아님. 작업 난이도·선택편향 미보정.");
  console.log(`트랜스크립트 ${files.length}개 | 탐색형 요청 ${samples.length}개 | 지도 ${installedAtText} · 게이트 ${gateAtText}`);
  console.log("그룹                       n  검색중앙  검색p75  로드전검색  턴중앙   턴당토큰중앙   턴당p75  세션내위치");
  for (const row of summary) {
    const enough = row.n >= 20;
    console.log(
      row.group.padEnd(24) +
      String(row.n).padStart(4) +
      display(row.searchMedian).padStart(10) +
      display(row.searchP75).padStart(9) +
      display(row.searchesBeforeMapMedian).padStart(11) +
      display(row.turnMedian).padStart(8) +
      (enough ? display(row.perTurnMedian) : "판정 불가").padStart(15) +
      (enough ? display(row.perTurnP75) : "-").padStart(10) +
      display(row.startTurnMedian).padStart(9)
    );
  }

  const excerptSamples = samples.filter((sample) => sample.excerpts > 0);
  const excerptThenRead = excerptSamples.filter((sample) => sample.mapReadsAfterExcerpt > 0).length;
  console.log(`발췌 요청 ${excerptSamples.length}개 | 발췌 뒤 별도 지도 열람 ${excerptThenRead}개`);

  if (process.argv.includes("--write-baseline")) {
    // 기준선은 정의상 게이트 이전이다. 언제 다시 만들어도 게이트 이후 표본이 섞이지 않게 자른다.
    const preGate = samples.filter((sample) => Date.parse(sample.startedAt) < gateAt);
    const preGateSummary = stats(preGate);
    // 추적되는 경로에 둔다. .orch/ 는 gitignore 라 정리 한 번이면 비교 기준이 사라진다.
    const output = path.join(projectDir, ".claude", "map-baseline.json");
    fs.mkdirSync(path.dirname(output), { recursive: true });
    fs.writeFileSync(output, JSON.stringify({
      generatedAt: new Date().toISOString(), projectDir, installedAt: installedAtText, gateAt: gateAtText,
      detectorVersion: DETECTOR_VERSION,
      warning: "관측 차이 — 인과 아님. 작업 난이도·선택편향 미보정.",
      note: "집계 대상 트랜스크립트는 전부 게이트 도입 이전 기록이다. detectorVersion 이 바뀌면 다시 만들어야 비교가 성립한다.",
      transcriptCount: files.length, exploratoryRequestCount: preGate.length, groups: preGateSummary,
    }, null, 2) + "\n");
    console.log(`기준선 저장: ${output}`);
  }
}

if (require.main === module) main();

module.exports = { discoverTranscripts, hasMapExcerpt, isRealUser, isSearch, parseTranscript, stats };
