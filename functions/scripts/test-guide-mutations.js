const assert = require("node:assert/strict");
const Module = require("node:module");
const {missionPeriod} = require("../lib/missions/period");
const {missionBumpFromSnapshot} = require("../lib/missions/missionStore");

// Real guide evaluator, mission store, callables and trigger; only external
// services and unrelated pack/enhancement arithmetic are replaced. mutateSave's
// actual receipt transaction is separately exercised by test-batched-save.
const cards = [1, 2, 3, 4, 5, 6].map(id => ({id, name: `card-${id}`,
  synergies: id === 1 ? "Data_Synergy_Caretaker" : "", grade: "Common"}));
const catalog = ["Guide.DeckSaved6", "Guide.CaretakerCardsAtStar1", "Guide.AdventureNode1"]
  .map((event, index) => ({id: `guide.${index}`, event, period: "guide", target: 1,
    title: event, description: event, passExp: 0, sortOrder: index, enabled: true}));
const missionPath = "envs/test/users/alice/missions/current";
const documents = new Map();
const receipts = new Map();
let current;
let reads = [];
let writes = [];
let cardSpecMode = "available";
let warnings = 0;
let succeeds = true;
let triggerOptions;
const now = Date.UTC(2026, 8, 10, 12);
const originalNow = Date.now;
Date.now = () => now;
const period = missionPeriod(now);
const copy = value => structuredClone(value);
const snapshot = value => ({exists: value !== undefined, data: () => value});
const db = {doc: path => ({path}), runTransaction: transaction};
async function transaction(run) {
  const pending = [];
  const read = refs => {
    assert.equal(pending.length, 0, "all reads precede transaction writes");
    reads.push(...refs.map(ref => ref.path));
    return refs.map(ref => snapshot(documents.get(ref.path)));
  };
  const result = await run({
    get: async ref => read([ref])[0], getAll: async (...refs) => read(refs),
    set: (ref, data, options) => pending.push({path: ref.path, data, options}),
  });
  for (const write of pending) {
    documents.set(write.path, write.options?.merge ?
      {...documents.get(write.path), ...write.data} : write.data);
  }
  writes.push(...pending);
  return result;
}
async function readSpecRows(env, table) {
  assert.equal(env, "test");
  if (table === "CardEnhanceRule") return [{maxLevel: 4, maxLimitBreak: 1}];
  if (table === "CardLimitBreak") return [{stage: 1, hpGain: 10, snackCost: 100}];
  if (table !== "Card") return [];
  if (cardSpecMode === "unavailable") throw Error("injected card spec outage");
  return cardSpecMode === "empty" ? [] : cards;
}
class HttpsError extends Error {
  constructor(code, message) {super(message); this.code = code;}
}
const logger = {info() {}, error() {}, warn() {warnings++;}};
const saveMock = {
  isKnownEnv: env => env === "test", requireUid: auth => auth.uid,
  mutateSave: async (env, uid, source, receipt, mutate, finalize) => {
    const key = `${source}/${receipt.txId}`;
    if (receipts.has(key)) return receipts.get(key);
    const result = await transaction(async tx => {
      const outcome = await mutate(current, tx, {rev: 1, balances: {Gold: 100}});
      current = {...current, ...outcome.slots};
      return finalize({revision: 11, updatedSlots: outcome.slots});
    });
    receipts.set(key, result);
    return result;
  },
};
const packMocks = {
  "../packs/cardCatalog": {loadCatalogIds: async () => new Set(cards.map(card => card.id))},
  "../packs/packSpecReader": {readSpecRows,
    readCardPackRow: async () => ({price: 0, priceType: "Gold", drawCount: 1,
      uniqueDraw: false, minRankGrade: "", refundAmount: 0}),
    readDropRows: async () => [], readRankGradeRows: async () => [],
  },
  "../packs/rankGrade": {entryPointsFromRows: () => [0], gradeOf: () => 0,
    parseRequiredGrade: () => null, isRanked: () => true},
  "../packs/packDraw": {resolveDropPool: () => [{cardId: 6, weight: 1}],
    drawPack: () => [{cardId: 6, isNew: true, snack: 0}]},
  "../rank/rankStore": {rankRef: (_, env, uid) => db.doc(`envs/${env}/users/${uid}/rank/current`)},
};
const enhancementMocks = {
  "../growth/enhanceRules": {parseCardEnhanceRule: () => ({maxLevel: 10}),
    parseCardEnhanceOverrides: () => [],
    cardEnhanceStep: (_, __, level) => ({level, cost: 1, currency: "Gold", successPermille: 1000}),
    rollSucceeded: () => succeeds},
};
const originalLoad = Module._load;
Module._load = function(request, parent, isMain) {
  const filename = parent?.filename ?? "";
  if (filename.endsWith("requestMetrics.js") && request === "firebase-functions/logger") return logger;
  const isPack = filename.endsWith("openPack.js");
  const isEnhance = filename.endsWith("enhanceCard.js");
  const isTrigger = filename.endsWith("syncGuideProgress.js");
  const isHelper = filename.endsWith("guideMutation.js");
  if (isPack || isEnhance || isTrigger || isHelper) {
    const commandMocks = isPack ? packMocks : isEnhance ? enhancementMocks : {};
    if (request in commandMocks) return commandMocks[request];
    if (request === "../firebaseApp") return {db, DATABASE_ID: "test-db"};
    if (request === "../save/environments") return {isKnownEnv: env => env === "test"};
    if (request === "../save/saveDocument") return saveMock;
    if (request === "../packs/packSpecReader") return {readSpecRows};
    if (request === "../missions/missionSpec" || request === "./missionSpec") {
      return {readMissionCatalog: async () => catalog};
    }
    if (request === "../currency/walletStore") return {
      nextWallet: (state, balances) => ({next: {...state, balances}}),
    };
    if (request === "../observability/analyticsEvent") return {recordEvent() {}};
    if (request === "firebase-functions/logger") return logger;
    if (request === "firebase-functions/v2/https") return {HttpsError, onCall: (...args) => args.at(-1)};
    if (request === "firebase-functions/v2/firestore") return {
      onDocumentWritten: (options, handler) => {triggerOptions = options; return handler;},
    };
  }
  return originalLoad.call(this, request, parent, isMain);
};
let applyGuideProgress, readGuideCards, openPack, enhanceCard, syncGuideProgress;
try {
  ({applyGuideProgress, readGuideCards} = require("../lib/missions/guideMutation"));
  ({openPack} = require("../lib/commands/openPack"));
  ({enhanceCard} = require("../lib/commands/enhanceCard"));
  ({syncGuideProgress} = require("../lib/commands/syncGuideProgress"));
} finally {
  Module._load = originalLoad;
}
const request = data => ({auth: {uid: "alice"}, data: {env: "test", ...data}});
const event = (before, after) => ({params: {env: "test", uid: "alice"},
  data: {before: snapshot(before), after: snapshot(after)}});
