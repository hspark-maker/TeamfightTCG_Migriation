// 서버 전투 리졸버 회귀. 두 종류를 본다.
//  1) splitmix64 벡터 — 이 파일 밖(독립 구현)에서 계산한 값과 대조한다. C# MatchRandom 과
//     비트 단위로 같은지가 재시뮬레이션의 전제라, 여기가 깨지면 나머지는 볼 필요가 없다.
//  2) 스펙 호환 계약 — Card 표에 maxHp 가 아직 없으면 재생을 포기해야 한다(잘못된 결과로
//     정산하는 것보다 포기가 낫다).
// 아직 C# 골든 벡터(finalStateHash)는 없다. 그게 들어오기 전까지 서버 권위 스위치
// (submitMatchResult.ts SERVER_SIMULATION_AUTHORITATIVE)를 켜면 안 된다.
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");
const {Rng, simulateBattle} = require("../lib/battleSimulation.js");
const {parseSynergyRules} = require("../lib/synergyRules.js");

const SEED = BigInt("0x0123456789ABCDEF");

function parseCsvLine(line) {
  const values = [];
  let value = "";
  let quoted = false;
  for (let i = 0; i < line.length; i++) {
    const char = line[i];
    if (char === '"') {
      if (quoted && line[i + 1] === '"') { value += '"'; i++; }
      else quoted = !quoted;
    } else if (char === "," && !quoted) {
      values.push(value); value = "";
    } else value += char;
  }
  values.push(value);
  return values;
}

function readSheet(table) {
  const file = path.join(__dirname, "..", "..", "docs", "SpecData", `${table}_sheet.csv`);
  const lines = fs.readFileSync(file, "utf8").replace(/^\uFEFF/, "").split(/\r?\n/).filter(Boolean);
  const headerIndex = lines.findIndex((line) => parseCsvLine(line)[0] === "id");
  if (headerIndex < 0 || headerIndex + 1 >= lines.length) throw new Error(`${table}: header missing`);
  const columns = parseCsvLine(lines[headerIndex]);
  return lines.slice(headerIndex + 2).map((line) => Object.fromEntries(
    parseCsvLine(line).map((value, index) => [columns[index], value])));
}

const synergyRules = parseSynergyRules(
  readSheet("SynergyDef"), readSheet("SynergyTierDef"), readSheet("SynergyEffectDef"));

// --- 1) splitmix64 ---
{
  const rng = new Rng(SEED);
  const got = [];
  for (let i = 0; i < 5; i++) got.push("0x" + rng.next().toString(16));
  assert.deepEqual(got, [
    "0x157a3807a48faa9d",
    "0xd573529b34a1d093",
    "0x2f90b72e996dccbe",
    "0xa2d419334c4667ec",
    "0x1404ce914938008",
  ]);
  assert.equal(rng.draws, 5);
}

// seed 0 은 GOLDEN 으로 정규화된다(C# DeterministicRandom.Seed 와 같은 규약).
{
  const rng = new Rng(BigInt(0));
  const got = [];
  for (let i = 0; i < 3; i++) got.push("0x" + rng.next().toString(16));
  assert.deepEqual(got, [
    "0x6e789e6aa1b965f4",
    "0x6c45d188009454f",
    "0xf88bb8a8724c81ec",
  ]);
}

// 파생 덱 시드. 공유 스트림을 소비하지 않는다.
{
  assert.equal("0x" + Rng.deckSeed(SEED, 0).toString(16), "0x7a76321e37168f90");
  assert.equal("0x" + Rng.deckSeed(SEED, 1).toString(16), "0x1018a8c6c59b963c");
}

// range: max<=1 이면 전진하지 않는다(C# MatchRandom.Range 와 동일).
{
  const rng = new Rng(SEED);
  const got = [];
  for (let i = 0; i < 6; i++) got.push(rng.range(3));
  assert.deepEqual(got, [0, 2, 2, 2, 2, 1]);
  assert.equal(rng.draws, 6);

  const idle = new Rng(SEED);
  assert.equal(idle.range(1), 0);
  assert.equal(idle.range(0), 0);
  assert.equal(idle.draws, 0);
}

