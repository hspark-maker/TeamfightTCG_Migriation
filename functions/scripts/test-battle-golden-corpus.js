// 골든 코퍼스(functions/testdata/golden/*.json)의 형식 계약만 본다.
//
// 규칙 등가성(finalStateHash·체크포인트·잔존·승패)은 여기서 보지 않는다 — 전투 리졸버가
// C# BattleCore 한 벌뿐이라 TS 사본과 대조할 대상이 없다. 그쪽은 골든 러너가 강제한다:
//   dotnet run --project Tools/BattleCoreGolden/TeamfightTCG.BattleCoreGolden.csproj -- .
//   (npm run test:battle-golden)
//
// 여기서 막는 것: 러너가 조용히 건너뛰는 벡터가 늘어나는 것. boardOrder 없는 캡처는
// SKIP 되므로, 코퍼스가 통째로 스킵돼도 골든 러너는 0건 검사하고 성공한다.
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");

const goldenRoot = path.join(__dirname, "..", "testdata", "golden");
const files = fs.existsSync(goldenRoot) ? fs.readdirSync(goldenRoot)
  .filter((name) => name.endsWith(".json")).sort() : [];

let eligibleCount = 0;
const skipped = [];

for (const file of files) {
  const golden = JSON.parse(fs.readFileSync(path.join(goldenRoot, file), "utf8"));
  assert.ok([1, 2, 3].includes(golden.schemaVersion), `${file}: schemaVersion`);
  assert.ok(Number.isInteger(golden.rulesetVersion) && golden.rulesetVersion > 0,
    `${file}: rulesetVersion`);
  assert.match(golden.contentFingerprint, /^[0-9a-f]{64}$/, `${file}: contentFingerprint`);
  assert.ok(golden.capturedAtUtc && golden.unityVersion, `${file}: capture metadata`);
  assert.ok(Array.isArray(golden.decks) && golden.decks.length === 2, `${file}: decks`);
  assert.ok(Array.isArray(golden.cardSpecs) && golden.cardSpecs.length > 0, `${file}: cardSpecs`);
  assert.match(golden.commandLogHash, /^[0-9a-f]{64}$/, `${file}: commandLogHash`);
  const rawLog = Buffer.from(golden.commandLog || "", "base64");
  assert.equal(crypto.createHash("sha256").update(rawLog).digest("hex"), golden.commandLogHash,
    `${file}: command log digest`);

  if (!golden.eligible) {
    assert.ok(golden.exclusionReason, `${file}: excluded golden needs a reason`);
    skipped.push(`${file}: ${golden.exclusionReason}`);
    continue;
  }
  // boardOrder 는 셔플이 끝난 실제 보드 순서다. 없으면 골든 러너가 벡터를 건너뛴다 —
  // decks[].cards 가 cardId 로 정렬돼 있어 셔플 입력 순서를 복원할 수 없기 때문이다.
  const missingBoardOrder = golden.decks.some(
    (deck) => !Array.isArray(deck.boardOrder) || deck.boardOrder.length === 0);
  if (missingBoardOrder) {
    assert.ok(golden.schemaVersion < 3, `${file}: schemaVersion 3 requires boardOrder`);
    skipped.push(`${file}: boardOrder 없음(구 스키마) — 재캡처 필요`);
    continue;
  }
  // schemaVersion 1 은 무승부를 모르던 클라가 찍은 것이라 승자 비교를 걸 수 없다.
  if (golden.schemaVersion >= 2) {
    assert.equal(typeof golden.draw, "boolean", `${file}: draw`);
    assert.ok(Number.isInteger(golden.winnerOwner), `${file}: winnerOwner`);
  }
  assert.match(golden.finalStateHash, /^[0-9a-f]{16}$/, `${file}: finalStateHash`);
  assert.ok(Array.isArray(golden.remaining) && golden.remaining.length === 2, `${file}: remaining`);
  eligibleCount++;
}

for (const line of skipped) console.log(`  skip ${line}`);
if (process.env.REQUIRE_BATTLE_GOLDENS === "1") {
  assert.ok(eligibleCount >= 12, `expected at least 12 eligible goldens, got ${eligibleCount}`);
}
console.log(`battle golden corpus ok (eligible ${eligibleCount} / files ${files.length})`);
