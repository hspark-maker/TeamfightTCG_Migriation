#!/usr/bin/env node
"use strict";

/**
 * 지도 커버리지 — `Assets/Scripts/` 의 public 타입 중 지도에 없는 것을 디렉터리별로 뽑는다.
 *
 * `sync-feature-map.js` 는 개수(`unmapped-public-types=N`)만 헤더에 적는다. 그 N 이
 * **어디에 있는 무엇인지** 알아야 섹션에 배치할 수 있어서 목록으로 뽑는 도구가 따로 필요하다.
 *
 *   node .claude/report-map-coverage.js            디렉터리별 미수록 타입
 *   node .claude/report-map-coverage.js --terms    지도에 없는 상위 검색어(기능 단어) 진단
 */
const fs = require("node:fs");
const path = require("node:path");
const { mapSymbols } = require("./lib/map-index.js");

const root = path.resolve(__dirname, "..");
const SRC = path.join(root, "Assets", "Scripts");
const MAP = path.join(__dirname, "orch-feature-map.md");

const DECLARATION = /\bpublic\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*(?:class|struct|interface|enum)\s+([A-Z][A-Za-z0-9_]*)/g;

function sources(dir, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) sources(full, out);
    else if (entry.name.endsWith(".cs")) out.push(full);
  }
  return out;
}

const map = fs.readFileSync(MAP, "utf8");
const mapped = mapSymbols(map).types;
const mapLower = map.toLowerCase();

const declared = new Map();
for (const file of sources(SRC)) {
  const rel = path.relative(SRC, file).split(path.sep).join("/");
  const text = fs.readFileSync(file, "utf8");
  for (const match of text.matchAll(DECLARATION)) {
    if (!declared.has(match[1])) declared.set(match[1], rel);
  }
}

const missing = [...declared].filter(([name]) => !mapped.has(name));

if (process.argv.includes("--terms")) {
  /* 기능 단어 단위 진단 — 타입 이름을 쪼개 지도 본문에 그 단어가 아예 없는 경우를 센다.
     "Death 를 찾는데 지도에 death 가 한 글자도 없다" 같은 구멍을 잡는다. */
  const counts = new Map();
  for (const [name, file] of declared) {
    for (const word of name.match(/[A-Z][a-z]{3,}/g) || []) {
      const key = word.toLowerCase();
      if (mapLower.includes(key)) continue;
      const entry = counts.get(key) || { count: 0, sample: file };
      entry.count += 1;
      counts.set(key, entry);
    }
  }
  console.log(`지도 본문에 없는 기능 단어 ${counts.size}개 (타입 이름에서 추출)`);
  [...counts].sort((a, b) => b[1].count - a[1].count).slice(0, 30)
    .forEach(([word, info]) => console.log(`  ${String(info.count).padStart(3)}개 타입  ${word}  (예: ${info.sample})`));
} else {
  const byDir = new Map();
  for (const [name, file] of missing) {
    const dir = file.includes("/") ? file.slice(0, file.lastIndexOf("/")) : "(root)";
    if (!byDir.has(dir)) byDir.set(dir, []);
    byDir.get(dir).push(name);
  }
  console.log(`public 타입 ${declared.size} | 지도 수록 ${declared.size - missing.length} | 미수록 ${missing.length}`);
  [...byDir].sort((a, b) => b[1].length - a[1].length)
    .forEach(([dir, names]) => console.log(`\n${dir}/  (${names.length})\n   ${names.join(" · ")}`));
}
