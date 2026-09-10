const assert = require("node:assert/strict");
const Module = require("node:module");
const originalLoad = Module._load;
let data = [];
let state = {seasonId: "S1", points: 500, bestTierIndex: 1, claimed: {}};
const queries = [];
const profileReads = [];
const profileOf = id => ({nickname: id, avatarId: "avatar", frameId: "frame"});
class HttpsError extends Error { constructor(code, message) { super(message); this.code = code; } }
function query(path, conditions = [], size = Infinity) {
  const selected = () => data.filter(row => path === `envs/${row.env}/rankings` &&
    conditions.every(([key, op, value]) => op === "==" ? row[key] === value : op === ">" ? row[key] > value : row[key] >= value))
    .sort((a, b) => b.points - a.points || b.uid.localeCompare(a.uid));
  return {
    where: (key, op, value) => query(path, [...conditions, [key, op, value]], size),
    orderBy: (field, order) => {assert.equal(field, "points"); assert.equal(order, "desc"); return query(path, conditions, size);},
    limit: limit => query(path, conditions, limit),
    get: async () => {
      queries.push({path, conditions, size});
      return {docs: selected().slice(0, size).map(row => ({id: row.uid, data: () => row}))};
    },
    count: () => ({get: async () => ({data: () => ({count: selected().length})})}),
  };
}
const db = {
  collection: path => query(path), doc: path => ({path}),
  getAll: async (...refs) => {
    assert.deepEqual(refs.pop(), {fieldMask: ["profile"]});
    profileReads.push(...refs.map(ref => ref.path));
    return refs.map(ref => ({data: () => ({profile: {
    nickname: ref.path.split("/")[3], avatarId: "avatar", frameId: "frame", secret: "never return",
    }, ownership: {secret: true}})}));
  },
};
Module._load = function(request, parent, isMain) {
  if (parent?.filename.endsWith("getRankLeaderboard.js")) {
    if (request === "firebase-functions/v2/https") return {HttpsError, onCall: fn => fn};
    if (request === "../firebaseApp") return {db};
    if (request === "../payout") return {parseRankGradeRows: () => [{entryPoints: 100}]};
    if (request === "../rank/rankSeason") return {currentRankSeason: () => ({seasonId: "S1", endAtMs: 12345})};
    if (request === "../rank/rankStore") return {ensureRankSnapshot: async (_db, env, uid) => {
      if (!state) return null;
      data = data.filter(row => row.env !== env || row.uid !== uid);
      data.push({env, uid, ...state, profile: profileOf(uid)});
      return {state, profile: profileOf(uid)};
    }};
    if (request === "../save/saveDocument") return {
      requireUid: auth => {if (!auth?.uid) throw new HttpsError("unauthenticated", "auth"); return auth.uid;},
      isKnownEnv: env => ["live", "test"].includes(env),
    };
    if (request === "../specs/specBlobReader") return {readSpecRows: async () => []};
  }
  return originalLoad.call(this, request, parent, isMain);
};
const {getRankLeaderboard} = require("../lib/commands/getRankLeaderboard");
Module._load = originalLoad;

