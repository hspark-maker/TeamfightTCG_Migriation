/**
 * openPack 이 볼 스펙 표를 실서버에서 읽어 사전 점검한다. **읽기 전용이다.**
 *
 * 클라는 CardPack 시트에 행이 없으면 SO 인스펙터로 폴백하지만 서버는 SO 를 못 본다 —
 * 시트에 빠진 팩은 상점에 떠 있어도 openPack 이 PackNotFound 로 거절한다.
 * 그 어긋남을 실왕복 전에 잡는 것이 이 스크립트의 유일한 목적이다.
 *
 * 판정은 서버가 실제로 쓰는 순수 모듈(lib/packs/*)을 그대로 불러서 한다 — 여기서 규칙을 다시 쓰면
 * 점검이 통과해도 서버가 다르게 판정할 수 있다.
 *
 * **자격증명이 필요 없다.** firestore.rules 의 스펙 읽기 조건이 isSignedIn() 뿐이라,
 * Assets/google-services.json 의 웹 API key 로 익명 로그인한 뒤 Firestore REST 로 읽는다
 * (Admin SDK·gcloud·서비스 계정 키 모두 불필요). 매 실행마다 익명 계정이 하나 생긴다.
 *
 * 사용:
 *   npm run build && node scripts/check-pack-spec.js test
 *   npm run build && node scripts/check-pack-spec.js live
 */
const fs = require("node:fs");
const path = require("node:path");
const {resolveDropPool} = require("../lib/packs/packDraw.js");
const {entryPointsFromRows, gradeOf, parseRequiredGrade, GRADE_KEYS,
  FALLBACK_ENTRY_POINTS} = require("../lib/packs/rankGrade.js");

const GOOGLE_SERVICES = path.join(__dirname, "..", "..", "Assets", "google-services.json");
const DATABASE_ID = "cardbattle";

function readConfig() {
  const raw = JSON.parse(fs.readFileSync(GOOGLE_SERVICES, "utf8"));
  const projectId = raw.project_info?.project_id;
  const apiKey = raw.client?.[0]?.api_key?.[0]?.current_key;
  if (!projectId || !apiKey) throw new Error(`${GOOGLE_SERVICES} 에서 project_id/api_key 를 못 읽었다.`);
  return {projectId, apiKey};
}

/** 익명 로그인. 클라(FirebaseAuthService)와 같은 방식이라 룰의 isSignedIn() 을 통과한다. */
async function signInAnonymously(apiKey) {
  const response = await fetch(
    `https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=${apiKey}`,
    {method: "POST", headers: {"Content-Type": "application/json"},
      body: JSON.stringify({returnSecureToken: true})});

  const body = await response.json();
  if (!response.ok) {
    throw new Error(`익명 로그인 실패(${response.status}): ${body.error?.message ?? "unknown"}`);
  }
  return body.idToken;
}

/** Firestore REST 의 타입 봉투를 벗긴다. 업로더가 쓰는 타입은 integerValue/stringValue 둘뿐이다. */
function unwrap(field) {
  if (field.integerValue !== undefined) return Number(field.integerValue);
  if (field.stringValue !== undefined) return field.stringValue;
  if (field.doubleValue !== undefined) return Number(field.doubleValue);
  if (field.booleanValue !== undefined) return field.booleanValue;
  if (field.nullValue !== undefined) return null;
  return undefined;
}

