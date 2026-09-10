// 스펙 블롭 파서 회귀. 에뮬레이터 없이 lib/ 를 직접 require 한다(test-open-pack.js 관용구).
//
// 여기서 지키는 것 둘.
//  1) 블롭 payload 를 편 행이 예전 `rows/` 문서와 **같은 타입**으로 나오는가.
//     payout.finiteInteger 가 `typeof value === "number"` 를 요구해서, 정수 열이 문자열로 남으면
//     매치 결과 제출이 통째로 실패한다 — 배포하고 나서야 보이는 종류의 사고다.
//  2) 반쪽 업로드된 payload 를 조용히 넘기지 않는가. 여기서 통과시키면 서버가 클라와 다른 표로
//     보상·덱을 판정하고, 그 갈림은 로그에 안 남는다.
const assert = require("node:assert/strict");
const {createHash} = require("node:crypto");
const Module = require("node:module");
const requests = [];
const mockDb = {
  doc(path) {
    return {get: () => new Promise((resolve, reject) => requests.push({path, resolve, reject}))};
  },
};
const originalLoad = Module._load;
let reader;
try {
  Module._load = function(request, parent, isMain) {
    if (request === "../firebaseApp") return {db: mockDb};
    if (request === "firebase-functions/logger") return {info() {}};
    return originalLoad.call(this, request, parent, isMain);
  };
  reader = require("../lib/specs/specBlobReader.js");
} finally {
  Module._load = originalLoad;
}
const {parseSpecPayload, specPayloadHash, readSpecPins, readSpecRows,
  readPinnedSpecRows, clearSpecCache} = reader;

const payload = JSON.stringify([
  ["id", "packId", "amount", "minGrade", "note"],
  ["3", "starter", "-12", "Bronze", ""],
  ["10", "1001", "0", "9007199254740993", "a,b"],
]);

const rows = parseSpecPayload(payload);
assert.equal(rows.length, 2);

// 정수 열은 number 로 돌아온다 — 업로더가 rows/ 에 integerValue 로 쓰던 것과 같은 모양.
assert.deepEqual(rows[0], {id: 3, packId: "starter", amount: -12, minGrade: "Bronze", note: ""});

// 숫자만 든 문자열 열(packId "1001")도 number 가 되지만 소비자가 전부 String(...) 으로 받아 값이 같다.
assert.equal(String(rows[1].packId), "1001");
// 빈 문자열은 0 이 아니다. 0 으로 접으면 "값 없음" 과 "0" 이 구별되지 않는다.
assert.equal(rows[0].note, "");
// 안전 정수 범위를 넘으면 문자열로 남긴다 — Number 로 접으면 값이 조용히 바뀐다.
assert.equal(rows[1].minGrade, "9007199254740993");

// 해시는 MD5 앞 8바이트 hex. 업로더 SpecFirestoreUploader.HashOf 와 같은 규칙이어야 한다.
const expected = createHash("md5").update(payload, "utf8").digest("hex").slice(0, 16);
assert.equal(specPayloadHash(payload), expected);
assert.equal(specPayloadHash(payload).length, 16);
assert.notEqual(specPayloadHash(payload), specPayloadHash(payload + " "));

// 깨진 payload 는 전부 예외. 조용한 빈 표는 만들지 않는다.
const broken = [
  "[]",
  "[[\"id\"]]",
  JSON.stringify([["id", "amount"], ["1"]]),
  JSON.stringify([["id", "amount"], ["1", 2]]),
  JSON.stringify([["id", 5], ["1", "2"]]),
  "{}",
  "not json",
];
for (const text of broken) {
  assert.throws(() => parseSpecPayload(text), `깨진 payload 를 통과시켰다: ${text}`);
}

function fixture(env, version, value = version) {
  const text = JSON.stringify([["id", "value"], ["2", value], ["1", value]]);
  const payloadHash = specPayloadHash(text);
  return {
    pin: {blobPath: `envs/${env}/specs/Card/releases/${version}`, payloadHash},
    blob: {major: 4, payload: text, payloadHash, rowCount: 2},
    rows: [{id: 1, value}, {id: 2, value}],
  };
}

function indexOf(tables) {
  return {major: 4, minor: 0, tables};
}

function resolve(request, data) {
  request.resolve({exists: data !== undefined, data: () => data});
}

// A turn of the event loop drains all promise continuations without wall-clock sleeps.
function drain() {
  return new Promise((resolve) => setImmediate(resolve));
}

function reset() {
  clearSpecCache();
  requests.length = 0;
}

function paths(...expectedPaths) {
  assert.deepEqual(requests.map((request) => request.path), expectedPaths);
}

