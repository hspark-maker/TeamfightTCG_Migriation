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
const {buildFreshAccountSlots, STARTER_GOLD, DECK_SLOT_COUNT,
  STARTER_DECK_NAME} = require("../lib/save/freshAccount.js");

const ENV = "test";
const UID = "ensure-account-test-uid";
const DEVICE = "0123456789abcdef0123456789abcdef";
const APP_VERSION = "0.1.0";
const STARTER = [1, 28, 20, 6, 11, 30];

// 룰 하네스의 15키 계약(firestore.rules 의 isValidSave)과 같은 목록이다.
const TOP_LEVEL_KEYS = [
  "schemaVersion", "revision", "updatedAt", "deviceId", "appVersion",
  "currency", "ownership", "deck", "cardGrowth", "keywordGrowth",
  "rank", "albumReward", "tournament", "tutorial", "profile",
].sort();

(async () => {
  const reference = saveDocument(ENV, UID);
  await reference.delete().catch(() => {});

  const first = await ensureSaveDocument(
    ENV, UID, DEVICE, APP_VERSION, () => buildFreshAccountSlots(STARTER));
  assert.deepEqual(first, {revision: 1, created: true}, "첫 호출은 문서를 만든다");

  const snapshot = await reference.get();
  assert.ok(snapshot.exists, "문서가 실제로 생겼다");
  const data = snapshot.data();

  // 15키 정확히 — 하나라도 어긋나면 클라의 다음 저장이 hasOnly/hasAll 에 걸려 영구 거부된다.
  assert.deepEqual(Object.keys(data).sort(), TOP_LEVEL_KEYS, "최상위 15키");

  assert.equal(data.schemaVersion, SCHEMA_VERSION);
  assert.equal(data.revision, 1, "룰이 revision > 0 을 요구한다");
  assert.equal(data.deviceId, DEVICE);
  assert.equal(data.appVersion, APP_VERSION);
  assert.ok(data.updatedAt, "updatedAt 이 서버 시각으로 찍혔다");

  assert.equal(data.currency.balances.Gold, STARTER_GOLD);
  assert.deepEqual(data.ownership.cardIds, STARTER);
  assert.equal(data.deck.slots.length, DECK_SLOT_COUNT);
  assert.equal(data.deck.slots[0].name, STARTER_DECK_NAME);
  assert.equal(data.tutorial.lastBootChapterIndex, -1);
  assert.equal(data.tutorial.lastBootStepIndex, -1);
  assert.equal(data.profile.nickname, null);

  // 멱등 — 문서가 있으면 쓰지 않는다. 타임아웃 후 재호출이 안전해야 한다(미결 #10).
  const second = await ensureSaveDocument(
    ENV, UID, "ffffffffffffffffffffffffffffffff", "9.9.9",
    () => buildFreshAccountSlots([99, 98, 97, 96, 95, 94]));
  assert.deepEqual(second, {revision: 1, created: false}, "두 번째 호출은 만들지 않는다");

  const again = (await reference.get()).data();
  assert.equal(again.deviceId, DEVICE, "기존 문서를 덮지 않는다");
  assert.deepEqual(again.ownership.cardIds, STARTER);
  assert.equal(again.revision, 1, "revision 이 오르지 않는다");

  // 클라가 저장을 이어간 뒤에도 현재 revision 을 그대로 돌려줘야 한다(부트 재시도 경로).
  await reference.update({revision: 2});
  const third = await ensureSaveDocument(
    ENV, UID, DEVICE, APP_VERSION, () => buildFreshAccountSlots(STARTER));
  assert.deepEqual(third, {revision: 2, created: false}, "현재 revision 을 그대로 돌려준다");

  await reference.delete();
  console.log("test-ensure-account: ok");
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
