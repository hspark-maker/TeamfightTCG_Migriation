#!/usr/bin/env node
"use strict";

/** 지도의 백틱 토큰을 한 규칙으로 해석한다. */
function mapTokens(mapText) {
  return [...new Set([...String(mapText).matchAll(/`([^`]+)`/g)].map((match) => match[1].trim()))];
}

/**
 * `A.B`는 A와 B를 모두 수록 심볼로 인정한다.
 * 중첩 타입과 멤버를 문법만으로 구분할 수 없고, 지도의 목적은 검색 적중이기 때문이다.
 */
function mapSymbols(mapText) {
  const types = new Set();
  const dirs = new Set();
  const members = new Map();

  for (const raw of mapTokens(mapText)) {
    if (raw.endsWith("/")) {
      dirs.add(raw);
      continue;
    }
    const last = raw.split("/").pop().replace(/\.cs(?=\.|$)/, "");
    const parts = last.split(".").filter((part) => /^[A-Z][A-Za-z0-9_]*$/.test(part));
    if (!parts.length) continue;
    for (const part of parts) types.add(part);
    if (parts.length > 1) {
      const known = members.get(parts[0]) || new Set();
      for (const member of parts.slice(1)) known.add(member);
      members.set(parts[0], known);
    }
  }

  return { types, dirs, members };
}

module.exports = { mapSymbols, mapTokens };
