const assert = require("node:assert/strict");
const Module = require("node:module");
const {parseMissionCatalog} = require("../lib/missions/catalog");
const {missionPeriod} = require("../lib/missions/period");

// Firestore만 대체한다. 실제 index 캐시·블롭 검증·조회·수령 코드를 함께 실행한다.
const documents = new Map();
const reads = [];
let writeCount = 0;
const snapshot = path => ({exists: documents.has(path), data: () => structuredClone(documents.get(path))});
const db = {
  doc: path => ({path, get: async () => { reads.push(path); return snapshot(path); }}),
  runTransaction: async callback => {
    const writes = [];
    const transaction = {
      get: async ref => { assert.equal(writes.length, 0); return snapshot(ref.path); },
      getAll: async (...refs) => { assert.equal(writes.length, 0); return refs.map(ref => snapshot(ref.path)); },
      set: (ref, value, options) => writes.push({ref, value, options}),
    };
    const result = await callback(transaction);
    for (const {ref, value, options} of writes) {
      documents.set(ref.path, options?.merge ? {...documents.get(ref.path), ...value} : value);
      writeCount++;
    }
    return result;
  },
};
const saveDocument = (env, uid) => db.doc(`envs/${env}/users/${uid}/save/current`);
const originalLoad = Module._load;
Module._load = function(request, parent, isMain) {
  if (request === "../firebaseApp") return {db};
  if (request === "firebase-functions/logger") return {info() {}, warn() {}, error() {}};
  if (request === "../observability/analyticsEvent") return {recordEvent() {}};
  if (request === "../save/saveDocument") return {
    requireUid: auth => auth.uid,
    isKnownEnv: env => ["test", "live"].includes(env),
    saveDocument,
    mutateSave: async (env, uid, command, receipt, update, finalize) => db.runTransaction(async tx => {
      const mutation = await update(snapshot(saveDocument(env, uid).path).data() ?? {}, tx,
        {rev: 0, balances: {}, paidBalances: {}});
      return finalize({revision: 1, wallet: mutation.wallet?.next});
    }),
  };
  return originalLoad.call(this, request, parent, isMain);
};
const {readMissionCatalog} = require("../lib/missions/missionSpec");
const {specPayloadHash, clearSpecCache} = require("../lib/specs/specBlobReader");
const {getMissions} = require("../lib/commands/getMissions");
const {claimMission} = require("../lib/commands/claimMission");
Module._load = originalLoad;

const columns = ["id", "missionId", "enabled", "period", "eventKey", "targetCount", "title", "description", "passExp", "sortOrder"];
const row = (patch = {}) => ({id: 1, missionId: "daily.openPack1", enabled: 1, period: "daily",
  eventKey: "OpenPack", targetCount: 1, title: "팩 1회 개봉", description: "팩 개봉 조건", passExp: 3, sortOrder: 1, ...patch});
const milestone = row({id: 2, missionId: "daily.completeMissions1", eventKey: "CompleteDailyMissions",
  title: "일일 미션 1개 완료", passExp: 0, sortOrder: 2});
function publish(env, table, version, rows, fields = Object.keys(rows[0])) {
  const payload = JSON.stringify([fields, ...rows.map(r => fields.map(key => String(r[key])))]);
  const payloadHash = specPayloadHash(payload);
  const blobPath = `envs/${env}/specs/${table}/releases/${version}`;
  documents.set(blobPath, {major: 4, payload, payloadHash, rowCount: rows.length});
  const indexPath = `envs/${env}/specs/_index`;
  const previous = documents.get(indexPath);
  documents.set(indexPath, {major: 4, minor: version,
    tables: {...previous?.tables, [table]: {blobPath, payloadHash}}});
}
function seed(env) {
  publish(env, "Card", 1, [{id: 1, name: "card", synergies: ""}]);
  publish(env, "Reward", 1, [{id: 1, ownerType: "Mission", ownerId: "daily.openPack1", order: 1,
    rewardType: "Currency", rewardId: "Gold", amount: 10}]);
  publish(env, "Mission", 1, [row(), milestone], columns);
  const period = missionPeriod(Date.now());
  documents.set(`envs/${env}/users/me/missions/current`, {
    dailyKey: period.daily, weeklyKey: period.weekly, progress: {"daily.OpenPack": 1}, claimed: {}, passExp: 0,
  });
}
const callGet = env => getMissions.run({auth: {uid: "me"}, data: {env}});
const callClaim = env => claimMission.run({auth: {uid: "me"}, data: {env, missionId: "daily.openPack1"}});