// --- 2) 스펙 호환 계약 ---
{
  const deck = [];
  for (let i = 1; i <= 6; i++) {
    deck.push({cardId: i, level: 1, hpBonus: 0, evolutionStage: 1,
      unlockedKeywords: 0, synergyUnlocked: false});
  }
  const specsWithout = new Map();
  const specsWith = new Map();
  for (let i = 1; i <= 6; i++) {
    // maxHp 열이 아직 안 올라온 구 표를 흉내낸다(parseCardSpecRow 가 0 으로 떨군다).
    specsWithout.set(i, {id: i, maxHp: 0, keywords: 0, keywordUnlockLevel: 1,
      defaultEvolutionStage: 0, synergies: [], hp2: 2, hp3: 3, hp4: 4});
    specsWith.set(i, {id: i, maxHp: 10, keywords: 0, keywordUnlockLevel: 1,
      defaultEvolutionStage: 0, synergies: [], hp2: 2, hp3: 3, hp4: 4});
  }
  const input = (specs) => ({
    seedHex: "0123456789abcdef",
    decks: [deck, deck],
    specs,
    synergyRules,
    commandLog: "",
  });

  const refused = simulateBattle(input(specsWithout));
  assert.equal(refused.ok, false, "maxHp 없는 표로는 재생을 포기해야 한다");

  // maxHp 가 있으면 적어도 makeField 를 통과해 명령 로그 부족으로 떨어진다(체력 0 승부가 아님).
  const ran = simulateBattle(input(specsWith));
  assert.equal(ran.ok, false);
  assert.notEqual(ran.reason, refused.reason);
}