async function main() {
  data = Array.from({length: 110}, (_, i) => ({env: "live", seasonId: "S1", uid: `user${i}`, points: 2000 - i * 10,
    profile: {...profileOf(`user${i}`), secret: "never return"}}));
  data[1].points = data[0].points;
  data.push({env: "test", seasonId: "S1", uid: "wrongEnv", points: 99999},
    {env: "live", seasonId: "old", uid: "wrongSeason", points: 99999});
  const request = {auth: {uid: "me"}, data: {env: "live"}};
  let result = await getRankLeaderboard(request);
  assert.equal(result.entries.length, 100);
  assert.deepEqual(result.entries.slice(0, 3).map(row => row.rank), [1, 1, 3]);
  assert.equal(result.self.rank, 111);
  assert.equal(result.self.isSelf, true);
  assert.equal(result.self.points, 500);
  assert.equal(result.entries.some(row => row.nickname.startsWith("wrong")), false);
  assert.equal(JSON.stringify(result).includes("secret"), false);
  assert.equal(queries[0].size, 100);
  assert.equal(profileReads.length, 0, "projected top100 and self require no extra save reads");
  assert.equal(result.self.nickname, "me", "self profile comes from the rank transaction even outside top100");
  delete data.find(row => row.uid === "user0").profile;
  data.find(row => row.uid === "user1").profile = {nickname: 123};
  result = await getRankLeaderboard(request);
  assert.deepEqual(new Set(profileReads), new Set([
    "envs/live/users/user0/save/current", "envs/live/users/user1/save/current",
  ]), "only legacy or incomplete projections need a compatibility read");
  assert.equal(profileReads.length, 2);
  assert.deepEqual(result.entries.slice(0, 2).map(row => row.nickname).sort(), ["user0", "user1"]);
  assert.equal(JSON.stringify(result).includes("secret"), false);
  for (const id of ["user0", "user1"]) data.find(row => row.uid === id).profile = profileOf(id);
  profileReads.length = 0;
  state = {...state, points: 0};
  result = await getRankLeaderboard(request);
  assert.equal(result.self.rank, 0);
  assert.equal(result.entries.some(row => row.isSelf), false);
  assert.equal(profileReads.length, 0, "unranked self also avoids extra save reads");
  state = {...state, points: 9999};
  result = await getRankLeaderboard(request);
  assert.equal(result.self.rank, 1);
  assert.equal(result.entries[0].nickname, "me");
  assert.equal(profileReads.length, 0);
  data = [];
  state = {...state, points: 0};
  result = await getRankLeaderboard(request);
  assert.deepEqual(result.entries, []);
  assert.equal(result.self.nickname, "me");
  assert.equal(profileReads.length, 0, "empty board must not call getAll with no documents");
  state = null;
  await assert.rejects(getRankLeaderboard(request), {code: "failed-precondition"});
  await assert.rejects(getRankLeaderboard({data: {env: "live"}}), {code: "unauthenticated"});
  await assert.rejects(getRankLeaderboard({auth: {uid: "me"}, data: {env: "other"}}), {code: "invalid-argument"});

  const {writeRank, ensureRankState, canEnterFirstRank, applyTutorialRankEntry,
    FIRST_RANK_CHAPTER_INDEX} = require("../lib/rank/rankStore");
  const tutorialAsset = require("node:fs").readFileSync(require("node:path").join(__dirname,
    "../../Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset"), "utf8");
  const chapters = tutorialAsset.split(/^  - label:/m).slice(1);
  const entry = chapters.flatMap((chapter, chapterIndex) =>
    [...chapter.matchAll(/    - stepId: (\d+)\r?\n      action: (\d+)/g)]
      .map((match, chapterStepIndex) => ({chapterIndex, chapterStepIndex,
        stepId: Number(match[1]), action: Number(match[2])})))
    .filter(step => step.action === 13);
  assert.deepEqual(entry, [{chapterIndex: FIRST_RANK_CHAPTER_INDEX, chapterStepIndex: 0,
    stepId: 23, action: 13}], "server eligibility must follow authored EnterFirstRank coordinates");
  for (const tutorial of [undefined, {}, {outgameCompleted: "true"},
    {chapterIndex: 2, chapterStepIndex: 999, stepId: 999},
    {chapterIndex: "3", chapterStepIndex: 0}, {chapterIndex: 3, chapterStepIndex: -1},
    {chapterIndex: 3.5, chapterStepIndex: 0}, {chapterIndex: 3},
    {chapterIndex: Infinity, chapterStepIndex: 0}]) {
    assert.equal(canEnterFirstRank({tutorial}), false, JSON.stringify(tutorial));
  }
  assert.equal(canEnterFirstRank(null), false);
  for (const tutorial of [{chapterIndex: 3, chapterStepIndex: 0, stepId: 23},
    {chapterIndex: 3, chapterStepIndex: 1, stepId: 24},
    {chapterIndex: 5, chapterStepIndex: 5, stepId: 17}, {outgameCompleted: true}]) {
    assert.equal(canEnterFirstRank({tutorial}), true);
  }
  function doc(path) {
    const parts = path.split("/");
    return {path, id: parts.at(-1), parent: {parent: parts.length > 2 ? doc(parts.slice(0, -2).join("/")) : null},
      collection: name => ({doc: id => doc(`${path}/${name}/${id}`)})};
  }
  for (const env of ["live", "test"]) {
    const writes = [];
    const rank = {seasonId: "S1", points: 234, bestTierIndex: 2, claimed: {}};
    writeRank({set: (ref, value) => writes.push({path: ref.path, value})}, doc(`envs/${env}/users/me/rank/current`),
      rank, "now", {...profileOf("abcdefghijklmnop"), private: "secret"});
    assert.deepEqual(writes.map(write => write.path), [`envs/${env}/users/me/rank/current`, `envs/${env}/rankings/me`]);
    assert.deepEqual(writes[1].value, {seasonId: "S1", points: 234, updatedAt: "now",
      profile: profileOf("abcdefghijkl")});
    assert.equal(JSON.stringify(writes).includes("secret"), false);
  }

  const grades = [100, 500, 900].map((entryPoints, id) =>
    ({id, entryPoints, pointsPerDivision: 100, winPoints: 20, losePoints: 10}));
  const unranked = {seasonId: "S1", points: 0, bestTierIndex: -1, claimed: {}};
  const eligibleSave = {tutorial: {chapterIndex: 3, chapterStepIndex: 0, stepId: 23},
    rank: {points: 900}};
  const entered = applyTutorialRankEntry(unranked, eligibleSave, grades);
  assert.deepEqual(entered, {...unranked, points: 100, bestTierIndex: 0});
  assert.equal(applyTutorialRankEntry(entered, eligibleSave, grades), entered);
  assert.equal(applyTutorialRankEntry(unranked, {rank: {points: 900}}, grades), unranked);
  assert.equal(applyTutorialRankEntry(unranked, eligibleSave, []), unranked);
  const ranked = {...entered, points: 500, bestTierIndex: 4};
  assert.equal(applyTutorialRankEntry(ranked, eligibleSave, grades), ranked);
  for (const env of ["live", "test"]) {
    const rankPath = `envs/${env}/users/me/rank/current`;
    const payoutPath = `envs/${env}/users/me/payoutState/current`;
    const savePath = `envs/${env}/users/me/save/current`;
    const boardPath = `envs/${env}/rankings/me`;
    const canonical = {schemaVersion: 1, seasonId: "S1", points: 600, bestTierIndex: 5, claimed: {"2": true}};
    let documents;
    let writes;
    let reads;
    const reset = () => {
      documents = new Map([
        [rankPath, {...structuredClone(canonical), updatedAt: "old-rank-time"}],
        [payoutPath, {currentPoints: 600, sequence: 9, lastMatchId: "prior", updatedAt: "old-payout-time"}],
        [savePath, {rank: {points: 100, claimedTiers: [0]}}],
        [boardPath, {seasonId: "S1", points: 600, updatedAt: "old-board-time",
          profile: {nickname: "플레이어", avatarId: "", frameId: ""}}],
      ]);
      writes = [];
      reads = [];
    };
    const store = {doc, runTransaction: async run => {
      const pending = [];
      const result = await run({
        getAll: async (...refs) => {
          assert.equal(pending.length, 0, "all reads precede writes");
          return refs.map(ref => {
            reads.push(ref.path);
            const value = structuredClone(documents.get(ref.path));
            return {exists: value !== undefined, data: () => value};
          });
        },
        set: (ref, value, options) => pending.push({path: ref.path, value, options}),
      });
      for (const write of pending) {
        documents.set(write.path, write.options?.merge || write.options?.mergeFields ?
          {...documents.get(write.path), ...write.value} : write.value);
      }
      writes.push(...pending);
      return result;
    }};
    const ensure = (season = "S1") => ensureRankState(store, env, "me", season, grades);
    const stable = async (season = "S1") => {
      writes = [];
      reads = [];
      const before = new Map(documents);
      const result = await ensure(season);
      assert.equal(writes.length, 0, "unchanged rank lookup must not write");
      assert.deepEqual(documents, before, "no-op must preserve timestamps and compatibility metadata");
      assert.equal(reads.length, 4, "unchanged rank lookup reads each document exactly once");
      assert.deepEqual(new Set(reads), new Set([rankPath, payoutPath, savePath, boardPath]));
      return result;
    };
    const repaired = async (season = "S1") => {
      const result = await ensure(season);
      assert.deepEqual(writes.map(write => write.path), [rankPath, boardPath, payoutPath]);
      assert.equal(documents.get(payoutPath).currentPoints, result.points);
      assert.equal(documents.get(boardPath).points, result.points);
      assert.equal(documents.get(boardPath).seasonId, season);
      assert.deepEqual(await stable(season), result, "repair must converge to a read-only lookup");
      return result;
    };

    reset();
    assert.deepEqual(await stable(), {seasonId: "S1", points: 600, bestTierIndex: 5, claimed: {"2": true}});
    for (const missing of [true, false]) {
      reset();
      if (missing) delete documents.get(boardPath).profile;
      else documents.get(savePath).profile = {...profileOf("changed"), secret: "never return"};
      await ensure();
      assert.equal(writes.length, 1, "profile-only adoption must write only the projection");
      assert.equal(writes[0].path, boardPath);
      assert.deepEqual(Object.keys(writes[0].value), ["profile"]);
      assert.equal(JSON.stringify(documents.get(boardPath)).includes("secret"), false);
      await stable();
    }
    reset();
    assert.deepEqual(await repaired("S2"), {seasonId: "S2", points: 200, bestTierIndex: 1, claimed: {}});
    assert.equal(documents.get(payoutPath).sequence, 9, "season reset preserves payout sequencing");
    reset();
    documents.delete(rankPath);
    documents.delete(boardPath);
    assert.deepEqual(await repaired(), {seasonId: "S1", points: 600, bestTierIndex: 5, claimed: {"0": true}});
    reset();
    documents.delete(rankPath);
    documents.delete(payoutPath);
    documents.delete(boardPath);
    assert.equal((await repaired()).points, 100, "first adoption uses legacy save when payout is absent");
    reset();
    documents.set(rankPath, {...canonical, points: 0, bestTierIndex: -1, claimed: {}});
    assert.equal((await repaired()).points, 0, "legacy save/payout cannot raise an existing rank");
    for (const tutorial of [eligibleSave.tutorial, {chapterIndex: 5, chapterStepIndex: 0},
      {outgameCompleted: true}]) {
      reset();
      documents.set(rankPath, {...canonical, points: 0, bestTierIndex: -1, claimed: {}});
      documents.set(payoutPath, {currentPoints: 0});
      documents.set(savePath, {...eligibleSave, tutorial});
      assert.equal((await repaired()).points, 100, "eligible progress enters only first tier");
      assert.equal((await repaired("S2")).points, 100, "season change cannot duplicate entry points");
    }
    for (const [path, patch] of [
      [rankPath, {schemaVersion: 0}],
      [rankPath, {points: "600"}],
      [rankPath, {bestTierIndex: -1}],
      [rankPath, {claimed: {"2": true, invalid: true}}],
      [boardPath, {seasonId: "old"}],
      [boardPath, {points: 1}],
      [payoutPath, {currentPoints: 1}],
    ]) {
      reset();
      documents.set(path, {...documents.get(path), ...patch});
      await repaired();
    }
    for (const path of [boardPath, payoutPath]) {
      reset();
      documents.delete(path);
      await repaired();
    }
    reset();
    documents.delete(savePath);
    assert.equal(await ensure(), null);
    assert.equal(writes.length, 0, "missing account must not create rank documents");
  }
  console.log("rank ensure: no-op 4R/0W, migration, season reset, normalization and projection/rollback repair OK");
  console.log("rank leaderboard: ties, top100, self, env/season, privacy and atomic projection OK");
}
main().catch(error => {console.error(error); process.exitCode = 1;});