async function readRows(projectId, idToken, env, table) {
  const base = `https://firestore.googleapis.com/v1/projects/${projectId}/databases/${DATABASE_ID}` +
    `/documents/envs/${env}/specs/${table}/rows`;

  const rows = [];
  let pageToken = "";
  do {
    const url = `${base}?pageSize=300${pageToken ? `&pageToken=${encodeURIComponent(pageToken)}` : ""}`;
    const response = await fetch(url, {headers: {Authorization: `Bearer ${idToken}`}});
    const body = await response.json();
    if (!response.ok) {
      throw new Error(`'${table}' 읽기 실패(${response.status}): ${body.error?.message ?? "unknown"}`);
    }

    for (const document of body.documents ?? []) {
      const row = {};
      for (const [key, field] of Object.entries(document.fields ?? {})) row[key] = unwrap(field);
      rows.push(row);
    }
    pageToken = body.nextPageToken ?? "";
  } while (pageToken);

  return rows
    .filter((r) => Number.isInteger(Number(r.id)))
    .sort((a, b) => Number(a.id) - Number(b.id));
}

async function main() {
  const env = process.argv[2];
  if (env !== "test" && env !== "live") {
    console.error("사용법: node scripts/check-pack-spec.js <test|live>");
    process.exit(1);
  }

  const {projectId, apiKey} = readConfig();
  const idToken = await signInAnonymously(apiKey);

  // env 가 곧 런모드다 — ContentProfileConfig.CloudEnvId 가 runMode 에서 파생된다.
  const cardTable = env === "test" ? "Card_Test" : "Card";
  const [packRows, dropRows, cardRows, gradeRows] = await Promise.all(
    ["CardPack", "CardPackDrop", cardTable, "RankGrade"]
      .map((t) => readRows(projectId, idToken, env, t)));

  console.log(`env=${env}  CardPack=${packRows.length}  CardPackDrop=${dropRows.length}  ` +
    `${cardTable}=${cardRows.length}  RankGrade=${gradeRows.length}`);

  let failures = 0;
  const fail = (message) => {
    failures++;
    console.log(`  X ${message}`);
  };

  // 카탈로그 재현: live 는 channel=="Live" 만, test 는 전 행(Test 프로필 includeTestCards=1).
  const catalogIds = new Set();
  for (const row of cardRows) {
    const id = Number(row.id);
    if (!Number.isInteger(id) || id <= 0) continue;
    if (env === "live" && String(row.channel ?? "") !== "Live") continue;
    catalogIds.add(id);
  }
  console.log(`카탈로그 ${catalogIds.size}장`);

  const entryPoints = entryPointsFromRows(gradeRows.map((r) => ({
    id: Number(r.id), gradeKey: String(r.gradeKey ?? ""), entryPoints: Number(r.entryPoints ?? Number.NaN),
  })));
  if (entryPoints === null) {
    fail(`RankGrade 표를 못 읽는다 — 서버가 폴백 상수 [${FALLBACK_ENTRY_POINTS}] 로 돈다. ` +
      "클라 RankConfig.asset 과 어긋나면 팩 잠금이 표시와 갈린다.");
  } else {
    console.log(`RankGrade 임계치 [${entryPoints}]`);
    if (String(entryPoints) !== String(FALLBACK_ENTRY_POINTS)) {
      fail(`시트 임계치가 서버 폴백 상수 [${FALLBACK_ENTRY_POINTS}] 와 다르다 — ` +
        "둘 중 무엇이 옳은지 정하고 RankConfig.asset 까지 셋을 맞춰라.");
    }
  }
  const thresholds = entryPoints ?? FALLBACK_ENTRY_POINTS;

  if (packRows.length === 0) fail("CardPack 표가 비었다 — 어떤 팩도 서버에서 못 연다.");

  console.log("\n팩별 풀 크기 (등급 Bronze→Diamond):");
  for (const pack of packRows) {
    const packId = String(pack.packId ?? "");
    const drops = dropRows
      .filter((r) => String(r.packId ?? "") === packId)
      .map((r) => ({
        id: Number(r.id), packId, minGrade: String(r.minGrade ?? ""),
        cardId: Number(r.cardId ?? 0), weight: Number(r.weight ?? 0),
      }));

    const sizes = GRADE_KEYS.map((_, grade) => resolveDropPool(drops, grade, catalogIds).length);
    const required = parseRequiredGrade(String(pack.minRankGrade ?? ""));
    const lock = required === null ? "잠금없음" : `${GRADE_KEYS[required]} 이상`;
    const drawCount = Math.max(1, Number(pack.drawCount ?? 0));
    const unique = Number(pack.uniqueDraw ?? 0) !== 0;

    console.log(`  ${packId.padEnd(24)} [${sizes.join(",")}]  ${drawCount}장${unique ? "(비복원)" : ""}  ` +
      `${String(pack.priceType ?? "")} ${Number(pack.price ?? 0)}  ${lock}`);

    // 잠금이 열리는 최저 등급에서 풀이 비면 그 팩은 살 수 있는데 못 여는 상태가 된다.
    const from = required ?? 0;
    for (let grade = from; grade < GRADE_KEYS.length; grade++) {
      if (sizes[grade] === 0) {
        fail(`'${packId}' 는 ${GRADE_KEYS[grade]} 등급에서 풀이 비어 EmptyPool 로 거절된다.`);
        break;
      }
    }
    if (unique && sizes[from] > 0 && sizes[from] < drawCount) {
      console.log(`    - 비복원인데 풀(${sizes[from]})이 뽑을 장수(${drawCount})보다 작다 — 장수가 줄어든다(클라와 같은 동작).`);
    }
    // 시트에 없는 카드는 클라 PackSpec 도 버리지만, 어느 쪽이든 저작 실수다.
    const unknown = drops.filter((d) => !catalogIds.has(d.cardId)).map((d) => d.cardId);
    if (unknown.length > 0) {
      console.log(`    - 카탈로그에 없는 cardId ${[...new Set(unknown)].join(",")} — ${cardTable} 표와 대조할 것.`);
    }
  }

  // 역방향 대조: SO 에는 있는데 시트에 없는 팩. 위 순회는 시트 행만 도므로 이걸 못 본다.
  // 클라는 이런 팩을 SO 인스펙터로 열지만 서버는 SO 를 모른다 → openPack 이 PackNotFound 로 거절한다.
  const sheetIds = new Set(packRows.map((p) => String(p.packId ?? "")));
  const soRoot = path.join(__dirname, "..", "..", "Assets", "SO", "CardPack");
  const orphans = [];
  if (fs.existsSync(soRoot)) {
    const walk = (dir) => {
      for (const entry of fs.readdirSync(dir, {withFileTypes: true})) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) walk(full);
        else if (entry.name.endsWith(".asset")) {
          const found = /^\s*packId:\s*(.+)$/m.exec(fs.readFileSync(full, "utf8"));
          const id = found === null ? "" : found[1].trim();
          if (id.length > 0 && !sheetIds.has(id)) orphans.push({id, file: entry.name});
        }
      }
    };
    walk(soRoot);
  }
  if (orphans.length > 0) {
    console.log("\nSO 에만 있고 시트에 없는 팩:");
    for (const orphan of orphans) console.log(`  ! ${orphan.id.padEnd(24)} (${orphan.file})`);
    console.log("  상점·튜토리얼에 배선돼 있으면 서버가 PackNotFound 로 거절한다.");
    console.log("  아무 데서도 참조하지 않으면 죽은 저작이니 그대로 둬도 된다 —");
    console.log("  .asset.meta 의 guid 로 역참조를 세어 확인할 것.");
  }

  // 랭크 점수 몇 개로 등급 파생을 눈에 보이게 찍는다(잠금 판정의 근거).
  console.log("\n랭크 점수 → 등급:",
    [0, 99, 100, 260, 420, 580, 740].map((p) => `${p}:${GRADE_KEYS[gradeOf(thresholds, p)]}`).join("  "));

  console.log(failures === 0 ? "\ncheck-pack-spec: ok" : `\ncheck-pack-spec: 문제 ${failures}건`);
  process.exit(failures === 0 ? 0 : 1);
}

main().catch((error) => {
  console.error(error.message ?? error);
  process.exit(1);
});
