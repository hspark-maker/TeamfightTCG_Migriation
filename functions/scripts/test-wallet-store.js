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
  nextWallet,
  writeReceiptOnly,
  readReceipt,
} = require("../lib/currency/walletStore.js");
const {cacheableResponse} = require("../lib/save/receiptCache.js");

const KEYS_SORTED = ["Diamond", "Energy", "Gold", "Shard"];
const NOW = "<serverTimestamp>";
const ZERO = {Gold: 0, Diamond: 0, Energy: 0, Shard: 0};

const CLIENT_RECEIPT = {kind: "client", txId: "tx-1"};
const BOOT_RECEIPT = {kind: "boot", txId: "walletCreate:migration"};

const snapshotOf = (data) => ({exists: data !== undefined, data: () => data});

function fakeTransaction() {
  const calls = [];
  return {
    calls,
    set: (ref, value) => calls.push({op: "set", path: ref.path, value}),
    create: (ref, value) => calls.push({op: "create", path: ref.path, value}),
  };
}

// 영수증은 지갑 밑의 하위 컬렉션이라, 경로를 재려면 가짜 ref 도 collection().doc() 을 알아야 한다.
const fakeRef = (path) => ({
  path,
  collection: (name) => ({doc: (id) => fakeRef(path + "/" + name + "/" + id)}),
});

const walletWrites = (tx) => tx.calls.filter((call) => call.path === "wallet");
const receiptWrite = (tx) => tx.calls.find((call) => call.path !== "wallet");

// ── 경로 ─────────────────────────────────────────────────────────────────────
{
  const paths = [];
  const db = {doc: (path) => { paths.push(path); return {path}; }};
  walletRef(db, "test", "uid-1");
  assert.deepEqual(paths, ["envs/test/users/uid-1/wallet/current"],
    "클라 FirebaseRootPath.User + /wallet/current 와 같아야 한다");
}

// ── 읽기: 문서가 없거나 깨져도 선다 ──────────────────────────────────────────
assert.deepEqual(readWallet(snapshotOf(undefined)),
  {rev: 0, balances: {Gold: 0, Diamond: 0, Energy: 0, Shard: 0}, paidBalances: {}},
  "문서가 없으면 rev 0 · 4키 0 · 유상분 없음 — 여기서 던지면 미러가 순수 계약을 잃는다");

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

// ── 읽기: 유상 사이드카 ──────────────────────────────────────────────────────
assert.deepEqual(readWallet(snapshotOf({rev: 1, balances: {Gold: 50}})).paidBalances, {},
  "paidBalances 가 없는 문서 = 전부 무상");
assert.deepEqual(readWallet(snapshotOf({rev: 1, balances: {Gold: 50}, paidBalances: "x"})).paidBalances, {},
  "깨진 paidBalances 도 전부 무상으로 읽는다");
assert.deepEqual(readWallet(snapshotOf({rev: 1, balances: {Gold: 50}, paidBalances: null})).paidBalances, {},
  "null 도 전부 무상");
assert.deepEqual(readWallet(snapshotOf({rev: 1, balances: {Gold: 50}, paidBalances: {Gold: 20}})).paidBalances,
  {Gold: 20}, "유상분은 쓰인 키만 남는다 — 4키로 채우지 않는다");
assert.deepEqual(readWallet(snapshotOf({rev: 1, balances: {Gold: 50}, paidBalances: {Gold: 80}})).paidBalances,
  {Gold: 50}, "유상분은 잔액을 넘지 못한다");
assert.deepEqual(readWallet(snapshotOf({rev: 1, balances: {Gold: 50}, paidBalances: {Junk: 9}})).paidBalances, {},
  "모르는 키는 버린다");

