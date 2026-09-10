// Real commands, draw/growth rules, mission stores and mutateSave receipts. Only I/O is replaced.
const assert = require("node:assert/strict");
const Module = require("node:module");
const {HttpsError} = require("firebase-functions/v2/https");
const {missionPeriod} = require("../lib/missions/period");
const period = missionPeriod(Date.now());
const root = "envs/test/users/me";
const SAVE = `${root}/save/current`;
const WALLET = `${root}/wallet/current`;
const MISSIONS = `${root}/missions/current`;
const PASS = `${root}/pass/current`;
const documents = new Map();
let attempts = [], active = false, abort = false, retry, specs;
const ref = path => ({path, collection: name => ref(`${path}/${name}`), doc: name => ref(`${path}/${name}`)});
const db = {doc: ref, collection: ref};
const copy = value => structuredClone(value);
const catalog = [
  {id: "daily.test", event: "OpenPack", period: "daily", target: 1, passExp: 5, enabled: true, sortOrder: 1},
  {id: "guide.test", event: "Guide.CaretakerCardsAtStar1", period: "guide", target: 1, enabled: true, sortOrder: 1},
];
async function readSpecRows(env, table) {
  assert.equal(active, false, `spec ${table} must be loaded outside transaction`);
  assert.equal(env, "test");
  return copy(specs[table] ?? []);
}
async function runTransaction(source, run) {
  for (let i = 0; i < 2; i++) {
    const pending = [];
    const reads = [];
    const read = reference => {
      assert.equal(pending.length, 0, "Firestore reads must precede all writes");
      reads.push(reference.path);
      const value = copy(documents.get(reference.path));
      return {exists: value !== undefined, data: () => value};
    };
    const write = kind => (reference, value, options) =>
      pending.push({kind, path: reference.path, value: copy(value), options});
    const tx = {get: async reference => read(reference), getAll: async (...refs) => refs.map(read),
      set: write("set"), create: write("create"), update: write("update")};
    active = true;
    let response;
    try {response = await run(tx);} finally {active = false;}
    attempts.push({source, reads, writes: pending, response});
    if (retry && i === 0) {const change = retry; retry = undefined; change(); continue;}
    if (abort) {abort = false; throw Error("injected commit failure");}
    for (const entry of pending) {
      if (entry.kind === "create") assert.equal(documents.has(entry.path), false);
      const previous = entry.kind === "update" || entry.options?.merge ? documents.get(entry.path) : {};
      documents.set(entry.path, {...previous, ...entry.value});
    }
    return response;
  }
  assert.fail("retry exhausted");
}
const originalLoad = Module._load;
Module._load = function(request, parent, isMain) {
  if (request.endsWith("/firebaseApp")) return {db};
  if (request.endsWith("/countedTransaction")) return {withCountedTransaction: runTransaction};
  if (request.endsWith("/specBlobReader")) return {readSpecRows};
  if (request.endsWith("/missionSpec")) return {readMissionCatalog: async () => {
    assert.equal(active, false); return catalog;
  }};
  if (request.endsWith("/analyticsEvent")) return {recordEvent() {}};
  if (request === "firebase-functions/v2/https") return {HttpsError, onCall: (...args) => args.at(-1)};
  if (request === "firebase-functions/logger") return {info() {}, warn() {}, error() {}};
  if (request === "firebase-admin/firestore") return {FieldValue: {serverTimestamp: () => "server-time"}};
  return originalLoad.call(this, request, parent, isMain);
};
let commands, SCHEMA_VERSION;
try {
  commands = Object.fromEntries(["openPack", "claimReward", "claimMission", "claimPassReward", "spinRoulette", "limitBreakCard"]
    .map(name => [name, require(`../lib/commands/${name}`)[name]]));
  ({SCHEMA_VERSION} = require("../lib/save/saveDocument"));
} finally {Module._load = originalLoad;}