async function testConcurrentReads() {
  reset();
  const card = fixture("test", "v1");
  const tables = ["Card", "SynergyDef", "SynergyTierDef", "SynergyEffectDef"];
  const pins = readSpecPins("test", tables);
  await drain();
  paths("envs/test/specs/_index");
  resolve(requests[0], indexOf(Object.fromEntries(tables.map((table) => [table, card.pin]))));
  assert.equal(Object.keys(await pins).length, 4);

  reset();
  const concurrent = Promise.all(Array.from({length: 20}, () => readSpecRows("test", "Card")));
  await drain();
  paths("envs/test/specs/_index");
  resolve(requests[0], indexOf({Card: card.pin}));
  await drain();
  paths("envs/test/specs/_index", card.pin.blobPath);
  resolve(requests[1], card.blob);
  for (const result of await concurrent) assert.deepEqual(result, card.rows);
  assert.deepEqual(await readSpecRows("test", "Card"), card.rows);
  assert.deepEqual(await readPinnedSpecRows("test", "Card", card.pin), card.rows);
  paths("envs/test/specs/_index", card.pin.blobPath);

  reset();
  const current = readSpecRows("test", "Card");
  const pinned = readPinnedSpecRows("test", "Card", card.pin);
  await drain();
  paths("envs/test/specs/_index", card.pin.blobPath);
  resolve(requests[0], indexOf({Card: card.pin}));
  await drain();
  paths("envs/test/specs/_index", card.pin.blobPath);
  resolve(requests[1], card.blob);
  assert.deepEqual(await current, card.rows);
  assert.deepEqual(await pinned, card.rows);
}

async function testExpiryAndVersion() {
  reset();
  const oldNow = Date.now;
  let now = 100000;
  Date.now = () => now;
  try {
    const first = fixture("test", "v1");
    const second = fixture("test", "v2");
    const initial = readSpecRows("test", "Card");
    await drain();
    resolve(requests[0], indexOf({Card: first.pin}));
    await drain();
    resolve(requests[1], first.blob);
    assert.deepEqual(await initial, first.rows);
    now += 29999;
    assert.deepEqual(await readSpecRows("test", "Card"), first.rows);
    assert.equal(requests.length, 2);

    now++;
    const refresh = Promise.all(Array.from({length: 20}, () => readSpecRows("test", "Card")));
    await drain();
    paths("envs/test/specs/_index", first.pin.blobPath, "envs/test/specs/_index");
    resolve(requests[2], indexOf({Card: first.pin}));
    await refresh;
    assert.equal(requests.length, 3, "same hash and path must reuse immutable blob after index expiry");

    now += 30000;
    const unchangedRelease = readSpecRows("test", "Card");
    await drain();
    assert.equal(requests.length, 4);
    resolve(requests[3], indexOf({Card: {...first.pin, blobPath: "envs/test/specs/Card/releases/v1-copy"}}));
    assert.deepEqual(await unchangedRelease, first.rows);
    assert.equal(requests.length, 4, "new release path with unchanged hash must reuse completed rows");

    now += 30000;
    const changed = readSpecRows("test", "Card");
    const pinnedOld = readPinnedSpecRows("test", "Card", first.pin);
    await drain();
    assert.equal(requests.length, 5);
    resolve(requests[4], indexOf({Card: second.pin}));
    await drain();
    assert.equal(requests[5].path, second.pin.blobPath);
    resolve(requests[5], second.blob);
    assert.deepEqual(await changed, second.rows);
    assert.deepEqual(await pinnedOld, first.rows);
  } finally {
    Date.now = oldNow;
  }
}

async function testFailuresRetry() {
  for (const invalidIndex of [null, undefined, {major: -1, minor: 0, tables: {}}]) {
    reset();
    const pair = Promise.allSettled([readSpecPins("test", ["Card"]), readSpecPins("test", ["Card"])]);
    await drain();
    paths("envs/test/specs/_index");
    if (invalidIndex === null) requests[0].reject(new Error("index offline"));
    else resolve(requests[0], invalidIndex);
    assert.ok((await pair).every((result) => result.status === "rejected"));
    const card = fixture("test", "retry");
    const retry = readSpecPins("test", ["Card"]);
    await drain();
    assert.equal(requests.length, 2);
    resolve(requests[1], indexOf({Card: card.pin}));
    assert.deepEqual(await retry, {Card: card.pin});
  }

  const card = fixture("test", "retry");
  for (const invalidBlob of [null, undefined, {...card.blob, payloadHash: "wrong"},
    {...card.blob, rowCount: 3}]) {
    reset();
    const pair = Promise.allSettled([readPinnedSpecRows("test", "Card", card.pin),
      readPinnedSpecRows("test", "Card", card.pin)]);
    await drain();
    paths(card.pin.blobPath);
    if (invalidBlob === null) requests[0].reject(new Error("blob offline"));
    else resolve(requests[0], invalidBlob);
    assert.ok((await pair).every((result) => result.status === "rejected"));
    const retry = readPinnedSpecRows("test", "Card", card.pin);
    await drain();
    assert.equal(requests.length, 2);
    resolve(requests[1], card.blob);
    assert.deepEqual(await retry, card.rows);
  }
}

