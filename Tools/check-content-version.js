"use strict";

// content-version.json 을 선언 진실원으로 두고 C#·TS 테이블 세대 상수가 그것과 같은지 대조한다.
// 앱 빌드 버전(bundleVersion)은 보지 않는다 — 테이블 세대와 묶여 있지 않다.
// 앵커 주석을 못 찾으면 통과가 아니라 실패다 — 포맷이 바뀌었는데 조용히 새면 게이트가 없는 것과 같다.

const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const manifest = JSON.parse(fs.readFileSync(path.join(root, "content-version.json"), "utf8"));
const csharp = fs.readFileSync(path.join(root, "Assets/Scripts/OutGame/Spec/ContentVersion.cs"), "utf8");
const typescript = fs.readFileSync(path.join(root, "functions/src/specs/specBlobReader.ts"), "utf8");

function requiredMatch(source, pattern, label) {
  const match = source.match(pattern);
  if (!match) throw new Error(`content version anchor not found: ${label}`);
  return match[1];
}

// `{ Major, 3 }` / `[CONTENT_MAJOR, 3]` 처럼 목록이 늘어나도 읽는다 —
// 단일 리터럴만 매치하면 롤백 지원 빌드에서 "anchor not found" 로 죽는다.
function parseMajorList(listText, aliasName, aliasValue, label) {
  const values = listText.split(",").map(token => token.trim()).filter(token => token.length > 0);
  if (values.length === 0) throw new Error(`content version list is empty: ${label}`);
  return values.map(token => {
    if (token === aliasName) return aliasValue;
    if (!/^\d+$/.test(token)) throw new Error(`content version list has an unreadable entry: ${label} '${token}'`);
    return Number(token);
  });
}

function sameSet(left, right) {
  if (left.length !== right.length) return false;
  const sortedLeft = [...left].sort((a, b) => a - b);
  const sortedRight = [...right].sort((a, b) => a - b);
  return sortedLeft.every((value, index) => value === sortedRight[index]);
}

const csharpMajor = Number(requiredMatch(
  csharp, /content-version:major\s*[\r\n]+\s*public const int Major = (\d+);/, "C# major"));
const csharpMinAppMajor = Number(requiredMatch(
  csharp, /content-version:min-app-major\s*[\r\n]+\s*public const int MinAppMajor = (\d+);/, "C# minAppMajor"));
const typescriptMajor = Number(requiredMatch(
  typescript, /content-version:major\s*[\r\n]+\s*const CONTENT_MAJOR = (\d+);/, "TypeScript major"));

const csharpSupported = parseMajorList(requiredMatch(
  csharp,
  /content-version:supported\s*[\r\n]+\s*static readonly int\[\] SupportedMajors = \{([^}]*)\};/,
  "C# supported"), "Major", csharpMajor, "C# supported");
const typescriptSupported = parseMajorList(requiredMatch(
  typescript,
  /content-version:supported\s*[\r\n]+\s*const SUPPORTED_CONTENT_MAJORS = new Set<number>\(\[([^\]]*)\]\);/,
  "TypeScript supported"), "CONTENT_MAJOR", typescriptMajor, "TypeScript supported");

if (!Number.isInteger(manifest.major) || !Number.isInteger(manifest.minAppMajor) ||
    !Array.isArray(manifest.supported) || manifest.supported.length === 0 ||
    manifest.supported.some(value => !Number.isInteger(value))) {
  throw new Error("content-version.json has an invalid shape");
}
// 현재 세대를 자기 목록에 안 넣으면 클라가 방금 올린 테이블을 못 읽는다.
if (!manifest.supported.includes(manifest.major)) {
  throw new Error(`content-version.json supported must contain major ${manifest.major}`);
}
// 요구 세대가 현재 세대보다 높으면 서버가 자기 테이블을 자기 앱에서 거절한다.
if (manifest.minAppMajor > manifest.major) {
  throw new Error(`content-version.json minAppMajor ${manifest.minAppMajor} exceeds major ${manifest.major}`);
}
if (manifest.major !== csharpMajor || manifest.major !== typescriptMajor ||
    manifest.minAppMajor !== csharpMinAppMajor ||
    !sameSet(manifest.supported, csharpSupported) ||
    !sameSet(manifest.supported, typescriptSupported)) {
  throw new Error(
    `content version mismatch: manifest=${JSON.stringify(manifest)}` +
    ` C#=${csharpMajor}/${csharpMinAppMajor}/[${csharpSupported}]` +
    ` TS=${typescriptMajor}/[${typescriptSupported}]`);
}

process.stdout.write(`table generation ${manifest.major} is consistent\n`);
