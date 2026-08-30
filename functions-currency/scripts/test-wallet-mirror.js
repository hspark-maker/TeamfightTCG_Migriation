// 미러(src/generated) 순수 회귀. 에뮬레이터 없이 lib/ 를 직접 require 한다(functions/scripts 관용구).
//
// 이 codebase 는 devGrantCurrency 부터 **실제 지갑을 쓴다**. 그 쓰기가 성립하려면 미러가
// 두 가지를 지켜야 하고, 여기가 그 집행 지점이다.
//  1) 미러는 순수하다 — firebase-admin·firebase-functions 를 런타임으로 들이지 않는다.
//     들이는 순간 codebase 마다 앱 인스턴스가 다른 제약을 어겨 배포본이 죽는다.
//  2) 화이트리스트(환경·재화 키)가 default 와 같다 — 갈리면 같은 uid 의 지갑을
//     codebase 마다 다르게 거절한다.
const assert = require("node:assert/strict");

const {ENVIRONMENTS, isKnownEnv} = require("../lib/generated/save/environments.js");
const {CURRENCY_KEYS} = require("../lib/generated/currency/currencyKeys.js");
const {grant} = require("../lib/generated/currency/wallet.js");
const {nextWallet} = require("../lib/generated/currency/walletStore.js");

// ── 미러 순수성 ──────────────────────────────────────────────────────────────
// 위 require 4줄이 전부 끝난 시점에 Firebase 모듈이 하나도 적재돼 있으면 안 된다.
const loaded = Object.keys(require.cache).filter((path) =>
  path.includes("firebase-admin") || path.includes("firebase-functions"));
assert.deepEqual(loaded, [],
  `미러가 Firebase 모듈을 런타임으로 들였다: ${loaded.join(", ")}`);

// ── 화이트리스트 ─────────────────────────────────────────────────────────────
assert.deepEqual([...ENVIRONMENTS].sort(), ["live", "test"], "default 의 환경 목록과 같아야 한다");
assert.equal(isKnownEnv("test"), true);
assert.equal(isKnownEnv("live"), true);
assert.equal(isKnownEnv("stage"), false, "모르는 환경은 지갑을 열지 못한다");

assert.deepEqual([...CURRENCY_KEYS].sort(), ["Diamond", "Energy", "Gold", "Shard"],
  "룰의 balances.hasOnly 와 같은 4키여야 한다");

// ── devGrantCurrency 가 밟는 합성 ────────────────────────────────────────────
// 명령 본문이 하는 일은 grant → nextWallet 두 줄이 전부다. 그 결과가 응답 {rev, balances} 다.
{
  const empty = {rev: 0, balances: {}, paidBalances: {}};
  const next = nextWallet(empty, grant(empty.balances, [{currency: "Gold", amount: 500}]), "devGrantCurrency").next;

  assert.equal(next.rev, 1, "지갑 rev 는 쓰기마다 오른다");
  assert.deepEqual(Object.keys(next.balances).sort(), ["Diamond", "Energy", "Gold", "Shard"],
    "지급 뒤에도 항상 4키다");
  assert.equal(next.balances.Gold, 500);
  assert.deepEqual(next.paidBalances, {}, "디버그 지급은 무상분이다");
}

// 두 번째 지급이 쌓이는지 — 클라 디버그 오버레이가 연타하는 경로다.
{
  const current = {rev: 7, balances: {Gold: 500, Diamond: 0, Energy: 0, Shard: 0}, paidBalances: {}};
  const next = nextWallet(current, grant(current.balances, [{currency: "Diamond", amount: 3}]), "devGrantCurrency").next;

  assert.equal(next.rev, 8);
  assert.equal(next.balances.Gold, 500, "다른 재화는 그대로다");
  assert.equal(next.balances.Diamond, 3);
}

console.log("test-wallet-mirror: ok");
