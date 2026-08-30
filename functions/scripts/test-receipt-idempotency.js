// 영수증 멱등 게이트의 순수 회귀. 에뮬레이터 없이 lib/ 를 직접 require 한다(test-wallet-store.js 관용구).
//
// 재는 것은 두 축이다.
//  1) 요청 txId 파서 — **절대 던지지 않는다.** invalid-argument 로 거절하면 클라
//     CloudFailureClassifier 가 Unusable → BlockSession 으로 읽어 세션이 끊기고,
//     txId 를 안 보내는 구 클라가 그 자리에서 죽는다(배포 순서 제약이 생긴다).
//  2) 영수증 히트의 source 대조 — 같은 txId 를 다른 명령이 재사용하면 첫 명령의 응답이
//     엉뚱한 명령에 흘러간다. 그 판정에 쓰는 값이 readReceipt 가 내는 source 다.
const assert = require("node:assert/strict");

const {clientReceiptId, isClientReceiptId} = require("../lib/save/receiptId.js");
const {readReceipt} = require("../lib/currency/walletStore.js");
const {cacheableResponse, replayCached} = require("../lib/save/receiptCache.js");

const FALLBACK = "server-issued-uuid";
const snapshotOf = (data) => ({exists: data !== undefined, data: () => data});

// ── 통과하는 모양 ────────────────────────────────────────────────────────────
const ACCEPTED = [
  "abcdefgh",                                       // 하한 8자
  "01234567",
  "9f8c1d2e-4a5b-6c7d-8e9f-0a1b2c3d4e5f",           // randomUUID 도 이 형식을 통과한다
  "openPack:9f8c1d2e4a5b",                          // 콜론 — 명령별 접두어 관용구
  "boot_retry-3",
  "A".repeat(128),                                  // 상한 128자
];
for (const raw of ACCEPTED) {
  assert.equal(isClientReceiptId(raw), true, `통과해야 한다: ${raw}`);
  assert.equal(clientReceiptId(raw, FALLBACK), raw, "형식을 통과하면 그대로 쓴다");
}

// ── 서버 발급으로 떨어지는 모양 ──────────────────────────────────────────────
const REJECTED = [
  ["abcdefg", "8자 미만은 계정 전역 멱등 키로 쓰기엔 너무 좁다"],
  ["", "빈 문자열"],
  ["A".repeat(129), "128자 초과"],
  ["tx/with/slash", "슬래시는 Firestore 문서 id 를 쪼갠다"],
  ["tx.with.dot", "점은 문서 경로 문법과 겹친다"],
  ["tx with space", "공백"],
  ["tx#hash!", "그 밖의 기호"],
  ["한글영수증번호", "ASCII 밖"],
  ["tx\nnewline", "제어문자"],
  [undefined, "구 클라는 txId 를 아예 보내지 않는다"],
  [null, "null"],
  [12345678, "숫자는 문자열이 아니다"],
  [{txId: "abcdefgh"}, "객체"],
  [["abcdefgh"], "배열"],
  [true, "boolean"],
];
for (const [raw, why] of REJECTED) {
  assert.equal(isClientReceiptId(raw), false, `서버 발급이어야 한다: ${why}`);
  assert.equal(clientReceiptId(raw, FALLBACK), FALLBACK,
    `거절이 아니라 폴백이다(던지면 세션이 끊긴다): ${why}`);
}

// 던지지 않는다는 것 자체를 못박는다 — 여기서 예외가 새면 구 클라가 세션째 죽는다.
assert.doesNotThrow(() => clientReceiptId(Symbol("x"), FALLBACK));
assert.doesNotThrow(() => clientReceiptId(Object.create(null), FALLBACK));

// ── 캐시본: 접기 ────────────────────────────────────────────
// 슬롯 **값**은 영수증에 실리지 않는다. openPack 의 ownership 은 슬롯 전체 값이라 계정이
// 자랄수록 커지고, 영수증이 1MiB 상한을 치면 트랜잭션이 통째로 실패해 정상 명령이 죽는다.
const RESPONSE = {
  revision: 12,
  updatedSlots: {ownership: {ownedIds: [1, 2, 3]}, cardGrowth: {entries: []}},
  wallet: {rev: 9, balances: {Gold: 100, Diamond: 0, Energy: 0, Shard: 0}},
  packId: "Pack_Basic",
};