function seed() {
  documents.clear(); attempts = []; abort = false; retry = undefined;
  documents.set(SAVE, {schemaVersion: SCHEMA_VERSION, revision: 10,
    ownership: {cardIds: [1]}, cardGrowth: {entries: {1: {level: 2, snack: 1, limitBreak: 0}}},
    adventure: {pendingRewardNodeId: "node_01", clearedNodeIds: []}});
  documents.set(WALLET, {rev: 4, balances: {Gold: 100, RouletteTicket: 10}, paidBalances: {}});
  documents.set(MISSIONS, {dailyKey: period.daily, weeklyKey: period.weekly,
    progress: {"daily.OpenPack": 1}, claimed: {}, passExp: 0});
  documents.set(PASS, {seasonId: "S1", exp: 10, claimed: {}});
  specs = {
    CardEnhanceRule: [{maxLevel: 4, maxLimitBreak: 3}],
    CardLimitBreak: [1, 2, 3].map(stage => ({id: stage, stage, hpGain: stage * 10, snackCost: stage + 1})),
    Card: [{id: 1, grade: "Rare", synergies: "Data_Synergy_Caretaker", channel: "Live"}],
    CardPack: [{id: 1, packId: "P", price: 10, priceType: "Gold", drawCount: 1, uniqueDraw: 0,
      sortOrder: 1, minRankGrade: "Bronze", channel: "Live"}],
    CardPackDrop: [{id: 1, packId: "P", minGrade: "Bronze", cardId: 1, weight: 1}],
    RankGrade: ["Bronze", "Silver", "Gold", "Platinum", "Diamond"]
      .map((gradeKey, id) => ({id, gradeKey, entryPoints: id * 100})),
    AdventureChapter: [{id: 1, chapterId: "chapter_01", nodeId: "node_01", order: 1}],
    PassSeason: [{id: 1, seasonId: "S1", startAtMs: 0, endAtMs: 4102444800000, maxLevel: 1}],
    PassLevel: [{id: 1, seasonId: "S1", level: 1, requiredExp: 0}],
    Reward: [
      {id: 1, ownerType: "Adventure", ownerId: "node_01", order: 1, rewardType: "Pack", rewardId: "P", amount: 1},
      {id: 2, ownerType: "Mission", ownerId: "daily.test", order: 1, rewardType: "Card", rewardId: "1", amount: 1},
      {id: 3, ownerType: "Pass", ownerId: "S1:1", order: 1, rewardType: "PackChoice", rewardId: "UnlockedThemePack", amount: 1},
      {id: 4, ownerType: "CardDuplicate", ownerId: "Rare", order: 1, rewardType: "Currency", rewardId: "Shard", amount: 2},
    ],
    Roulette: [{id: 1, rouletteId: "R", priceType: "RouletteTicket", price: 1}],
    RouletteSlot: Array.from({length: 8}, (_, slotIndex) => ({id: slotIndex + 1, rouletteId: "R", slotIndex,
      rewardType: "Pack", rewardId: "P", amount: 1, weight: 1})),
  };
}
const params = {
  openPack: {packId: "P"}, claimReward: {ownerType: "Adventure", ownerId: "node_01"},
  claimMission: {missionId: "daily.test"}, claimPassReward: {level: 1, selectedPackId: "P"},
  spinRoulette: {rouletteId: "R"}, limitBreakCard: {cardId: 1},
};
const invoke = (name, txId = `snack-${name}`) => commands[name]({auth: {uid: "me"},
  data: {env: "test", ...params[name], txId}});
