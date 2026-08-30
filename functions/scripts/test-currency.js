// 재화 지갑 순수 회귀. 에뮬레이터 없이 lib/ 를 직접 require 한다(test-fresh-account.js 관용구).
//
// 여기서 지키는 것은 두 가지다.
//  1) 산술 결과가 항상 4키다 — v8 에서 대상이 세이브 currency 슬롯 → 지갑 문서로 옮겨갔을 뿐
//     4키 계약은 그대로다. 어긋나면 룰이 그 문서를 거부한다.
//  2) 상한·하한 클램프 — 서버는 Admin SDK 라 룰을 우회하므로 여기가 마지막 방어선이다.
const assert = require("node:assert/strict");
const {CURRENCY_KEYS, CURRENCY_MAX, parseCurrency} = require("../lib/currency/currencyKeys.js");
const {readBalances, canAfford, spend, grant} = require("../lib/currency/wallet.js");
const {nextWallet} = require("../lib/currency/walletStore.js");

const FULL = {Gold: 500, Diamond: 0, Energy: 0, Shard: 0};
const KEYS_SORTED = ["Diamond", "Energy", "Gold", "Shard"];
const EMPTY_WALLET = {rev: 0, balances: {}, paidBalances: {}};

// ── 키 목록 ──────────────────────────────────────────────────────────────────
assert.deepEqual([...CURRENCY_KEYS].sort(), KEYS_SORTED, "룰의 balances.hasOnly 와 같은 4키여야 한다");

assert.equal(parseCurrency("gOLD"), "Gold", "대소문자를 안 가린다");
assert.equal(parseCurrency("  shard "), "Shard");
assert.equal(parseCurrency("없는거"), "Gold", "못 읽으면 Gold 로 떨어진다(클라 ParseCurrency)");
for (const key of CURRENCY_KEYS) assert.equal(parseCurrency(key), key);

// ── 잔액 모양: 항상 4키 ─────────────────────────────────────────────────────
// readBalances 의 남은 호출자는 이관(currency/walletMigration) 하나다 — 세이브 슬롯을 읽는 마지막 자리라 계속 잰다.
assert.deepEqual(Object.keys(readBalances({balances: {Gold: 500, Junk: 7}})).sort(), KEYS_SORTED,
  "모르는 키는 버린다");
assert.equal(readBalances({balances: {Gold: 500}}).Gold, 500);
assert.equal(readBalances({balances: {Gold: -5}}).Gold, 0);
assert.equal(readBalances(undefined).Gold, 0, "슬롯이 없어도 4키가 선다");
assert.deepEqual(Object.keys(readBalances({balances: {Gold: "x"}})).sort(), KEYS_SORTED);
assert.equal(readBalances({balances: {Gold: "x"}}).Gold, 0, "못 읽는 값은 0");

// 부분 잔액을 넣어도 빠진 키가 0 으로 선다 — 그 모양을 세우는 출구가 nextWallet 이다.
{
  const seeded = nextWallet(EMPTY_WALLET, {Gold: 10}, "claimReward").next;
  assert.deepEqual(seeded.balances, {Gold: 10, Diamond: 0, Energy: 0, Shard: 0});
  assert.equal(seeded.rev, 1, "지갑에 실리는 순간 rev 가 오른다 — 안 오르면 뒤 쓰기가 앞 쓰기를 덮는다");
}

// ── 차감: 구 currency 슬롯에 쓰던 값이 그대로 지갑 잔액이 된다 ──────────────
{
  const paid = nextWallet({rev: 7, balances: FULL, paidBalances: {}}, spend(FULL, "Gold", 120), "openPack").next;
  assert.deepEqual(paid.balances, {Gold: 380, Diamond: 0, Energy: 0, Shard: 0});
  assert.equal(paid.rev, 8, "차감도 rev 를 올린다");
}

// 잔액보다 큰 차감은 0에서 멈춘다(음수 문서를 쓰면 룰이 이후 저장을 거부한다).
assert.equal(spend(FULL, "Gold", 900).Gold, 0);
assert.equal(spend(FULL, "Diamond", 1).Gold, 500, "다른 재화는 안 건드린다");

// ── 여력 판정 ────────────────────────────────────────────────────────────────
assert.equal(canAfford(FULL, "Gold", 500), true, "같은 값이면 낼 수 있다");
assert.equal(canAfford(FULL, "Gold", 501), false);
assert.equal(canAfford(FULL, "Diamond", 1), false, "잔액 0");
assert.equal(canAfford({}, "Gold", 0), true, "0원은 빈 지갑으로도 낼 수 있다");

// ── 지급: 다건 1회 ───────────────────────────────────────────────────────────
assert.deepEqual(grant(FULL, [{currency: "Gold", amount: 100}, {currency: "Shard", amount: 3}]),
  {Gold: 600, Diamond: 0, Energy: 0, Shard: 3});
assert.deepEqual(grant({}, [{currency: "Gold", amount: 100}]),
  {Gold: 100, Diamond: 0, Energy: 0, Shard: 0}, "빈 지갑에 지급해도 4키가 선다");
assert.equal(grant(FULL, [{currency: "Gold", amount: -50}]).Gold, 500, "grant 는 획득 전용이다");

// 상한을 넘긴 문서를 쓰면 그 계정의 이후 클라 저장이 전부 PERMISSION_DENIED 다.
assert.equal(grant({Gold: CURRENCY_MAX}, [{currency: "Gold", amount: 1}]).Gold, CURRENCY_MAX);
assert.equal(readBalances({balances: {Gold: CURRENCY_MAX + 1}}).Gold, CURRENCY_MAX);

console.log("test-currency: ok");