async function main() {
  assert.throws(() => parseMissionCatalog([]), /no rows/);
  for (const patch of [{enabled: 2}, {enabled: ""}, {period: "monthly"}, {period: "toString"},
    {missionId: "weekly.wrong"}, {eventKey: ""}, {targetCount: 0}, {targetCount: ""},
    {targetCount: 1.5}, {targetCount: Number.MAX_SAFE_INTEGER + 1}, {passExp: ""},
    {passExp: -1}, {sortOrder: 0}, {title: ""}, {description: ""}]) {
    assert.throws(() => parseMissionCatalog([row(patch)]), /Mission/);
  }
  assert.throws(() => parseMissionCatalog([row(), row()]), /Duplicated/);
  const originalNow = Date.now;
  let now = Date.parse("2026-09-10T03:00:00Z");
  Date.now = () => now;
  try {
    seed("test"); seed("live");
    const originalIndex = structuredClone(documents.get("envs/test/specs/_index"));
    const first = await callGet("test");
    assert.equal(first.definitions[0].title, "팩 1회 개봉");
    assert.equal(first.missions.progress["daily.CompleteDailyMissions"], 1);

    publish("test", "Mission", 2, [row({title: "팩 2회 개봉", targetCount: 2, passExp: 17}), milestone], columns);
    assert.equal((await callGet("test")).definitions[0].target, 1, "index TTL 동안 이전 발행본 유지");
    now += 30001;
    const updated = await callGet("test");
    assert.equal(updated.definitions[0].title, "팩 2회 개봉");
    assert.equal(updated.definitions[0].reward.passExp, 17);
    assert.equal(updated.missions.progress["daily.CompleteDailyMissions"], 0);
    const writesBeforeReject = writeCount;
    await assert.rejects(callClaim("test"), /NotEligible/, "수령 판정도 변경된 목표를 사용");
    assert.equal(writeCount, writesBeforeReject, "미달성 수령은 쓰기 없음");
    assert.equal((await callGet("live")).definitions[0].target, 1, "환경별 정의 격리");
    assert.equal((await callGet("test")).definitions[0].target, 2, "다른 환경 조회가 정의를 덮지 않음");

    documents.get("envs/test/users/me/missions/current").progress["daily.OpenPack"] = 2;
    const claim = await callClaim("test");
    assert.equal(claim.grantedPassExp, 17);
    assert.deepEqual(claim.granted, [{currency: "Gold", amount: 10}]);
    assert.equal(claim.missions.progress["daily.CompleteDailyMissions"], 1);
    await assert.rejects(callClaim("test"), /AlreadyClaimed/);

    publish("test", "Mission", 3, [row({enabled: 0}), milestone], columns);
    now += 30001;
    assert.equal((await callGet("test")).definitions.some(m => m.id === "daily.openPack1"), false);
    await assert.rejects(callClaim("test"), /MissionDisabled/);

    documents.set("envs/test/specs/_index", originalIndex);
    now += 30001;
    assert.equal((await callGet("test")).definitions[0].title, "팩 1회 개봉", "index 롤백 반영");
    assert.equal((await callGet("test")).missions.claimed["daily.openPack1"], true, "롤백 뒤 수령 낙인 보존");
    assert.ok(reads.some(path => path.includes("Mission/releases/2")));
    assert.equal(reads.some(path => path.includes("blob/current")), false, "가변 블롭 우회 없음");

    delete documents.get("envs/test/specs/_index").tables.Mission;
    now += 30001;
    await assert.rejects(readMissionCatalog("test"), /no Mission entry/, "누락 시 이전 카탈로그로 폴백하지 않음");
    publish("test", "Mission", 4, [row({targetCount: ""})], columns);
    now += 30001;
    await assert.rejects(callGet("test"), /Invalid Mission spec/);
    assert.equal((await callGet("live")).definitions[0].title, "팩 1회 개봉");
  } finally {
    Date.now = originalNow;
    clearSpecCache();
  }
  console.log("mission spec: validation, published index/TTL/rollback, environment isolation, display and claim parity OK");
}
main().catch(error => {console.error(error); process.exitCode = 1;});