// --- 3) Unity가 실제 규칙 실행으로 캡처한 골든 코퍼스 ---
{
  const goldenRoot = path.join(__dirname, "..", "testdata", "golden");
  const files = fs.existsSync(goldenRoot) ? fs.readdirSync(goldenRoot)
    .filter((name) => name.endsWith(".json")).sort() : [];
  let eligibleCount = 0;
  const shuffleMismatches = [];
  for (const file of files) {
    const fullPath = path.join(goldenRoot, file);
    const golden = JSON.parse(fs.readFileSync(fullPath, "utf8"));
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
      continue;
    }

    // boardOrder 는 셔플이 끝난 실제 보드 순서다. 없으면(구 골든) 재생기가 자기 셔플을 돌려야 하는데,
    // decks[].cards 가 cardId 로 정렬돼 있어 셔플 입력 순서를 복원할 수 없다 — 결과가 갈려도
    // "셔플이 다른가 규칙이 다른가"를 구분하지 못하므로 검증 대상에서 뺀다.
    const boardOrders = golden.decks.map((deck) => deck.boardOrder);
    if (boardOrders.some((order) => !Array.isArray(order) || order.length === 0)) {
      assert.ok(golden.schemaVersion < 3, `${file}: schemaVersion 3 requires boardOrder`);
      console.log(`  skip ${file}: boardOrder 없음(구 스키마) — 재캡처 필요`);
      continue;
    }

    eligibleCount++;
    const specs = new Map(golden.cardSpecs.map((spec) => [spec.id, spec]));
    const result = simulateBattle({
      seedHex: golden.seedHex,
      decks: golden.decks.map((deck) => deck.cards),
      specs,
      synergyRules,
      commandLog: golden.commandLog,
      boardOrders,
    });
    assert.equal(result.ok, true, `${file}: replay failed (${result.reason})`);
    assert.equal(result.finalStateHash, golden.finalStateHash, `${file}: finalStateHash`);
    assert.equal(result.drawCount, golden.finalDrawCount, `${file}: finalDrawCount`);
    assert.deepEqual(result.remaining, golden.remaining, `${file}: remaining`);

    // 셔플 등가성은 규칙 등가성과 따로 본다. 여기가 틀리면 재생기의 Fisher-Yates 나 파생 시드가
    // 클라와 다르다는 뜻이고, 위 해시 대조가 통과했더라도 라이브 재시뮬은 틀린 보드로 돈다.
    const seedValue = BigInt(`0x${golden.seedHex}`);
    for (const deck of golden.decks) {
      const ids = deck.cards.map((card) => card.cardId);
      const rng = new Rng(Rng.deckSeed(seedValue, deck.ownerIndex));
      for (let i = ids.length - 1; i > 0; i--) {
        const j = rng.range(i + 1);
        [ids[i], ids[j]] = [ids[j], ids[i]];
      }
      const shuffleMatches = JSON.stringify(ids) === JSON.stringify(deck.boardOrder);
      if (golden.schemaVersion >= 3) {
        assert.equal(shuffleMatches, true,
          `${file} owner${deck.ownerIndex}: derived shuffle differs from client boardOrder`);
      } else if (!shuffleMatches) {
        shuffleMismatches.push(`${file} owner${deck.ownerIndex}: ` +
          `replay=${JSON.stringify(ids)} client=${JSON.stringify(deck.boardOrder)}`);
      }
    }
    // 승자 판정 자체의 등가성. remaining 만 맞아도 승자 산출식이 갈릴 수 있다.
    // schemaVersion 1 은 무승부를 모르던 클라가 찍은 것이라(동시 전멸을 승리로 기록했다)
    // 승자 비교를 걸 수 없다 — 재캡처 대상이다.
    if (golden.schemaVersion >= 2) {
      assert.equal(result.draw === true, golden.draw === true, `${file}: draw`);
      assert.equal(result.winnerOwner, golden.winnerOwner, `${file}: winnerOwner`);
    } else if (Number.isInteger(golden.winnerOwner) && golden.winnerOwner >= 0 &&
        result.winnerOwner !== golden.winnerOwner) {
      console.log(`  note ${file}: 승자 비교 생략(schemaVersion 1, 무승부 미지원 캡처)`);
    }

    // 체크포인트는 (turn, actingOwner) 로 맞춘다. 명령 로그에 턴 경계 레코드가 없어서,
    // 공격 없이 닫힌 턴은 재생기가 존재 자체를 알 수 없다 — 그때 개수만 비교하면
    // "몇 번째가 틀렸다"가 아니라 "개수가 다르다"로만 떨어져 발산 지점을 못 찾는다.
    const replayed = new Map(result.checkpoints.map((cp) => [`${cp.turn}:${cp.actingOwner}`, cp]));
    const missing = [];
    for (const expected of golden.checkpoints) {
      const key = `${expected.turn}:${expected.actingOwner}`;
      const actual = replayed.get(key);
      if (actual == null) { missing.push(key); continue; }
      assert.deepEqual(actual, expected, `${file}: checkpoint ${key}`);
      replayed.delete(key);
    }
    assert.equal(missing.length, 0,
      `${file}: 골든에 있는 체크포인트를 재생기가 만들지 않았다 (turn:owner = ${missing.join(", ")}). ` +
      "명령 없이 닫힌 턴이면 명령 로그에 턴 경계 레코드가 필요하다.");
    assert.equal(replayed.size, 0,
      `${file}: 재생기가 골든에 없는 체크포인트를 만들었다 (turn:owner = ${[...replayed.keys()].join(", ")})`);
  }
  // schemaVersion 1~2는 Local 셔플 시절 캡처라 불일치를 참고 정보로만 남긴다.
  // schemaVersion 3부터는 위에서 결정론 셔플 일치를 강제한다.
  if (shuffleMismatches.length > 0) {
    console.log(`참고: 시드 파생 셔플과 클라 보드가 다르다 (${shuffleMismatches.length}건). ` +
      "보드 순서는 제출값을 쓴다.");
  }
  if (process.env.REQUIRE_BATTLE_GOLDENS === "1") {
    assert.ok(eligibleCount >= 12, `expected at least 12 eligible goldens, got ${eligibleCount}`);
  }
}

console.log("battle-sim tests passed");