function resetCounters() {reads = []; writes = [];}
function seed() {
  documents.clear(); receipts.clear(); resetCounters();
  current = {ownership: {cardIds: [1, 2, 3, 4, 5]},
    deck: {selectedSlot: 0, slots: [{cardIds: [1, 2, 3, 4, 5, 6]}]},
    cardGrowth: {entries: {1: {level: 1, snack: 7, limitBreak: 0}}}};
  documents.set(missionPath, {dailyKey: "old-day", weeklyKey: "old-week",
    progress: {"daily.OpenPack": 30, "weekly.OpenPack": 50, "guide.keep": 9},
    claimed: {"daily.old": true, "weekly.old": true, "guide.old": true}, passExp: 100});
}
function assertMissionWrites(count) {
  assert.equal(writes.filter(write => write.path === missionPath).length, count);
}
async function main() {
  assert.equal(triggerOptions.retry, true, "failed fallback aggregation must be retried");
  assert.deepEqual(await readGuideCards("test"), cards);
  for (const mode of ["unavailable", "empty"]) {
    cardSpecMode = mode;
    assert.deepEqual(await readGuideCards("test"), [], "optional guide specs do not block the mutation");
  }
  assert.ok(warnings >= 2, "both missing and failing guide specs leave an observable warning");
  cardSpecMode = "available";

  seed();
  const beforePack = copy(current);
  const packRequest = request({packId: "free", txId: "guide-pack"});
  const pack = await openPack(packRequest);
  assertMissionWrites(1);
  assert.equal(pack.missions.progress["guide.Guide.DeckSaved6"], 1,
    "guide evaluation sees newly granted ownership together with existing deck");
  assert.equal(pack.missions.progress["daily.OpenPack"], 1);
  assert.equal(pack.missions.progress["weekly.OpenPack"], 1);
  assert.equal(pack.missions.progress["guide.keep"], 9);
  assert.equal(pack.missions.passExp, 100);
  assert.deepEqual(pack.missions.claimed, {"guide.old": true});
  resetCounters();
  await syncGuideProgress(event(beforePack, current));
  assert.deepEqual(reads, [missionPath]);
  assertMissionWrites(0); // Unmet guide missions contain zero: absence must compare equal.
  resetCounters();
  assert.deepEqual(await openPack(packRequest), pack);
  assert.deepEqual(reads, [], "receipt replay skips callback mission reads");
  assert.deepEqual(writes, [], "receipt replay never bumps missions again");

  const beforeEnhance = copy(current);
  resetCounters();
  const enhanced = await enhanceCard(request({cardId: 1, txId: "guide-enhance"}));
  assert.equal(enhanced.outcome, "Success");
  assert.equal(enhanced.missions.progress["guide.Guide.CaretakerCardsAtStar1"], 1,
    "same command response sees the new growth slot");
  assertMissionWrites(1);
  resetCounters();
  await syncGuideProgress(event(beforeEnhance, current));
  assert.deepEqual(reads, [missionPath]);
  assertMissionWrites(0);
  succeeds = false;
  resetCounters();
  const failed = await enhanceCard(request({cardId: 1, txId: "guide-enhance-fail"}));
  assert.equal(failed.outcome, "Failed");
  assert.equal(failed.missions.progress["guide.Guide.CaretakerCardsAtStar1"], 1);
  assertMissionWrites(1);
  succeeds = true;

  // Direct client deck saves still rely on the trigger; reversed/duplicate events
  // retain high-water marks and leave existing daily/weekly counters untouched.
  seed();
  current.ownership.cardIds.push(6);
  const withoutDeck = {...copy(current), deck: {slots: []}};
  resetCounters();
  await syncGuideProgress(event(withoutDeck, current));
  assert.deepEqual(reads, [missionPath]); assertMissionWrites(1);
  assert.equal(documents.get(missionPath).progress["guide.Guide.DeckSaved6"], 1);
  assert.equal(documents.get(missionPath).progress["daily.OpenPack"], 30);
  resetCounters();
  await syncGuideProgress(event(current, withoutDeck));
  await syncGuideProgress(event(withoutDeck, current));
  assertMissionWrites(0);

  // Helper uses the full post-mutation save and preserves historical progress.
  const bump = missionBumpFromSnapshot(db.doc(missionPath), snapshot(documents.get(missionPath)), period);
  const original = copy(current);
  applyGuideProgress(bump, current, {adventure: {clearedNodeIds: ["node_1"]}}, cards, catalog);
  assert.equal(bump.state.progress["guide.Guide.AdventureNode1"], 1);
  applyGuideProgress(bump, current, {deck: {slots: []}}, cards, catalog);
  assert.equal(bump.state.progress["guide.Guide.DeckSaved6"], 1);
  assert.deepEqual(current, original, "post-mutation evaluation leaves the prior save untouched");
  const priorProgress = copy(bump.state.progress);
  applyGuideProgress(bump, current, {}, [], catalog);
  assert.deepEqual(bump.state.progress, priorProgress);

  seed();
  for (const mode of ["unavailable", "empty"]) {
    cardSpecMode = mode;
    resetCounters();
    const result = await enhanceCard(request({cardId: 1, txId: `fallback-${mode}`}));
    assert.equal(result.outcome, "Success", "guide spec failure preserves ordinary enhancement");
    assert.equal(result.missions.progress["guide.keep"], 9);
    assertMissionWrites(1);
  }
  cardSpecMode = "available";
  resetCounters();
  await syncGuideProgress(event({}, current));
  assertMissionWrites(1);
  assert.equal(documents.get(missionPath).progress["guide.Guide.CaretakerCardsAtStar1"], 1,
    "fallback trigger recovers guide progress when spec access returns");

  // Client-written nested slots can be malformed even when the enclosing save
  // passed rules. Optional inline guide evaluation must not block paid commands.
  const malformedDecks = [
    {slots: {}},
    {slots: [null]},
    {slots: [{cardIds: {}}]},
    {slots: [{cardIds: [1, 2, 3, 4, 5, 6.5]}]},
    {slots: [{cardIds: [1, 2, 3, 4, 5, "6"]}]},
  ];
  for (const [index, deck] of malformedDecks.entries()) {
    seed();
    current.deck = deck;
    current.adventure = {clearedNodeIds: {node_1: true}};
    const malformedPack = await openPack(request({packId: "free", txId: `malformed-pack-${index}`}));
    assert.equal(malformedPack.cards[0].cardId, 6, "malformed guide inputs do not block pack grants");
    assert.equal(malformedPack.missions.progress["guide.Guide.DeckSaved6"], 0,
      "malformed or non-integer deck slots never satisfy the valid-deck guide");
    assert.equal(malformedPack.missions.progress["guide.Guide.AdventureNode1"], 0,
      "an object in clearedNodeIds never awards an adventure guide");
    assertMissionWrites(1);
    const mission = documents.get(missionPath);
    mission.progress["guide.Guide.DeckSaved6"] = 1;
    mission.progress["guide.Guide.AdventureNode1"] = 1;
    resetCounters();
    const malformedEnhance = await enhanceCard(request({cardId: 1, txId: `malformed-enhance-${index}`}));
    assert.equal(malformedEnhance.outcome, "Success", "malformed guide inputs do not block enhancement");
    assert.equal(malformedEnhance.missions.progress["guide.Guide.DeckSaved6"], 1);
    assert.equal(malformedEnhance.missions.progress["guide.Guide.AdventureNode1"], 1,
      "malformed current data does not erase previously reached guide milestones");
    assert.equal(malformedEnhance.missions.progress["guide.Guide.CaretakerCardsAtStar1"], 1,
      "valid growth guide still advances despite malformed unrelated slots");
    assertMissionWrites(1);
  }
  console.log("PASS guide mutations: pack/enhance shared writes, trigger 1R0W, direct deck fallback, zero counters, receipt replay, period reset, high-water marks, optional spec outage recovery, malformed client slots");
}

main().catch(error => {console.error(error); process.exitCode = 1;})
  .finally(() => {Date.now = originalNow;});
