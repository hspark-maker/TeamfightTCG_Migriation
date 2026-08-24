#!/usr/bin/env node
/**
 * 기능 지도 검증 — `.claude/orch-feature-map.md` 에 적힌 타입·경로가 실제 소스에 있는지 본다.
 *
 * 왜 필요한가: 지도가 틀리면 없느니만 못하다. 잘못된 곳을 읽고 다시 찾게 되므로
 * 지도 없을 때보다 비싸진다. 타입 이름이 바뀌거나 파일이 사라지면 여기서 즉시 실패해야 한다.
 *
 * 실행: node .claude/check-feature-map.js
 */
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { mapTokens } = require("./lib/map-index.js");
const { checkInstructionsSync } = require("./check-instructions-sync.js");

const root = path.resolve(__dirname, "..");
const MAP = path.join(root, ".claude", "orch-feature-map.md");
const SRC = path.join(root, "Assets", "Scripts");
const GRACE_DAYS = 7;
const DAY_MS = 24 * 60 * 60 * 1000;

function ageDays(since) {
  const stamp = Date.parse(since + "T00:00:00Z");
  return Number.isFinite(stamp) ? Math.floor((Date.now() - stamp) / DAY_MS) : Infinity;
}

assert.ok(fs.existsSync(MAP), "기능 지도가 없습니다: .claude/orch-feature-map.md");
assert.ok(fs.existsSync(SRC), "자체 코드 디렉터리가 없습니다: Assets/Scripts");
checkInstructionsSync();

const map = fs.readFileSync(MAP, "utf8");

/* 줄번호는 편집 한 번에 밀린다. 지도에 들어가면 안 된다. */
assert.doesNotMatch(map, /\.cs:\d+/, "지도에 썩기 쉬운 줄번호가 있습니다");

// 소스 전량을 한 번만 읽어 둔다(파일 409개, 매 심볼마다 재탐색하면 느리다).
const files = [];
(function walk(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full);
    else if (entry.name.endsWith(".cs")) files.push(full);
  }
})(SRC);
const sourceParts = files.map((f) => ({ file: f, text: fs.readFileSync(f, "utf8") }));
const source = sourceParts.map((x) => x.text).join("\n");
const relFiles = new Set(files.map((f) => path.relative(SRC, f).split(path.sep).join("/")));
const declarationFiles = new Map();
for (const item of sourceParts) {
  const rel = path.relative(SRC, item.file).split(path.sep).join("/").replace(/\.cs$/, "");
  // 자동 삭제의 오탐을 피하려고 주석/문자열까지 포함하는 보수적 탐지를 유지한다.
  for (const m of item.text.matchAll(/\b(?:class|struct|interface|enum)\s+@?([A-Z][A-Za-z0-9_]*)\b/g)) {
    const list = declarationFiles.get(m[1]) || [];
    if (!list.includes(rel)) list.push(rel);
    declarationFiles.set(m[1], list);
  }
}
const dirs = new Set();
for (const f of relFiles) {
  const parts = f.split("/");
  for (let i = 1; i <= parts.length - 1; i++) dirs.add(parts.slice(0, i).join("/") + "/");
}

/* 백틱 안 토큰을 세 갈래로 나눈다.
   - `Foo/Bar/` 로 끝나면 디렉터리
   - `Foo/Bar.cs` 나 `Foo/Bar` 처럼 슬래시 + 대문자 시작이면 경로가 붙은 타입
   - 그 외 대문자로 시작하는 식별자는 타입 이름 */
const tokens = mapTokens(map);
const knownMissingDirs = new Map(
  [...map.matchAll(/<!--\s*orch:missing-dir\s+(.+?)\s+since=(\d{4}-\d{2}-\d{2})\s*-->/g)]
    .map((m) => [m[1].trim(), m[2]])
);
const emptiedBullets = [...map.matchAll(/^(\s*-\s+[^:\r\n]+:)\s*<!--\s*orch:emptied\s+since=(\d{4}-\d{2}-\d{2})\s*-->/gm)];
const missing = [];
let okType = 0, okDir = 0, skipped = 0;

