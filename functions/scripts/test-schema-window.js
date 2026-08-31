// 세이브 스키마 판정 순수 회귀. 축이 둘이고, 둘은 일부러 다르다.
//   mutateSave  (assertWritableSchema)     — 정확히 SCHEMA_VERSION 만 쓴다
//   ensureWallet(assertMigratableSchema)   — 승급 창 [SCHEMA_VERSION-1, SCHEMA_VERSION]
// 에뮬레이터 없이 lib/ 를 직접 require 한다 — 두 판정 모두 Firestore 를 만지지 않는다.
//
// 여기서 창을 잘못 넓히면(예: mutateSave 가 v7 을 통과시키면) 지갑을 모르는 구 클라가
// 잔액을 바꾸는 명령에 성공한다. 그 순간 클라 잔액이 갈리고, 뒤이은 업로드가 낮은
// schemaVersion 을 실어 룰에 영구 거부되어 그 세션의 진행이 통째로 유실된다.
// 반대로 ensureWallet 이 v7 을 막으면 승급 자체가 불가능해져 v7 계정이 영영 굳는다.
const assert = require("node:assert/strict");
const {
  assertWritableSchema,
  SCHEMA_VERSION,
} = require("../lib/save/saveDocument.js");
const {
  assertMigratableSchema,
  MIGRATABLE_SCHEMA_VERSION,
} = require("../lib/commands/ensureWallet.js");
const {migrateFromSaveSlot} = require("../lib/currency/walletMigration.js");

/** 던진 HttpsError 를 돌려준다. 안 던지면 실패다. */
function thrownBy(assertFn, version) {
  // firebase-functions logger 가 거절마다 구조화 로그 + 스택을 stderr 로 쏟아 회귀 출력을
  // 통째로 덮는다. 이 호출 동안만 막는다.
  const stderr = process.stderr.write.bind(process.stderr);
  process.stderr.write = () => true;
  try {
    assertFn(version, "test", "uid");
  } catch (error) {
    return error;
  } finally {
    process.stderr.write = stderr;
  }
  assert.fail(`v${String(version)} 는 거절되어야 한다`);
}

const UNREADABLE = [
  ["없음", undefined],
  ["null", null],
  ["문자열 숫자", "8"],
  ["NaN", Number.NaN],
];

// ── 두 축의 기준점 ───────────────────────────────────────────────────────────
assert.equal(SCHEMA_VERSION, 8, "지갑 이관 뒤 세이브는 v8 이다");
assert.equal(MIGRATABLE_SCHEMA_VERSION, 7, "승급 대상은 이관 직전 판인 v7 이다");
assert.equal(MIGRATABLE_SCHEMA_VERSION, SCHEMA_VERSION - 1,
  "승급 창은 딱 한 판 아래까지다 — 더 벌리면 두 판 건너뛴 문서가 조용히 통과한다");

// ── mutateSave 축: 정확히 SCHEMA_VERSION ─────────────────────────────────────
assert.doesNotThrow(() => assertWritableSchema(SCHEMA_VERSION, "test", "uid"),
  "서버와 같은 판만 쓴다");

{
  // v7 은 **거절이 옳다**. 지갑을 모르는 클라를 살려 두면 잔액이 갈린 채로 굳는다.
  const error = thrownBy(assertWritableSchema, SCHEMA_VERSION - 1);
  assert.equal(error.code, "failed-precondition", "낡은 문서는 승급(ensureWallet) 대상이다");
  assert.match(error.message, /v7/, "실제값이 메시지에 남아야 원인 추적이 된다");
  assert.match(error.message, new RegExp(`v${SCHEMA_VERSION}`), "기대값도 함께 남는다");
}

{
  const error = thrownBy(assertWritableSchema, SCHEMA_VERSION + 1);
  assert.equal(error.code, "out-of-range", "서버가 뒤처진 것은 재시도로 풀리지 않는다");
  assert.match(error.message, /v9/, "실제값");
  assert.match(error.message, new RegExp(`v${SCHEMA_VERSION}`), "기대값");
}

for (const [label, version] of UNREADABLE) {
  const error = thrownBy(assertWritableSchema, version);
  assert.equal(error.code, "failed-precondition", `schemaVersion 이 ${label} 이면 쓸 수 없다`);
}

// ── ensureWallet 축: 승급 창 7..8 ────────────────────────────────────────────
assert.doesNotThrow(() => assertMigratableSchema(MIGRATABLE_SCHEMA_VERSION, "test", "uid"),
  "v7 은 이 명령이 존재하는 이유다 — 여기서 막으면 승급 경로가 없다");
assert.doesNotThrow(() => assertMigratableSchema(SCHEMA_VERSION, "test", "uid"),
  "v8 은 지갑만 세우는 멱등 경로라 통과한다");

{
  const error = thrownBy(assertMigratableSchema, MIGRATABLE_SCHEMA_VERSION - 1);
  assert.equal(error.code, "failed-precondition", "두 판 아래는 승급 대상이 아니다");
  assert.match(error.message, /v6/, "실제값");
  assert.match(error.message, new RegExp(`v${MIGRATABLE_SCHEMA_VERSION}`), "기대값(창의 아래끝)");
}

{
  const error = thrownBy(assertMigratableSchema, SCHEMA_VERSION + 1);
  assert.equal(error.code, "out-of-range", "서버가 뒤처진 것은 승급으로도 안 풀린다");
  assert.match(error.message, /v9/, "실제값");
  assert.match(error.message, new RegExp(`v${SCHEMA_VERSION}`), "기대값");
}

for (const [label, version] of UNREADABLE) {
  const error = thrownBy(assertMigratableSchema, version);
  assert.equal(error.code, "failed-precondition", `schemaVersion 이 ${label} 이면 승급할 수 없다`);
}

// ── 승급 후에는 반드시 v8 ────────────────────────────────────────────────────
// 창을 통과한 문서가 v7 이든 v8 이든, ensureWallet 이 얹는 패치는 항상 SCHEMA_VERSION 이다.
// 여기가 어긋나면 승급을 마친 문서가 그대로 mutateSave 의 거절 대상이 된다.
for (const before of [MIGRATABLE_SCHEMA_VERSION, SCHEMA_VERSION]) {
  const patch = migrateFromSaveSlot(
    {schemaVersion: before, currency: {balances: {Gold: 3}}}, "DELETE", SCHEMA_VERSION).slotPatch;
  assert.equal(patch.schemaVersion, SCHEMA_VERSION, `v${before} 문서는 승급 후 v8 이어야 한다`);
  assert.doesNotThrow(() => assertWritableSchema(patch.schemaVersion, "test", "uid"),
    "승급을 마친 문서는 곧바로 mutateSave 가 쓸 수 있어야 한다");
}

console.log("test-schema-window: ok");
