#!/usr/bin/env node
"use strict";

/**
 * 트랜스크립트에서 "진짜 사용자 요청"을 골라내는 공용 규칙.
 *
 * 게이트(요청 단위 해제)와 리포트(요청 단위 집계)가 같은 경계를 써야 한다.
 * 한쪽만 고치면 게이트가 여는 범위와 리포트가 세는 범위가 어긋난다.
 *
 * 주입 메시지도 `type:"user"` 로 들어오기 때문에 구분자가 없다 — 문자열 휴리스틱이 유일한 수단이다.
 * 실측: 필터 없이 세면 요청이 9% 부풀었다.
 */
const fs = require("node:fs");

const NOISE_PREFIX = /^\s*<(?:task-notification|system-reminder|local-command-[^>]*|command-name|hook[^>]*)>/i;

/* 세션 꼬리만 읽는다. 1.5MB 트랜스크립트를 도구 호출마다 통째로 읽으면 게이트가 작업을 느리게 만든다. */
const TAIL_BYTES = 512 * 1024;

function textContent(content) {
  if (typeof content === "string") return content;
  if (!Array.isArray(content)) return "";
  return content.filter((part) => part && part.type === "text").map((part) => part.text || "").join("\n");
}

function isRealUser(event) {
  if (!event || event.type !== "user" || !event.message) return false;
  if (Array.isArray(event.message.content) && event.message.content.some((part) => part && part.type === "tool_result")) return false;
  const text = textContent(event.message.content).trim();
  return Boolean(text) && !NOISE_PREFIX.test(text);
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

/**
 * 마지막 사용자 요청의 식별자. 요청이 바뀌면 값이 바뀐다.
 * 트랜스크립트를 못 읽으면 null — 호출부는 세션 단위로 폴백해야 한다(작업을 막지 않는다).
 */
function lastRequestKey(transcriptPath) {
  if (!transcriptPath) return null;
  let lines;
  try { lines = readTail(transcriptPath).split(/\r?\n/); } catch { return null; }
  // 꼬리를 잘라 읽었으므로 첫 줄은 깨져 있을 수 있다. 뒤에서부터 훑는다.
  for (let index = lines.length - 1; index >= 0; index -= 1) {
    const line = lines[index];
    if (!line.trim()) continue;
    let event;
    try { event = JSON.parse(line); } catch { continue; }
    if (isRealUser(event)) return event.uuid || event.timestamp || null;
  }
  return null;
}

module.exports = { NOISE_PREFIX, isRealUser, lastRequestKey, textContent };
