// 세이브 스키마 승급 창(MIN_WRITABLE_SCHEMA_VERSION..SCHEMA_VERSION) 순수 회귀.
// 에뮬레이터 없이 lib/ 를 직접 require 한다 — assertWritableSchema 는 Firestore 를 만지지 않는다.
//
// 이 창이 좁아지면(예: 실수로 MIN 을 SCHEMA 와 같게 되돌리면) 승급 전 클라의 모든 callable 이
// failed-precondition 으로 떨어지고, 클라 CloudFailureClassifier 가 그것을 BlockSession 으로
// 읽어 전 세션이 끊긴다. 이 파일이 그 사고를 배포 전에 잡는 자리다.
const assert = require("node:assert/strict");
const {
  assertWritableSchema,
  MIN_WRITABLE_SCHEMA_VERSION,
  SCHEMA_VERSION,
} = require("../lib/save/saveDocument.js");

/** 던진 HttpsError 를 돌려준다. 안 던지면 실패다. */
function thrownBy(version) {
  // firebase-functions logger 가 거절마다 구조화 로그 + 스택을 stderr 로 쏟아 회귀 출력을
  // 통째로 덮는다. 이 호출 동안만 막는다.
  const stderr = process.stderr.write.bind(process.stderr);
  process.stderr.write = () => true;
  try {
    assertWritableSchema(version, "test", "uid");
  } catch (error) {
    return error;
  } finally {
    process.stderr.write = stderr;
  }
  assert.fail(`v${String(version)} 는 거절되어야 한다`);
}

// ── 창의 모양 ────────────────────────────────────────────────────────────────
assert.equal(MIN_WRITABLE_SCHEMA_VERSION, 7, "승급 창의 아래끝은 구 클라가 쓰던 v7 이다");
assert.equal(SCHEMA_VERSION, 8, "지갑 이관 뒤 세이브는 v8 이다");
assert.ok(MIN_WRITABLE_SCHEMA_VERSION <= SCHEMA_VERSION, "창이 뒤집히면 아무 문서도 못 쓴다");

// ── 창 안은 통과한다 ─────────────────────────────────────────────────────────
for (let version = MIN_WRITABLE_SCHEMA_VERSION; version <= SCHEMA_VERSION; version += 1) {
  assert.doesNotThrow(() => assertWritableSchema(version, "test", "uid"),
    `v${version} 은 창 안이라 통과해야 한다`);
}

// ── 아래로 벗어나면 failed-precondition (문서가 낡음) ────────────────────────
{
  const error = thrownBy(MIN_WRITABLE_SCHEMA_VERSION - 1);
  assert.equal(error.code, "failed-precondition", "낡은 문서는 마이그레이션·재생성 대상이다");
  assert.match(error.message, /v6/, "실제값이 메시지에 남아야 원인 추적이 된다");
  assert.match(error.message, new RegExp(`v${MIN_WRITABLE_SCHEMA_VERSION}`),
    "기대값(창의 아래끝)도 함께 남는다");
}

// ── 위로 벗어나면 out-of-range (서버가 낡음) ─────────────────────────────────
{
  const error = thrownBy(SCHEMA_VERSION + 1);
  assert.equal(error.code, "out-of-range", "서버가 뒤처진 것은 재시도로 풀리지 않는다");
  assert.match(error.message, /v9/, "실제값");
  assert.match(error.message, new RegExp(`v${SCHEMA_VERSION}`), "기대값");
}

// ── 읽을 수 없는 값 ──────────────────────────────────────────────────────────
for (const [label, version] of [
  ["없음", undefined],
  ["null", null],
  ["문자열 숫자", "8"],
  ["NaN", Number.NaN],
]) {
  const error = thrownBy(version);
  assert.equal(error.code, "failed-precondition", `schemaVersion 이 ${label} 이면 쓸 수 없다`);
}

console.log("test-schema-window: ok");