// ── 생성: create 라야 경합이 트랜잭션 재실행으로 드러난다 ────────────────────
{
  const tx = fakeTransaction();
  const created = createWallet(
    tx, fakeRef("wallet"), {Gold: 100}, "walletCreate:migration", BOOT_RECEIPT, undefined, NOW);

  assert.deepEqual(created,
    {rev: 1, balances: {Gold: 100, Diamond: 0, Energy: 0, Shard: 0}, paidBalances: {}},
    "이관으로 선 지갑은 전부 무상이다");
  assert.equal(tx.calls.length, 2, "지갑 1회 + 영수증 1회");

  const wallet = walletWrites(tx)[0];
  assert.equal(wallet.op, "create", "set 이면 두 초기화가 겹칠 때 잔액이 두 번 이관된다");
  assert.equal(wallet.value.schemaVersion, WALLET_SCHEMA_VERSION);
  assert.equal(wallet.value.rev, 1);
  assert.equal(wallet.value.updatedAt, NOW, "서버 시각은 호출부가 넘긴다");
  assert.deepEqual(Object.keys(wallet.value.balances).sort(), KEYS_SORTED);
  assert.deepEqual(wallet.value.paidBalances, {}, "유상분 필드가 문서에 실린다");

  const receipt = receiptWrite(tx);
  assert.equal(receipt.path, "wallet/receipts/walletCreate:migration");
  assert.equal(receipt.op, "set",
    "boot 는 set 이다 — create 면 지갑만 지워진 계정이 재생성에서 영구 실패한다");
  assert.equal(receipt.value.source, "walletCreate:migration");
  assert.deepEqual(receipt.value.before, ZERO, "개설 직전 잔액은 4키 0 이다");
  assert.deepEqual(receipt.value.changes, {Gold: 100, Diamond: 0, Energy: 0, Shard: 0});
  assert.equal(receipt.value.result, null, "result 가 undefined 면 null 로 실린다");
  assert.equal(receipt.value.storeReceipt, null, "스토어 영수증 자리는 IAP 착수 때 찬다");
}

// ── 쓰기: 지갑과 영수증이 함께 나가고 클램프가 걸린다 ────────────────────────
{
  const tx = fakeTransaction();
  // 의도한 증감(+1004)과 실제 증감(+5)이 다른 값이다 — 영수증은 상·하한에 잘린 **실제**를 적어야 한다.
  const current = {rev: 8, balances: {Gold: CURRENCY_MAX - 5, Diamond: 3}, paidBalances: {Gold: 10}};
  const state = writeWallet(
    tx, fakeRef("wallet"),
    nextWallet(current, {Gold: CURRENCY_MAX + 999, Diamond: -3}, "openPack"),
    CLIENT_RECEIPT, {opened: ["Card_A"]}, NOW);

  assert.equal(tx.calls.length, 2, "지갑 set 1회 + 영수증 1회 — 잔액만 쓰는 경로는 없다");
  assert.equal(state.rev, 9, "쓰인 상태를 그대로 돌려준다");

  const written = walletWrites(tx)[0];
  assert.equal(written.op, "set");
  assert.equal(written.value.rev, 9, "rev 는 nextWallet 이 정한다 — 단조 증가만 보장한다");
  assert.equal(written.value.balances.Gold, CURRENCY_MAX, "상한을 넘긴 값은 잘린다");
  assert.equal(written.value.balances.Diamond, 0, "하한 0");
  assert.deepEqual(Object.keys(written.value.balances).sort(), KEYS_SORTED);
  assert.deepEqual(written.value.paidBalances, {Gold: 10}, "쓰기에서도 유상 불변식이 걸린다");

  const receipt = receiptWrite(tx);
  assert.equal(receipt.path, "wallet/receipts/tx-1", "영수증은 지갑 밑 receipts/{txId} 다");
  assert.equal(receipt.op, "create",
    "client 는 create 다 — 같은 txId 가 두 번 커밋되면 재실행되어 중복 집행이 막힌다");
  assert.equal(receipt.value.txId, "tx-1");
  assert.equal(receipt.value.source, "openPack");
  assert.equal(receipt.value.rev, 9);
  assert.equal(receipt.value.result, JSON.stringify({opened: ["Card_A"]}),
    "result 는 JSON 문자열로 실린다 — 재시도가 그대로 돌려받는다");
  assert.deepEqual(receipt.value.before, {Gold: CURRENCY_MAX - 5, Diamond: 3, Energy: 0, Shard: 0});
  assert.deepEqual(receipt.value.after, {Gold: CURRENCY_MAX, Diamond: 0, Energy: 0, Shard: 0});
  assert.deepEqual(receipt.value.changes, {Gold: 5, Diamond: -3, Energy: 0, Shard: 0},
    "요청은 +999 였지만 상한에 잘려 실제는 +5 다 — 영수증은 실제를 적는다");
}

