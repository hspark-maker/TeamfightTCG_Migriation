// 세이브 currency 슬롯 → 지갑 이관 계산 순수 회귀. 에뮬레이터 없이 lib/ 를 직접 require 한다.
//
// walletMigration 은 firebase-admin 을 모르는 것이 계약이다(삭제 센티널·스키마 버전을 인자로 받는다).
// 이 스크립트에서 require 가 되면 그 계약이 살아 있다는 뜻이다.
const assert = require("node:assert/strict");
const {migrateFromSaveSlot} = require("../lib/currency/walletMigration.js");

// 호출부가 FieldValue.delete() 를 넘기는 자리. 값의 정체는 이 모듈의 관심이 아니다.
const DELETE = "<FieldValue.delete()>";
const SCHEMA = 8;
const ZERO = {Gold: 0, Diamond: 0, Energy: 0, Shard: 0};

// ── 잔액 보존 ────────────────────────────────────────────────────────────────
{
  const result = migrateFromSaveSlot(
    {schemaVersion: 7, currency: {balances: {Gold: 1200, Diamond: 30, Energy: 5, Shard: 2}}},
    DELETE, SCHEMA);

  assert.deepEqual(result.balances, {Gold: 1200, Diamond: 30, Energy: 5, Shard: 2},
    "이관은 잔액을 한 푼도 바꾸지 않는다");
  assert.deepEqual(result.slotPatch, {currency: DELETE, schemaVersion: SCHEMA},
    "세이브 쪽은 currency 삭제 + 스키마 승급 둘뿐이다");
}

// ── 멱등: 이미 이관된 문서를 다시 넣어도 안전하다 ────────────────────────────
{
  const migrated = {schemaVersion: SCHEMA};
  const first = migrateFromSaveSlot(migrated, DELETE, SCHEMA);
  const second = migrateFromSaveSlot(migrated, DELETE, SCHEMA);

  assert.deepEqual(first, second, "두 번 돌려도 같은 값이다");
  assert.deepEqual(first.balances, ZERO,
    "이관 뒤에는 0 이 나온다 — 지갑이 두 번 서지 않게 막는 것은 createWallet 의 create 다");
  assert.deepEqual(first.slotPatch, {currency: DELETE, schemaVersion: SCHEMA},
    "이미 없는 필드를 지우는 것은 무해하고, 스키마는 같은 값으로 다시 쓰인다");
}

// ── currency 슬롯이 없거나 깨져도 선다 ──────────────────────────────────────
for (const [label, slot] of [
  ["슬롯 없음", undefined],
  ["null", null],
  ["문자열", "gold"],
  ["balances 없음", {}],
  ["balances 가 문자열", {balances: "x"}],
  ["못 읽는 값", {balances: {Gold: "x", Diamond: null}}],
]) {
  const result = migrateFromSaveSlot(slot === undefined ? {} : {currency: slot}, DELETE, SCHEMA);
  assert.deepEqual(result.balances, ZERO, `${label} 이면 4키 0 으로 선다`);
}

// ── 모르는 키는 버리고 빠진 키는 0 으로 채운다(룰 hasOnly 대응) ─────────────
{
  const result = migrateFromSaveSlot({currency: {balances: {Gold: 7, Junk: 999}}}, DELETE, SCHEMA);

  assert.deepEqual(Object.keys(result.balances).sort(), ["Diamond", "Energy", "Gold", "Shard"],
    "지갑 문서도 정확히 4키여야 한다");
  assert.equal(result.balances.Gold, 7);
  assert.equal(result.balances.Diamond, 0, "빠진 키는 0");
}

// ── 음수·소수는 잔액 규칙대로 잘린다 ────────────────────────────────────────
{
  const result = migrateFromSaveSlot({currency: {balances: {Gold: -5, Diamond: 3.9}}}, DELETE, SCHEMA);
  assert.equal(result.balances.Gold, 0, "음수 잔액은 0");
  assert.equal(result.balances.Diamond, 3, "소수는 잘린다");
}

// ── 스키마 승급값은 인자다 — 이 모듈이 상수로 갖지 않는다 ───────────────────
assert.equal(migrateFromSaveSlot({}, DELETE, 99).slotPatch.schemaVersion, 99,
  "SCHEMA_VERSION 의 진실원은 save/saveDocument 하나여야 한다");

console.log("test-wallet-migration: ok");