for (const marker of emptiedBullets) {
  const age = ageDays(marker[2]);
  if (age >= GRACE_DAYS) {
    missing.push(`빈 지도 항목 유예 만료(${age}일): ${marker[1].trim()} — 항목을 재배치하거나 불릿을 삭제하세요`);
  } else {
    console.warn(`경고: 빈 지도 항목 재배치 필요 (${age}/${GRACE_DAYS}일) — ${marker[1].trim()}`);
  }
}

for (const raw of tokens) {
  if (raw.endsWith("/")) {
    // 서드파티·범위 밖 디렉터리는 Assets/Scripts 아래가 아니므로 건너뛴다.
    const key = raw.replace(/^Assets\/Scripts\//, "");
    if (dirs.has(key)) { okDir++; continue; }
    if (/^(Assets|Photon|Plugins|PurchasedAssets|AmplifyShaderEditor|GUIPackCartoon)\//.test(raw)) { skipped++; continue; }
    if (knownMissingDirs.has(raw)) {
      const age = ageDays(knownMissingDirs.get(raw));
      if (age >= GRACE_DAYS) {
        missing.push(`없는 디렉터리 유예 만료(${age}일): ${raw} — 섹션을 재배치하거나 삭제하세요`);
      } else {
        console.warn(`경고: 섹션 재배치 필요 (${age}/${GRACE_DAYS}일) — 없는 디렉터리 ${raw}`);
        skipped++;
      }
      continue;
    }
    missing.push(`디렉터리 없음: ${raw}`);
    continue;
  }
  /* 경로가 섞인 표기(`UI/Battle/CardView`)는 마지막 조각이 타입이다.
     `Type.Method` 표기는 **둘 다** 본다 — 타입만 보고 넘기면 메서드 이름이 바뀌어도 통과하고,
     통째로 한 이름 취급하면 정규식에 안 맞아 조용히 건너뛴다(실제로 그렇게 새 이름을 놓쳤다). */
  const last = raw.split("/").pop().replace(/\.cs$/, "");
  const parts = last.split(".").filter(Boolean);
  if (!parts.length || !/^[A-Z][A-Za-z0-9_]*$/.test(parts[0])) { skipped++; continue; }

  const typeName = parts[0];
  const declared = new RegExp(`\\b(?:class|struct|interface|enum)\\s+@?${typeName}\\b`);
  if (!declared.test(source)) { missing.push(`타입 선언이 없음: ${raw} (${typeName})`); continue; }
  okType++;

  // 경로형 토큰은 Assets/Scripts 기준 선언 파일과 정확히 일치해야 한다.
  // 타입만 전역에서 찾으면 이동 전 경로가 조용히 살아남아 지도의 탐색 포인터가 썩는다.
  if (raw.includes("/")) {
    const prefix = raw.split("/").slice(0, -1);
    const expected = [...prefix, typeName].join("/").replace(/^Assets\/Scripts\//, "");
    const actual = declarationFiles.get(typeName) || [];
    if (!actual.includes(expected)) {
      missing.push(`경로 불일치: ${raw} -> ${actual.join(", ") || "선언 파일 없음"}`);
      continue;
    }
  }

  // 뒤따르는 멤버는 선언 형태가 제각각이라 등장 여부만 확인한다.
  for (const member of parts.slice(1)) {
    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(member)) { skipped++; continue; }
    if (new RegExp(`\\b${member}\\b`).test(source)) { okType++; continue; }
    missing.push(`멤버가 소스에 없음: ${raw} (${member})`);
  }
  continue;
}

if (missing.length) {
  for (const m of missing) console.error("  " + m);
  assert.fail(`기능 지도에 실재하지 않는 항목 ${missing.length}건`);
}

/* 지도가 커지면 한 번에 읽는 이득이 사라진다. 약 26,000 bytes ≈ 7k 토큰이 분할 검토선이다. */
const bytes = Buffer.byteLength(map);
if (bytes > 26000) console.warn(`경고: 지도가 ${bytes} bytes 입니다. 기능 축으로 분할을 검토하세요.`);

console.log(`feature map ok: 타입/심볼 ${okType}, 디렉터리 ${okDir}, 건너뜀 ${skipped}, 소스 ${files.length}파일, 지도 ${bytes} bytes`);