const metadata = {fromStage: 0, toStage: 1, hpGain: 10, snackCost: 2, snackLeft: 0};
const automatic = Object.keys(params).filter(name => name !== "limitBreakCard");
async function main() {
  for (const name of automatic) {
    seed();
    const result = await invoke(name);
    assert.deepEqual(result.cards[0].snackGrowth, metadata, name);
    assert.deepEqual(documents.get(SAVE).cardGrowth.entries[1], {level: 2, snack: 0, limitBreak: 1}, name);
    assert.equal(result.missions.progress["daily.LimitBreakCard"], 1, name);
    assert.equal(result.missions.progress["weekly.LimitBreakCard"], 1, name);
    assert.equal(result.missions.progress["guide.Guide.CaretakerCardsAtStar1"], 1, name);
    assert.equal(result.wallet.balances.Gold, name === "openPack" ? 90 : 100, "no automatic Gold enhance charge");
    assert.equal(result.wallet.balances.Shard, 2, "duplicate currency preserved exactly once");
    assert.equal(documents.get(SAVE).revision, 11);
    const writes = attempts.at(-1).writes;
    assert.equal(writes.filter(write => write.path === MISSIONS).length, 1, "mission state written once");
    assert.ok(writes.some(write => write.path === SAVE));
    assert.ok(writes.some(write => write.path.startsWith(`${WALLET}/receipts/`)));
    if (name !== "openPack" && name !== "claimMission") assert.deepEqual(result.packs[0].cards[0], result.cards[0]);
    const committed = copy(documents);
    const replay = await invoke(name);
    assert.deepEqual(replay.cards, result.cards, "real receipt retains original per-draw metadata");
    assert.deepEqual(replay.missions, result.missions);
    assert.deepEqual(documents, committed, "replay writes nothing");
    assert.equal(attempts.at(-1).writes.length, 0);

    seed();
    const before = copy(documents);
    abort = true;
    await assert.rejects(invoke(name), /injected commit failure/);
    assert.deepEqual(documents, before, "failed transaction rolls back snacks, stage, currency, claims and missions");

    seed();
    retry = () => {
      documents.get(SAVE).cardGrowth.entries[1] = {level: 2, snack: 2, limitBreak: 1};
      documents.get(MISSIONS).progress["daily.LimitBreakCard"] = 1;
      documents.get(MISSIONS).progress["weekly.LimitBreakCard"] = 1;
    };
    const retried = await invoke(name);
    assert.deepEqual(attempts[0].response.cards[0].snackGrowth, metadata);
    assert.deepEqual(retried.cards[0].snackGrowth, {fromStage: 1, toStage: 2, hpGain: 20, snackCost: 3, snackLeft: 0});
    assert.equal(retried.missions.progress["daily.LimitBreakCard"], 2, "retry counts only committed growth");
    assert.deepEqual(documents.get(SAVE).cardGrowth.entries[1], {level: 2, snack: 0, limitBreak: 2});

    for (const mode of ["missing-rule", "missing-curve", "hole"]) {
      seed();
      if (mode === "missing-rule") specs.CardEnhanceRule = [];
      if (mode === "missing-curve") specs.CardLimitBreak = [];
      if (mode === "hole") specs.CardLimitBreak.splice(1, 1);
      const unchanged = copy(documents);
      await assert.rejects(invoke(name), error => error.code === "failed-precondition" && /RuleUnavailable/.test(error.message));
      assert.equal(attempts.length, 0, "invalid growth definitions fail before any transaction");
      assert.deepEqual(documents, unchanged);
    }
  }

  seed();
  specs.CardPack[0].drawCount = 7;
  const sequence = await invoke("openPack");
  assert.deepEqual(sequence.cards.map(card => card.snackGrowth?.toStage), [1, undefined, undefined, 2, undefined, undefined, undefined]);
  assert.equal(sequence.missions.progress["daily.LimitBreakCard"], 2);
  assert.equal(sequence.updatedSlots.cardGrowth.entries[1].snack, 3);

  seed();
  documents.get(SAVE).cardGrowth.entries[1].snack = 8;
  const multi = await invoke("openPack");
  assert.deepEqual(multi.cards[0].snackGrowth, {fromStage: 0, toStage: 3, hpGain: 60, snackCost: 9, snackLeft: 0});
  assert.equal(multi.missions.progress["daily.LimitBreakCard"], 3);
  const capped = await invoke("openPack", "snack-capped");
  assert.equal(capped.cards[0].snackGrowth, undefined);
  assert.equal(capped.updatedSlots.cardGrowth.entries[1].snack, 1);
  assert.equal(capped.missions.progress["daily.LimitBreakCard"], 3);

  seed();
  documents.get(SAVE).ownership.cardIds = [];
  documents.get(SAVE).cardGrowth.entries[1].snack = 99;
  const fresh = await invoke("openPack");
  assert.equal(fresh.cards[0].isNew, true);
  assert.equal(fresh.cards[0].snackGrowth, undefined);
  assert.equal(fresh.updatedSlots.cardGrowth.entries[1].snack, 99);
  assert.equal(fresh.missions.progress["daily.LimitBreakCard"], undefined);

  seed();
  documents.get(SAVE).cardGrowth.entries[1].snack = 2;
  const manual = await invoke("limitBreakCard");
  assert.equal(manual.stage, 1);
  assert.equal(manual.hpGain, metadata.hpGain);
  assert.equal(manual.snackCost, metadata.snackCost);
  assert.equal(manual.snackLeft, metadata.snackLeft);
  assert.equal(manual.missions.progress["daily.LimitBreakCard"], 1);
  console.log("PASS snack growth commands: all 5 grant paths, real receipts, rollback/retry, spec I/O order, guide/missions, manual parity");
}
main().catch(error => {console.error(error); process.exitCode = 1;});
