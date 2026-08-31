// 영수증 멱등을 **실제 문서**에 대고 검증한다. 이 장치의 핵심 주장은 하나다 —
// "같은 txId 로 다시 오면 쓰기가 0회다". 그 주장은 순수 회귀로는 증명할 수 없다.
//
// 순수 회귀(test-receipt-idempotency.js)가 재는 것은 txId 파서와 캐시본 접기/펴기뿐이다.
// 트랜잭션이 정말 아무것도 쓰지 않는지 — revision·지갑 rev 가 그대로인지, 영수증이 하나로
// 유지되는지, mutate 콜백이 아예 안 도는지 — 는 에뮬레이터에서만 잴 수 있고 여기가 그 자리다.
// 이게 깨지면 재시도가 조용한 이중 과금이 되고, 알아차릴 때는 이미 실유저 잔액이 틀어져 있다.
//
// 돌리는 법:
//   npm run test:emulator
//   또는 이미 떠 있는 에뮬레이터에:
//   FIRESTORE_EMULATOR_HOST=127.0.0.1:8080 GCLOUD_PROJECT=bm-cardbattle node scripts/test-receipt-replay.js
const assert = require("node:assert/strict");

if (!process.env.FIRESTORE_EMULATOR_HOST) {
  console.error("FIRESTORE_EMULATOR_HOST 가 없다 — 이 테스트는 에뮬레이터 전용이다. 파일 머리의 사용법 참조.");
  process.exit(1);
}

const {db} = require("../lib/firebaseApp.js");
const {ensureSaveDocument, mutateSave, saveDocument} = require("../lib/save/saveDocument.js");
const {mutateWallet} = require("../lib/currency/walletTransaction.js");
const {nextWallet, walletRef} = require("../lib/currency/walletStore.js");
const {buildFreshAccountBalances, buildFreshAccountSlots} = require("../lib/save/freshAccount.js");
const {grant, spend} = require("../lib/currency/wallet.js");

const ENV = "test";
// 이 파일 전용 uid. 다른 에뮬레이터 회귀(ensure-account-test-uid)와 겹치면 남은 문서가
// 서로의 시작 상태를 흔들어 실패가 재현되지 않는다.
const UID = "receipt-replay-test-uid";
const DEVICE = "0123456789abcdef0123456789abcdef";
const APP_VERSION = "0.1.0";
const STARTER = [1, 28, 20, 6, 11, 30];
const BOOT_RECEIPT_ID = "walletCreate:freshAccount";

const save = () => saveDocument(ENV, UID);
const wallet = () => walletRef(db, ENV, UID);
const receipts = () => wallet().collection("receipts");

const revisionOf = async () => Number((await save().get()).data().revision);
const walletRevOf = async () => Number((await wallet().get()).data().rev);
const balancesOf = async () => (await wallet().get()).data().balances;
const receiptIds = async () =>
  (await receipts().listDocuments()).map((reference) => reference.id).sort();

/** 던지길 기대하는 호출. 안 던지면 그 자체가 실패다. */
async function rejectionOf(promise, what) {
  try {
    await promise;
    assert.fail(what + ": 던져야 하는데 통과했다");
  } catch (error) {
    if (error instanceof assert.AssertionError) throw error;
    return error;
  }
}

async function wipeAccount() {
  for (const reference of await receipts().listDocuments()) {
    await reference.delete();
  }
  await wallet().delete();
  await save().delete();
}

/** 세이브 슬롯만 고치는 mutate. 콜백이 몇 번 돌았는지 세는 카운터를 함께 낸다. */
function slotWriter(keys) {
  const calls = {count: 0};
  return {
    calls,
    mutate: () => {
      calls.count++;
      return {slots: {albumReward: {claimedKeys: [...keys]}}};
    },
  };
}

const finalize = (result) => ({...result, echo: "receipt-replay"});

