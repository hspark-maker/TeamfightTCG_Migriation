// 지갑 문서 코덱 순수 회귀. 에뮬레이터 없이 lib/ 를 직접 require 한다(test-currency.js 관용구).
//
// walletStore 는 functions-currency 로 미러되는 파일이라 firebase-admin 을 **런타임으로** 들이면
// 안 된다(타입만 import 한다). 이 스크립트가 그 계약의 집행 지점이다 — 여기서 require 가 되면
// 미러 쪽 순수 회귀도 산다. Firestore 접점은 전부 인자로 받으므로 가짜 스냅샷·트랜잭션으로 잰다.
const assert = require("node:assert/strict");
const {CURRENCY_MAX} = require("../lib/currency/currencyKeys.js");
const {
  WALLET_SCHEMA_VERSION,
  walletRef,
  readWallet,
  writeWallet,
  createWallet,
  ledgerEntry,
} = require("../lib/currency/walletStore.js");

const KEYS_SORTED = ["Diamond", "Energy", "Gold", "Shard"];
const NOW = "<serverTimestamp>";

const snapshotOf = (data) => ({exists: data !== undefined, data: () => data});

function fakeTransaction() {
  const calls = [];
  return {
    calls,
    set: (ref, value) => calls.push({op: "set", ref, value}),
    create: (ref, value) => calls.push({op: "create", ref, value}),
  };
}

// ── 경로 ─────────────────────────────────────────────────────────────────────
{
  const paths = [];
  const db = {doc: (path) => { paths.push(path); return {path}; }};
  walletRef(db, "test", "uid-1");
  assert.deepEqual(paths, ["envs/test/users/uid-1/wallet/current"],
    "클라 FirebaseRootPath.User + /wallet/current 와 같아야 한다");
}

// ── 읽기: 문서가 없거나 깨져도 선다 ──────────────────────────────────────────
assert.deepEqual(readWallet(snapshotOf(undefined)), {rev: 0, balances: {Gold: 0, Diamond: 0, Energy: 0, Shard: 0}},
  "문서가 없으면 rev 0 · 4키 0 — 여기서 던지면 미러가 순수 계약을 잃는다");

assert.deepEqual(Object.keys(readWallet(snapshotOf({rev: 3, balances: {Gold: 5, Junk: 7}})).balances).sort(),
  KEYS_SORTED, "모르는 키는 버린다");

assert.equal(readWallet(snapshotOf({rev: 3, balances: {Gold: 5}})).rev, 3);
assert.equal(readWallet(snapshotOf({rev: 3, balances: {Gold: 5}})).balances.Gold, 5);
assert.equal(readWallet(snapshotOf({balances: {}})).rev, 0, "rev 가 없으면 0");
assert.equal(readWallet(snapshotOf({rev: -1, balances: {}})).rev, 0, "음수 rev 는 0");
assert.equal(readWallet(snapshotOf({rev: "x", balances: {}})).rev, 0, "못 읽는 rev 는 0");
assert.equal(readWallet(snapshotOf({rev: 2.7, balances: {}})).rev, 2, "rev 는 정수로 자른다");
assert.equal(readWallet(snapshotOf({rev: 1, balances: {Gold: -5}})).balances.Gold, 0, "음수 잔액은 0");
assert.equal(readWallet(snapshotOf({rev: 1, balances: {Gold: "x"}})).balances.Gold, 0, "못 읽는 잔액은 0");
assert.equal(readWallet(snapshotOf({rev: 1})).balances.Gold, 0, "balances 가 없어도 4키가 선다");

// ── 생성: create 라야 경합이 트랜잭션 재실행으로 드러난다 ────────────────────
{
  const tx = fakeTransaction();
  const ref = {path: "wallet"};
  const created = createWallet(tx, ref, {Gold: 100}, NOW);

  assert.deepEqual(created, {rev: 1, balances: {Gold: 100, Diamond: 0, Energy: 0, Shard: 0}});
  assert.equal(tx.calls.length, 1);
  assert.equal(tx.calls[0].op, "create", "set 이면 두 부트가 겹칠 때 잔액이 두 번 이관된다");
  assert.equal(tx.calls[0].value.schemaVersion, WALLET_SCHEMA_VERSION);
  assert.equal(tx.calls[0].value.rev, 1);
  assert.equal(tx.calls[0].value.updatedAt, NOW, "서버 시각은 호출부가 넘긴다");
  assert.deepEqual(Object.keys(tx.calls[0].value.balances).sort(), KEYS_SORTED);
}

// ── 쓰기: 클램프가 걸린다 ────────────────────────────────────────────────────
{
  const tx = fakeTransaction();
  writeWallet(tx, {path: "wallet"}, {rev: 9, balances: {Gold: CURRENCY_MAX + 1000, Diamond: -3}}, NOW);

  const written = tx.calls[0].value;
  assert.equal(tx.calls[0].op, "set");
  assert.equal(written.rev, 9, "rev 는 호출부가 정한다 — 단조 증가만 보장한다");
  assert.equal(written.balances.Gold, CURRENCY_MAX, "상한을 넘긴 값은 잘린다");
  assert.equal(written.balances.Diamond, 0, "하한 0");
  assert.deepEqual(Object.keys(written.balances).sort(), KEYS_SORTED);
}

// ── 원장: 아직 호출자가 없다(IAP 자리) ───────────────────────────────────────
{
  const entry = ledgerEntry("openPack", [{currency: "Gold", amount: -110}, {currency: "Gold", amount: -10}],
    {Gold: 380}, 7, NOW);

  assert.deepEqual(entry.changes, {Gold: -120}, "같은 재화의 증감은 합쳐진다");
  assert.equal(entry.source, "openPack");
  assert.equal(entry.rev, 7);
  assert.equal(entry.createdAt, NOW);
  assert.equal(entry.receipt, null, "영수증 자리는 비어 있다 — IAP 착수 때 찬다");
  assert.deepEqual(Object.keys(entry.after).sort(), KEYS_SORTED);
}

console.log("test-wallet-store: ok");