// ── nextWallet: 상태를 만드는 유일한 출구 ────────────────────────────────────
{
  const current = {rev: 4, balances: {Gold: 100, Diamond: 0, Energy: 0, Shard: 0}, paidBalances: {Gold: 30}};

  const spent = nextWallet(current, {Gold: 90}, "enhanceCard");
  assert.equal(spent.next.rev, 5, "rev 는 여기서만 오른다 — writeWallet 은 받은 값을 그대로 싣는다");
  assert.equal(spent.source, "enhanceCard", "영수증의 source 는 명령 이름이다");
  assert.deepEqual(spent.before, {Gold: 100, Diamond: 0, Energy: 0, Shard: 0});
  assert.deepEqual(spent.changes, {Gold: -10, Diamond: 0, Energy: 0, Shard: 0},
    "감소는 음수 · 무변화는 0 — 4키를 전부 싣는다");
  assert.deepEqual(spent.next.paidBalances, {Gold: 30}, "무상분(70)에서 먼저 나가므로 유상분은 그대로다");

  assert.deepEqual(nextWallet(current, {Gold: 500}, "claimReward").changes,
    {Gold: 400, Diamond: 0, Energy: 0, Shard: 0}, "지급은 양수 차분이다");
  assert.deepEqual(nextWallet(current, current.balances, "claimPayout").changes, ZERO,
    "잔액이 그대로면 4키가 전부 0 이다");

  assert.deepEqual(nextWallet(current, {Gold: 30}, "enhanceCard").next.paidBalances, {Gold: 30},
    "무상분을 다 쓴 지점까지는 유상분이 온전하다");
  assert.deepEqual(nextWallet(current, {Gold: 12}, "enhanceCard").next.paidBalances, {Gold: 12},
    "잔액이 유상분 아래로 내려가면 유상분이 따라 깎인다");
  assert.deepEqual(nextWallet(current, {Gold: 0}, "enhanceCard").next.paidBalances, {},
    "0 이 된 키는 사라진다 — 빈 맵이 전부 무상의 정규형이다");
  assert.deepEqual(nextWallet(current, {Gold: 500}, "claimReward").next.paidBalances, {Gold: 30},
    "무상 지급은 유상분을 늘리지 않는다");
  assert.deepEqual(
    nextWallet({rev: 1, balances: {}, paidBalances: {}}, {Gold: 10}, "claimReward").next.paidBalances, {},
    "유상분이 없으면 계속 빈 맵이다");
  assert.deepEqual(Object.keys(spent.next.balances).sort(), KEYS_SORTED, "잔액은 4키로 정규화된다");

  assert.deepEqual(current.paidBalances, {Gold: 30}, "입력 상태를 건드리지 않는다");
}

