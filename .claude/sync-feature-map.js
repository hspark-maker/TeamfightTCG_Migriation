#!/usr/bin/env node
/**
 * 기능 지도의 기계 정보만 동기화한다.
 * - 사라진 타입 토큰 제거
 * - 이동한 경로형 타입 토큰 정정
 * - 사라진 디렉터리와 지도 밖 public 타입 수를 생성 블록에 기록
 * 산문과 섹션 배치는 판단 영역이므로 건드리지 않는다.
 */
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "..");
const mapFile = path.join(__dirname, "orch-feature-map.md");
const backupFile = mapFile + ".bak";
const srcRoot = path.join(root, "Assets", "Scripts");
const BLOCK_RE = /<!-- orch:feature-map-sync:start -->[\s\S]*?<!-- orch:feature-map-sync:end -->/;
const REMOVED_TOKEN = "\u0000";
const MAX_REMOVAL_RATIO = 0.05;
const today = new Date().toISOString().slice(0, 10);
const tempFiles = new Set();

function atomicWrite(file, content) {
  const temp = file + `.tmp-${process.pid}-${Date.now()}`;
  tempFiles.add(temp);
  fs.writeFileSync(temp, content, "utf8");
  fs.renameSync(temp, file);
  tempFiles.delete(temp);
}

function relative(file) {
  return path.relative(srcRoot, file).split(path.sep).join("/");
}

function scanSource() {
  const files = [];
  (function walk(dir) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.isFile() && entry.name.endsWith(".cs")) files.push(full);
    }
  })(srcRoot);

  const declarations = new Map();
  const publicTypes = new Set();
  const dirs = new Set();
  for (const file of files) {
    const rel = relative(file);
    const parts = rel.split("/");
    for (let i = 1; i < parts.length; i++) dirs.add(parts.slice(0, i).join("/") + "/");
    const text = fs.readFileSync(file, "utf8");
    // 일부러 느슨하게 찾는다. 주석의 죽은 선언을 살려둘 수 있지만 자동 오삭제보다 안전하다.
    for (const m of text.matchAll(/\b(?:class|struct|interface|enum)\s+@?([A-Z][A-Za-z0-9_]*)\b/g)) {
      const list = declarations.get(m[1]) || [];
      const noExt = rel.replace(/\.cs$/, "");
      if (!list.includes(noExt)) list.push(noExt);
      declarations.set(m[1], list);
    }
    for (const m of text.matchAll(/\bpublic\s+(?:(?:abstract|sealed|static|partial|readonly)\s+)*(?:class|struct|interface|enum)\s+@?([A-Z][A-Za-z0-9_]*)\b/g))
      publicTypes.add(m[1]);
  }
  return { files, declarations, publicTypes, dirs };
}

function tokenInfo(raw) {
  if (raw.endsWith("/")) return { kind: "dir" };
  const segments = raw.split("/");
  const last = segments.pop().replace(/\.cs$/, "");
  const parts = last.split(".").filter(Boolean);
  if (!parts.length || !/^[A-Z][A-Za-z0-9_]*$/.test(parts[0])) return { kind: "other" };
  return { kind: "type", typeName: parts[0], members: parts.slice(1), prefix: segments };
}