{
  const cached = cacheableResponse(RESPONSE, RESPONSE.updatedSlots);
  assert.deepEqual(cached.slotKeys, ["ownership", "cardGrowth"], "슬롯은 이름만 남는다");
  assert.equal(cached.updatedSlots, undefined);
  assert.equal(cached.revision, 12, "나머지 필드는 그대로다");
  assert.equal(cached.packId, "Pack_Basic", "명령별 필드도 캐시된다");

  const document = JSON.stringify(cached);
  assert.equal(document.includes("ownedIds"), false, "슬롯 값은 영수증 문서에 없다");
  assert.equal(document.includes("updatedSlots"), false,
    "JSON.stringify 가 undefined 필드를 버린다 — 키 자체가 문서에 남지 않는다");
}

// ── 캐시본: 펴기 ────────────────────────────────────────────
// 영수증을 거친 뒤(readReceipt 가 JSON 을 푸는 것까지 포함해) 응답이 되살아나는가.
const receiptOf = (cached) => readReceipt(snapshotOf({
  source: "openPack", result: cached === undefined ? null : JSON.stringify(cached),
})).result;

{
  const saved = {
    revision: 12,
    ownership: {ownedIds: [1, 2, 3, 4]},
    cardGrowth: {entries: [{cardId: 4}]},
  };
  const replay = replayCached(receiptOf(cacheableResponse(RESPONSE, RESPONSE.updatedSlots)), saved, 12);

  assert.deepEqual(replay.updatedSlots, {ownership: saved.ownership, cardGrowth: saved.cardGrowth},
    "슬롯은 지금 세이브 문서에서 다시 짓는다 — 값이 더 최신이라 채택도 더 옳다");
  assert.equal(replay.revision, 12, "revision 은 첫 시도 때의 값이다");
  assert.equal(replay.packId, "Pack_Basic", "명령별 필드가 되살아난다");
  assert.deepEqual(replay.wallet, RESPONSE.wallet);
  assert.equal("slotKeys" in replay, false, "캐시 내부 축은 응답에 새지 않는다");
}

{
  // 슬롯이 세이브에 없을 리는 없지만, 있더라도 그 슬롯만 빠지고 채택은 진행돼야 한다.
  const replay = replayCached(
    receiptOf(cacheableResponse(RESPONSE, RESPONSE.updatedSlots)),
    {revision: 12, ownership: {ownedIds: [7]}}, 12);

  assert.deepEqual(Object.keys(replay.updatedSlots), ["ownership"]);
  assert.equal(replay.revision, 12);
}

// ── 못 쓸 캐시본은 던진다 ──────────────────────────────────
// 조용히 기본값을 내보내면 클라가 그것을 채택해 그 자리에서 상태가 갈린다.
assert.throws(() => replayCached(receiptOf(undefined), {revision: 12}, 12),
  "C8-1 시절 result 없이 끊린 영수증 — revision 이 빠진 응답을 내보낼 수 없다");
assert.throws(() => replayCached({slotKeys: ["ownership"]}, {revision: 12}, 12),
  "revision 이 없는 캐시본은 못 쓴다");
assert.throws(() => replayCached({revision: "12", slotKeys: []}, {revision: 12}, 12),
  "정수가 아닌 revision 도 못 쓴다");
assert.throws(() => replayCached(cacheableResponse(RESPONSE, RESPONSE.updatedSlots), {revision: 13}, 13),
  "정상 재시도면 문서 revision 이 캐시본과 같다 — 다르면 그 사이에 다른 쓰기가 끼었고, " +
  "첫 시도의 revision 에 지금 슬롯 값을 섞어 내보내면 클라 상태가 갈린다");

// ── 영수증 조회: 미스는 집행, 히트는 source 로 가른다 ───────────────
assert.deepEqual(readReceipt(snapshotOf(undefined)), {hit: false},
  "영수증이 없으면 처음 온 요청이라 그대로 집행한다");

// source 가 없는 낡은 영수증도 히트이긴 하다. 빈 문자열은 어떤 명령 이름과도 같지 않아
// 대조에서 자동으로 TxIdReused 로 떨어진다 — 조용히 재집행되는 갈래가 없다.
assert.equal(readReceipt(snapshotOf({result: null})).source, "");
assert.equal(readReceipt(snapshotOf({source: "openPack", result: null})).source, "openPack");

console.log("test-receipt-idempotency: ok");