// ── 영수증만: 지갑을 쓰지 않은 트랜잭션도 낙인을 남긴다 ──────────────────────
{
  const tx = fakeTransaction();
  writeReceiptOnly(tx, fakeRef("wallet"), "claimPayout",
    {rev: 7, balances: {Gold: 380}, paidBalances: {}}, CLIENT_RECEIPT, {acked: []}, NOW);

  assert.equal(walletWrites(tx).length, 0, "잔액을 건드리지 않는 경로라 지갑 쓰기가 없어야 한다");
  assert.equal(tx.calls.length, 1);

  const receipt = receiptWrite(tx);
  assert.equal(receipt.path, "wallet/receipts/tx-1");
  // mutateSave 는 명령 이름을 받아 이 자리에 넘긴다 — C8-2 가 이 값을 재시도 판정에 쓴다.
  assert.equal(receipt.value.source, "claimPayout", "호출부가 넘긴 명령 이름이 그대로 실린다");
  assert.equal(receipt.value.rev, 7, "지갑 rev 는 오르지 않는다");
  assert.equal(receipt.value.txId, "tx-1");
  assert.deepEqual(receipt.value.changes, ZERO, "움직인 것이 없으므로 4키가 전부 0 이다");
  assert.deepEqual(receipt.value.before, receipt.value.after);
  assert.equal(receipt.value.result, JSON.stringify({acked: []}));
}

// ── 영수증 조회: 미스 · 히트 · 깨진 result ───────────────────────────────────
assert.deepEqual(readReceipt(snapshotOf(undefined)), {hit: false},
  "문서가 없으면 아직 처리되지 않은 요청이다");

assert.deepEqual(
  readReceipt(snapshotOf({source: "openPack", result: JSON.stringify({opened: ["Card_A"]})})),
  {hit: true, source: "openPack", result: {opened: ["Card_A"]}},
  "히트면 기록된 응답을 그대로 돌려준다");

assert.deepEqual(readReceipt(snapshotOf({source: "mutateSave", result: null})),
  {hit: true, source: "mutateSave", result: null},
  "응답이 없는 명령도 히트는 히트다");

assert.throws(() => readReceipt(snapshotOf({source: "openPack", result: "{not json"})),
  "깨진 result 는 던진다 — 미스로 강등하면 재집행이 열려 이중 과금이 된다");

// ── 캐시본의 모양: 슬롯 **값**은 영수증에 실리지 않는다 ───────────────
// mutateSave 가 영수증에 넣는 것은 응답 그대로가 아니라 updatedSlots 를 빼고 slotKeys 만
// 남긴 캐시본이다. openPack 의 ownership 은 슬롯 전체 값이라 계정이 자랄수록 커지고,
// 영수증이 1MiB 상한을 치면 트랜잭션이 통째로 실패해 정상 명령이 죽는다.
{
  const response = {
    revision: 12,
    updatedSlots: {ownership: {ownedIds: [1, 2, 3]}},
    wallet: {rev: 9, balances: {Gold: 100, Diamond: 0, Energy: 0, Shard: 0}},
    packId: "Pack_Basic",
  };
  const cached = cacheableResponse(response, response.updatedSlots);

  const tx = fakeTransaction();
  writeReceiptOnly(tx, fakeRef("wallet"), "openPack",
    {rev: 9, balances: {Gold: 100}, paidBalances: {}}, CLIENT_RECEIPT, cached, NOW);

  const stored = receiptWrite(tx).value.result;
  assert.equal(stored.includes("ownedIds"), false, "슬롯 값은 영수증에 실리지 않는다");
  assert.equal(stored.includes("updatedSlots"), false,
    "JSON.stringify 가 undefined 필드를 버린다 — 키 자체가 남지 않는다");

  const replayed = readReceipt(snapshotOf({source: "openPack", result: stored}));
  assert.equal(replayed.hit, true);
  assert.deepEqual(replayed.result.slotKeys, ["ownership"],
    "재시도는 슬롯 이름만 받아 현재 세이브 문서에서 값을 다시 짓는다");
  assert.equal(replayed.result.revision, 12, "revision 은 첫 시도 때의 값이다");
  assert.equal(replayed.result.packId, "Pack_Basic", "명령별 필드도 그대로 되살아난다");
  assert.equal("updatedSlots" in replayed.result, false);
}

console.log("test-wallet-store: ok");