function chooseMovedPath(info, declarations, warnings) {
  const actual = declarations.get(info.typeName) || [];
  if (!info.prefix.length || !actual.length) return null;
  const expected = [...info.prefix, info.typeName].join("/").replace(/^Assets\/Scripts\//, "");
  if (actual.includes(expected)) return null;
  if (actual.length !== 1) {
    warnings.push(`경로 자동 정정 보류(동명 타입 ${actual.length}개): ${info.typeName}`);
    return null;
  }
  if (path.posix.basename(actual[0]) !== info.typeName) {
    warnings.push(`경로 자동 정정 보류(파일명과 타입명 불일치): ${info.typeName} -> ${actual[0]}`);
    return null;
  }
  return actual[0] + (info.members.length ? "." + info.members.join(".") : "");
}

function sync() {
  if (!fs.existsSync(mapFile) || !fs.existsSync(srcRoot)) throw new Error("기능 지도 또는 Assets/Scripts가 없습니다");
  const original = fs.readFileSync(mapFile, "utf8");
  const eol = original.includes("\r\n") ? "\r\n" : "\n";
  const source = scanSource();
  const warnings = [];
  const removed = new Set();
  const moved = new Map();
  const mappedTypes = new Set();
  const previousMissingSince = new Map(
    [...original.matchAll(/<!--\s*orch:missing-dir\s+(.+?)\s+since=(\d{4}-\d{2}-\d{2})\s*-->/g)]
      .map((m) => [m[1].trim(), m[2]])
  );

  let body = original.replace(BLOCK_RE, "");
  const originalTypeTokens = new Set(
    [...body.matchAll(/`([^`]+)`/g)]
      .map((m) => m[1].trim())
      .filter((raw) => tokenInfo(raw).kind === "type")
  );
  body = body.replace(/`([^`]+)`/g, (whole, value) => {
    const raw = value.trim();
    const info = tokenInfo(raw);
    if (info.kind !== "type") return whole;
    const declarations = source.declarations.get(info.typeName) || [];
    if (!declarations.length) { removed.add(raw); return REMOVED_TOKEN; }
    mappedTypes.add(info.typeName);
    const replacement = chooseMovedPath(info, source.declarations, warnings);
    if (!replacement) return whole;
    moved.set(raw, replacement);
    return "`" + replacement + "`";
  });

  const removalRatio = originalTypeTokens.size ? removed.size / originalTypeTokens.size : 0;
  if (removalRatio > MAX_REMOVAL_RATIO) {
    console.warn(
      `feature map sync skipped: removal guard ${removed.size}/${originalTypeTokens.size} ` +
      `(${(removalRatio * 100).toFixed(1)}%) exceeds ${MAX_REMOVAL_RATIO * 100}%`
    );
    return;
  }

  // 제거 토큰에 붙은 목록 구분자만 정리한다. 설명 문장은 수정하지 않는다.
  let emptiedBullets = 0;
  body = body.split(/\r?\n/).map((line) => {
    let cleaned = line;
    while (cleaned.includes(REMOVED_TOKEN)) {
      const before = cleaned;
      cleaned = cleaned
        .replace(new RegExp(`\\s*[·/]\\s*${REMOVED_TOKEN}`, "g"), "")
        .replace(new RegExp(`${REMOVED_TOKEN}\\s*[·/]\\s*`, "g"), "")
        .replaceAll(REMOVED_TOKEN, "");
      if (cleaned === before) break;
    }
    cleaned = cleaned.replace(/[ \t]+$/, "");
    const empty = cleaned.match(/^(\s*-\s+[^:\r\n]+:\s*)(?:<!--\s*orch:emptied\s+since=(\d{4}-\d{2}-\d{2})\s*-->)?\s*$/);
    if (empty) {
      emptiedBullets++;
      return empty[1].trimEnd() + ` <!-- orch:emptied since=${empty[2] || today} -->`;
    }
    return cleaned;
  }).filter((line) => {
    if (!line.trim()) return true;
    return !/^[\s\-*·/|,;:()[\]—–]+$/.test(line);
  }).join(eol);

  const mapTokens = [...body.matchAll(/`([^`]+)`/g)].map((m) => m[1].trim());
  const missingDirs = [...new Set(mapTokens.filter((raw) => {
    if (!raw.endsWith("/")) return false;
    const key = raw.replace(/^Assets\/Scripts\//, "");
    if (source.dirs.has(key)) return false;
    return !/^(Assets|Photon|Plugins|PurchasedAssets|AmplifyShaderEditor|GUIPackCartoon)\//.test(raw);
  }))].sort();
  const unmappedPublic = [...source.publicTypes].filter((name) => !mappedTypes.has(name)).sort();
  const generated = [
    "<!-- orch:feature-map-sync:start -->",
    `<!-- orch:source files=${source.files.length} public-types=${source.publicTypes.size} unmapped-public-types=${unmappedPublic.length} -->`,
    `<!-- orch:emptied-bullets=${emptiedBullets} -->`,
    ...missingDirs.map((dir) => `<!-- orch:missing-dir ${dir} since=${previousMissingSince.get(dir) || today} -->`),
    "<!-- orch:feature-map-sync:end -->",
  ].join(eol);
  const firstSection = body.indexOf("## ");
  const next = firstSection >= 0
    ? body.slice(0, firstSection).trimEnd() + eol + eol + generated + eol + eol + body.slice(firstSection).trimStart()
    : generated + eol + body;

  if (next === original) {
    console.log(`feature map unchanged: ${source.files.length} files, ${unmappedPublic.length} unmapped public types`);
    return;
  }
  atomicWrite(backupFile, original);
  atomicWrite(mapFile, next);
  console.log(`feature map synced: removed ${removed.size}, moved ${moved.size}, missing dirs ${missingDirs.length}, unmapped public types ${unmappedPublic.length}`);
  for (const [from, to] of moved) console.log(`  moved: ${from} -> ${to}`);
  for (const item of removed) console.log(`  removed: ${item}`);
  for (const dir of missingDirs) console.warn(`  warning: section directory missing: ${dir}`);
  for (const warning of warnings) console.warn("  warning: " + warning);
}

try {
  sync();
} catch (error) {
  console.warn("feature map sync skipped: " + String(error && error.message || error));
} finally {
  for (const temp of tempFiles) { try { fs.unlinkSync(temp); } catch { /* best effort */ } }
  process.exitCode = 0;
}