(async () => {
  await wipeAccount();

  // ── 1. 초기화 영수증은 두 번 써도 터지지 않는다 ─────────────────────────────
  // 초기화 영수증은 create 가 아니라 set 이다. create 였다면 "지갑만 지워지고 영수증이 남은"
  // 계정이 재생성에서 ALREADY_EXISTS 로 영구 실패한다 — 그 계정은 다시는 초기화하지 못한다.
  const bootFirst = await ensureSaveDocument(
    ENV, UID, DEVICE, APP_VERSION,
    () => buildFreshAccountSlots(STARTER), buildFreshAccountBalances());
  assert.equal(bootFirst.walletCreated, true, "첫 초기화가 지갑을 세운다");
  assert.ok((await receiptIds()).includes(BOOT_RECEIPT_ID), "초기화도 영수증을 끊는다");

  // 세이브·지갑 문서만 지우고 영수증은 남긴다 — 위 함정을 정확히 재현하는 상태다.
  await wallet().delete();
  await save().delete();
  const bootAgain = await ensureSaveDocument(
    ENV, UID, DEVICE, APP_VERSION,
    () => buildFreshAccountSlots(STARTER), buildFreshAccountBalances());
  assert.equal(bootAgain.created, true, "영수증이 남아 있어도 계정을 다시 세운다");
  assert.equal(bootAgain.walletCreated, true, "지갑도 다시 선다(ALREADY_EXISTS 가 아니다)");

  // 아래 영수증 개수 단언을 0 에서 출발시키려고 초기화 영수증만 걷어낸다.
  await receipts().doc(BOOT_RECEIPT_ID).delete();
  assert.deepEqual(await receiptIds(), [], "클라 영수증 0개에서 시작한다");
  assert.equal(await revisionOf(), 1, "새 계정의 revision 은 1이다");
  assert.equal(await walletRevOf(), 1, "새 계정의 지갑 rev 는 1이다");

  // ── 2. mutateSave 첫 호출 ────────────────────────────────────────────────
  const TX_SPEND = "replay-spend-0001";
  const SOURCE = "replayTestSpend";
  const first = slotWriter(["alpha"]);
  const firstResponse = await mutateSave(
    ENV, UID, SOURCE, {kind: "client", txId: TX_SPEND},
    (current, transaction, walletState) => ({
      ...first.mutate(),
      wallet: nextWallet(walletState, spend(walletState.balances, "Gold", 30), SOURCE),
    }),
    finalize);

  assert.ok(first.calls.count >= 1, "첫 호출은 mutate 를 실제로 돈다");
  assert.equal(firstResponse.revision, 2, "callable 1회 = 세이브 revision 정확히 +1 이 계약이다");
  assert.equal(firstResponse.wallet.rev, 2, "잔액을 움직였으니 지갑 rev 도 오른다");
  assert.equal(firstResponse.wallet.balances.Gold, 70, "차감이 응답에 실린다");
  assert.equal(await revisionOf(), 2, "문서에도 반영됐다");
  assert.equal(await walletRevOf(), 2);
  assert.equal((await balancesOf()).Gold, 70);
  assert.deepEqual(await receiptIds(), [TX_SPEND], "영수증이 정확히 1개 선다");

  // ── 3. 같은 txId 재호출 = 쓰기 0회 ───────────────────────────────────────
  const replay = slotWriter(["beta"]);
  const replayResponse = await mutateSave(
    ENV, UID, SOURCE, {kind: "client", txId: TX_SPEND},
    (current, transaction, walletState) => ({
      ...replay.mutate(),
      wallet: nextWallet(walletState, spend(walletState.balances, "Gold", 30), SOURCE),
    }),
    finalize);

  // 재추첨·재차감이 없다는 것의 직접 증거다. 콜백이 한 번이라도 돌면 그 안의 난수·차감이
  // 이미 일어난 것이라, 커밋되지 않더라도 서버가 낸 결과가 갈릴 여지가 생긴다.
  assert.equal(replay.calls.count, 0, "히트는 mutate 콜백에 들어가지도 않는다");
  // 클라는 응답을 그대로 채택한다. 두 응답이 다르면 재시도한 클라만 다른 상태가 된다.
  assert.deepEqual(replayResponse, firstResponse, "재시도는 첫 응답과 완전히 같은 답을 받는다");
  // 히트에서 revision 이 오르면 클라의 "+1" 채택 단언에 걸려 세션이 끊긴다.
  assert.equal(await revisionOf(), 2, "히트는 세이브 revision 을 올리지 않는다");
  // 여기서 rev 가 오르면 그 자체가 이중 차감이다.
  assert.equal(await walletRevOf(), 2, "히트는 지갑 rev 를 올리지 않는다");
  assert.equal((await balancesOf()).Gold, 70, "히트는 잔액을 다시 깎지 않는다");
  assert.deepEqual(await receiptIds(), [TX_SPEND], "히트는 영수증도 새로 끊지 않는다");

  // ── 4. 다른 txId 는 정상 집행 ────────────────────────────────────────────
  const TX_SECOND = "replay-spend-0002";
  const second = slotWriter(["gamma"]);
  const secondResponse = await mutateSave(
    ENV, UID, SOURCE, {kind: "client", txId: TX_SECOND},
    (current, transaction, walletState) => ({
      ...second.mutate(),
      wallet: nextWallet(
        walletState, grant(walletState.balances, [{currency: "Gold", amount: 5}]), SOURCE),
    }),
    finalize);

  // 멱등 게이트가 txId 를 못 가르면 새 요청까지 캐시본을 받는다 — 그건 명령이 통째로 죽는 것이다.
  assert.equal(secondResponse.revision, 3, "새 txId 는 그대로 집행된다");
  assert.equal(await walletRevOf(), 3);
  assert.equal((await balancesOf()).Gold, 75);
  assert.deepEqual(await receiptIds(), [TX_SECOND, TX_SPEND].sort(), "영수증이 2개가 된다");

  // ── 5. 같은 txId + 다른 source = 거절, 그리고 쓰기 0회 ───────────────────
  const stolen = slotWriter(["delta"]);
  const reuse = await rejectionOf(mutateSave(
    ENV, UID, "someOtherCommand", {kind: "client", txId: TX_SPEND},
    () => stolen.mutate(), finalize), "txId 재사용");

  // permission-denied 여야 한다 — 클라 CloudFailureClassifier 는 이것만 도메인 거절로 읽는다.
  assert.equal(reuse.code, "permission-denied",
    "txId 재사용은 도메인 거절이다 (받은 코드: " + reuse.code + ")");
  // reason 접두어는 와이어 계약이다. 클라가 message 앞머리를 그대로 대조한다.
  assert.ok(reuse.message.startsWith("TxIdReused"),
    "메시지가 TxIdReused 로 시작해야 한다: " + reuse.message);
  assert.equal(stolen.calls.count, 0, "거절은 mutate 를 돌지 않는다");
  assert.equal(await revisionOf(), 3, "거절은 아무것도 쓰지 않는다");
  assert.equal(await walletRevOf(), 3);
  assert.deepEqual(await receiptIds(), [TX_SECOND, TX_SPEND].sort());

  // ── 6. 지갑을 안 쓴 명령도 영수증을 끊고, 그 재호출도 쓰기 0회 ───────────
  const TX_SLOT_ONLY = "replay-slotonly-0003";
  const SLOT_SOURCE = "replayTestSlotOnly";
  const slotOnly = slotWriter(["epsilon"]);
  const slotOnlyResponse = await mutateSave(
    ENV, UID, SLOT_SOURCE, {kind: "client", txId: TX_SLOT_ONLY},
    () => slotOnly.mutate(), finalize);

  // 세이브 쓰기가 재화 이동을 대신하는 명령이다 — 영수증이 없으면 재시도가 첫 응답과 다른 답을 낸다.
  assert.equal(slotOnlyResponse.revision, 4);
  assert.equal(slotOnlyResponse.wallet.rev, 3, "지갑을 안 건드렸으니 현재 rev 가 그대로 실린다");
  assert.equal((await receiptIds()).length, 3, "지갑을 안 쓴 명령도 영수증을 끊는다");

  const slotOnlyReplay = slotWriter(["zeta"]);
  const slotOnlyAgain = await mutateSave(
    ENV, UID, SLOT_SOURCE, {kind: "client", txId: TX_SLOT_ONLY},
    () => slotOnlyReplay.mutate(), finalize);

  assert.equal(slotOnlyReplay.calls.count, 0, "지갑 미사용 명령의 히트도 콜백에 안 들어간다");
  assert.deepEqual(slotOnlyAgain, slotOnlyResponse, "같은 응답을 그대로 돌려준다");
  assert.equal(await revisionOf(), 4, "히트는 revision 을 올리지 않는다");
  assert.equal(await walletRevOf(), 3);
  assert.equal((await receiptIds()).length, 3);

  // ── 7. revision 드리프트는 failed-precondition ───────────────────────────
  // 캐시본은 첫 시도의 revision·지갑을 싣고 updatedSlots 는 지금 문서를 싣는다. 둘이 어긋난 채
  // 나가면 섞인 상태가 클라에 채택된다. 이 갈래는 permission-denied 여선 안 된다 —
  // 클라가 도메인 거절로 오해하면 초기화를 다시 걸지 않고 어긋난 채로 진행한다.
  const walletRevBeforeDrift = await walletRevOf();
  await save().update({revision: 99});
  const drift = await rejectionOf(mutateSave(
    ENV, UID, SLOT_SOURCE, {kind: "client", txId: TX_SLOT_ONLY},
    () => ({slots: {albumReward: {claimedKeys: ["eta"]}}}), finalize), "revision 드리프트");

  assert.equal(drift.code, "failed-precondition",
    "드리프트는 세션 문제지 도메인 거절이 아니다 (받은 코드: " + drift.code + ")");
  // 거절이 트랜잭션 도중에 나므로 세 축을 다 본다 — 하나라도 남으면 반쯤 쓴 상태가 굳는다.
  assert.equal(await revisionOf(), 99, "드리프트 거절도 세이브를 쓰지 않는다");
  assert.equal(await walletRevOf(), walletRevBeforeDrift, "드리프트 거절은 지갑도 건드리지 않는다");
  assert.equal((await receiptIds()).length, 3, "드리프트 거절은 영수증을 새로 끊지 않는다");
  await save().update({revision: 4});

  // ── 8. mutateWallet 첫 호출과 재호출 ─────────────────────────────────────
  const TX_WALLET = "replay-wallet-0004";
  const WALLET_SOURCE = "replayTestWalletOnly";
  const walletCalls = {count: 0};
  const walletFinalize = (patch) => ({wallet: patch, echo: "wallet-only"});
  const walletSpend = (state) => {
    walletCalls.count++;
    return nextWallet(state, spend(state.balances, "Gold", 25), WALLET_SOURCE);
  };

  const walletFirst = await mutateWallet(
    ENV, UID, WALLET_SOURCE, {kind: "client", txId: TX_WALLET}, walletSpend, walletFinalize);

  assert.ok(walletCalls.count >= 1, "첫 호출은 mutate 를 돈다");
  assert.equal(walletFirst.wallet.rev, 4);
  assert.equal(walletFirst.wallet.balances.Gold, 50);
  assert.equal(await walletRevOf(), 4);
  assert.equal((await receiptIds()).length, 4, "지갑 전용 명령도 영수증을 끊는다");
  assert.equal(await revisionOf(), 4, "지갑 전용 명령은 세이브를 건드리지 않는다");

  walletCalls.count = 0;
  const walletReplayed = await mutateWallet(
    ENV, UID, WALLET_SOURCE, {kind: "client", txId: TX_WALLET}, walletSpend, walletFinalize);

  assert.equal(walletCalls.count, 0, "히트는 mutate 콜백에 들어가지도 않는다(재차감 없음)");
  assert.deepEqual(walletReplayed, walletFirst, "재시도는 첫 응답과 같은 답을 받는다");
  assert.equal(await walletRevOf(), 4, "히트는 지갑 rev 를 올리지 않는다");
  assert.equal((await balancesOf()).Gold, 50, "히트는 잔액을 다시 깎지 않는다");
  assert.equal((await receiptIds()).length, 4, "히트는 영수증을 새로 끊지 않는다");

  // ── 9. mutateWallet: 같은 txId + 다른 source ─────────────────────────────
  const walletReuse = await rejectionOf(mutateWallet(
    ENV, UID, "someOtherWalletCommand", {kind: "client", txId: TX_WALLET},
    (state) => nextWallet(state, state.balances, "someOtherWalletCommand"),
    walletFinalize), "지갑 txId 재사용");

  assert.equal(walletReuse.code, "permission-denied",
    "지갑 쪽 txId 재사용도 도메인 거절이다 (받은 코드: " + walletReuse.code + ")");
  assert.ok(walletReuse.message.startsWith("TxIdReused"),
    "메시지가 TxIdReused 로 시작해야 한다: " + walletReuse.message);
  assert.equal(await walletRevOf(), 4, "거절은 아무것도 쓰지 않는다");
  assert.equal((await receiptIds()).length, 4);

  await wipeAccount();
  console.log("test-receipt-replay: ok");
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
