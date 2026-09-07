// ensureSaveDocument 의 문서 조립·멱등을 에뮬레이터에 대고 검증한다.
//
// 순수 테스트(test-fresh-account.js)는 슬롯 10개만 본다 — 메타 5키(schemaVersion/revision/
// updatedAt/deviceId/appVersion)와 create 트랜잭션은 여기서만 커버된다. R4 에서 가장 되돌리기
// 어려운 실패가 "잘못된 첫 문서 = 그 계정의 이후 모든 저장이 영구 거부"라 자동 방어선이 필요하다.
//
// 돌리는 법:
//   npm run test:emulator
//   또는 이미 떠 있는 에뮬레이터에:
//   FIRESTORE_EMULATOR_HOST=127.0.0.1:8080 GCLOUD_PROJECT=bm-cardbattle node scripts/test-ensure-account.js
const assert = require("node:assert/strict");

if (!process.env.FIRESTORE_EMULATOR_HOST) {
  console.error("FIRESTORE_EMULATOR_HOST 가 없다 — 이 테스트는 에뮬레이터 전용이다. 파일 머리의 사용법 참조.");
  process.exit(1);
}

const {ensureSaveDocument, saveDocument, SCHEMA_VERSION} = require("../lib/save/saveDocument.js");
const {walletRef} = require("../lib/currency/walletStore.js");
const {db} = require("../lib/firebaseApp.js");
const {buildFreshAccountBalances, buildFreshAccountSlots, STARTER_GOLD, DECK_SLOT_COUNT,
  STARTER_DECK_NAME} = require("../lib/save/freshAccount.js");

const ENV = "test";
const UID = "ensure-account-test-uid";
const DEVICE = "0123456789abcdef0123456789abcdef";
const APP_VERSION = "0.1.0";
const STARTER = [1, 28, 20, 6, 11, 30];

// 룰 하네스의 14키 계약(firestore.rules 의 isValidSave)과 같은 목록이다.
// currency 는 없다 — C7 에서 잔액이 wallet/current 로 이사했고 룰이 그 필드를 금지한다.
const TOP_LEVEL_KEYS = [
  "schemaVersion", "revision", "updatedAt", "deviceId", "appVersion",
  "ownership", "deck", "cardGrowth", "keywordGrowth",
  "rank", "albumReward", "adventure", "tutorial", "profile",
].sort();

(async () => {
  const reference = saveDocument(ENV, UID);
  const walletReference = walletRef(db, ENV, UID);
  await reference.delete().catch(() => {});
  // 지갑도 같은 트랜잭션이 세우므로 함께 비운다 — 남아 있으면 walletCreated 가 false 로 나온다.
  for (const receipt of await walletReference.collection("receipts").listDocuments()) {
    await receipt.delete();
  }
  await walletReference.delete().catch(() => {});

  const first = await ensureSaveDocument(
    ENV, UID, DEVICE, APP_VERSION, () => buildFreshAccountSlots(STARTER),
    buildFreshAccountBalances());
  assert.deepEqual(
    first,
    {revision: 1, created: true, walletCreated: true, repaired: false, discardedFields: []},
    "첫 호출은 세이브와 지갑을 같은 트랜잭션에서 만든다");

  const snapshot = await reference.get();
  assert.ok(snapshot.exists, "문서가 실제로 생겼다");
  const data = snapshot.data();

  // 14키 정확히 — 하나라도 어긋나면 클라의 다음 저장이 hasOnly/hasAll 에 걸려 영구 거부된다.
  // 숫자를 손으로 적지 않는다: 이 파일이 빨갛게 방치됐던 원인이 정확히 "주석·상수가 룰 계약과 갈린 것"이다.
  assert.deepEqual(Object.keys(data).sort(), TOP_LEVEL_KEYS,
    `최상위 ${TOP_LEVEL_KEYS.length}키(firestore.rules 의 isValidSave 와 같은 목록)`);

  assert.equal(data.schemaVersion, SCHEMA_VERSION);
  assert.equal(data.revision, 1, "룰이 revision > 0 을 요구한다");
  assert.equal(data.deviceId, DEVICE);
  assert.equal(data.appVersion, APP_VERSION);
  assert.ok(data.updatedAt, "updatedAt 이 서버 시각으로 찍혔다");

  // 스타터 골드는 세이브가 아니라 지갑에 선다. 두 문서가 갈리면 그 계정은 골드를 영영 잃는다.
  assert.equal((await walletReference.get()).data().balances.Gold, STARTER_GOLD);
  assert.deepEqual(data.ownership.cardIds, STARTER);
  assert.equal(data.deck.slots.length, DECK_SLOT_COUNT);
  assert.equal(data.deck.slots[0].name, STARTER_DECK_NAME);
  assert.equal(data.tutorial.lastBootChapterIndex, -1);
  assert.equal(data.tutorial.lastBootStepIndex, -1);
  assert.equal(data.profile.nickname, null);

  // 멱등 — 문서가 있으면 쓰지 않는다. 타임아웃 후 재호출이 안전해야 한다(미결 #10).
  const second = await ensureSaveDocument(
    ENV, UID, "ffffffffffffffffffffffffffffffff", "9.9.9",
    () => buildFreshAccountSlots([99, 98, 97, 96, 95, 94]), buildFreshAccountBalances());
  assert.deepEqual(
    second,
    {revision: 1, created: false, walletCreated: false, repaired: false, discardedFields: []},
    "두 번째 호출은 만들지 않는다");

  const again = (await reference.get()).data();
  assert.equal(again.deviceId, DEVICE, "기존 문서를 덮지 않는다");
  assert.deepEqual(again.ownership.cardIds, STARTER);
  assert.equal(again.revision, 1, "revision 이 오르지 않는다");

  // 클라가 저장을 이어간 뒤에도 현재 revision 을 그대로 돌려줘야 한다(초기화 재시도 경로).
  await reference.update({revision: 2});
  const third = await ensureSaveDocument(
    ENV, UID, DEVICE, APP_VERSION, () => buildFreshAccountSlots(STARTER),
    buildFreshAccountBalances());
  assert.deepEqual(
    third,
    {revision: 2, created: false, walletCreated: false, repaired: false, discardedFields: []},
    "현재 revision 을 그대로 돌려준다");

  await reference.delete();
  for (const receipt of await walletReference.collection("receipts").listDocuments()) {
    await receipt.delete();
  }
  await walletReference.delete();
  console.log("test-ensure-account: ok");
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
