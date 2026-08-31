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
const {parseSpecPayload, specPayloadHash} = require("../lib/specs/specBlobReader.js");

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

console.log("test-spec-blob: ok");