async function testIsolation() {
  reset();
  const first = fixture("test", "v1");
  const otherEnv = fixture("prod", "v1");
  const current = Promise.all([readSpecRows("test", "Card"), readSpecRows("prod", "Card")]);
  await drain();
  paths("envs/test/specs/_index", "envs/prod/specs/_index");
  resolve(requests[0], indexOf({Card: first.pin}));
  resolve(requests[1], indexOf({Card: otherEnv.pin}));
  await drain();
  paths("envs/test/specs/_index", "envs/prod/specs/_index", first.pin.blobPath, otherEnv.pin.blobPath);
  resolve(requests[2], first.blob);
  resolve(requests[3], otherEnv.blob);
  await current;

  reset();
  const otherPath = {...first.pin, blobPath: "envs/test/specs/Card/releases/other"};
  const differentPaths = Promise.all([readPinnedSpecRows("test", "Card", first.pin),
    readPinnedSpecRows("test", "Card", otherPath)]);
  await drain();
  paths(first.pin.blobPath, otherPath.blobPath);
  resolve(requests[0], first.blob);
  resolve(requests[1], first.blob);
  await differentPaths;

  reset();
  const wrongHash = {...first.pin, payloadHash: "wrong"};
  const differentHashes = Promise.allSettled([readPinnedSpecRows("test", "Card", first.pin),
    readPinnedSpecRows("test", "Card", wrongHash)]);
  await drain();
  paths(first.pin.blobPath, first.pin.blobPath);
  resolve(requests[0], first.blob);
  resolve(requests[1], first.blob);
  const results = await differentHashes;
  assert.equal(results[0].status, "fulfilled");
  assert.equal(results[1].status, "rejected");
  assert.match(results[1].reason.message, /hash mismatch/);
  await assert.rejects(readPinnedSpecRows("prod", "Card", first.pin), /invalid spec pin/);
  assert.equal(requests.length, 2);
}

async function testClearWhilePending() {
  reset();
  const first = fixture("test", "v1");
  const second = fixture("test", "v2");
  const oldIndex = readSpecPins("test", ["Card"]);
  await drain();
  clearSpecCache();
  const newIndex = readSpecPins("test", ["Card"]);
  await drain();
  assert.equal(requests.length, 2);
  resolve(requests[0], indexOf({Card: first.pin}));
  assert.deepEqual(await oldIndex, {Card: first.pin});
  const joinedIndex = readSpecPins("test", ["Card"]);
  await drain();
  assert.equal(requests.length, 2, "old completion must neither fill cache nor remove newer pending index");
  resolve(requests[1], indexOf({Card: second.pin}));
  assert.deepEqual(await newIndex, {Card: second.pin});
  assert.deepEqual(await joinedIndex, {Card: second.pin});

  reset();
  const oldBlob = readPinnedSpecRows("test", "Card", first.pin);
  await drain();
  clearSpecCache();
  const newBlob = readPinnedSpecRows("test", "Card", first.pin);
  await drain();
  assert.equal(requests.length, 2);
  resolve(requests[0], first.blob);
  assert.deepEqual(await oldBlob, first.rows);
  const joinedBlob = readPinnedSpecRows("test", "Card", first.pin);
  await drain();
  assert.equal(requests.length, 2, "old completion must not remove newer pending blob");
  let joinedSettled = false;
  joinedBlob.then(() => { joinedSettled = true; });
  await drain();
  assert.equal(joinedSettled, false, "old completion must not repopulate cleared blob cache");
  resolve(requests[1], first.blob);
  assert.deepEqual(await newBlob, first.rows);
  assert.deepEqual(await joinedBlob, first.rows);
}

// A missing mock response must fail CI rather than leave an unresolved promise and exit successfully.
const timeout = setTimeout(() => {
  console.error("test-spec-blob: timed out waiting for a mocked read");
  process.exit(1);
}, 10000);
(async () => {
  await testConcurrentReads();
  await testExpiryAndVersion();
  await testFailuresRetry();
  await testIsolation();
  await testClearWhilePending();
  console.log("test-spec-blob: ok (parser, single-flight, TTL, retry, isolation, clear races)");
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
}).finally(() => {
  clearTimeout(timeout);
  clearSpecCache();
});
