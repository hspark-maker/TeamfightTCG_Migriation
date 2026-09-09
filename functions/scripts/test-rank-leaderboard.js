const assert = require("node:assert/strict");
const Module = require("node:module");
const originalLoad = Module._load;
let data = [];
let state = {seasonId: "S1", points: 500, bestTierIndex: 1, claimed: {}};
const queries = [];
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
  getAll: async (...refs) => refs.map(ref => ({data: () => ({profile: {
    nickname: ref.path.split("/")[3], avatarId: "avatar", frameId: "frame", secret: "never return",
  }, ownership: {secret: true}})})),
};
Module._load = function(request, parent, isMain) {
  if (parent?.filename.endsWith("getRankLeaderboard.js")) {
    if (request === "firebase-functions/v2/https") return {HttpsError, onCall: fn => fn};
    if (request === "../firebaseApp") return {db};
    if (request === "../payout") return {parseRankGradeRows: () => [{entryPoints: 100}]};
    if (request === "../rank/rankSeason") return {currentRankSeason: () => ({seasonId: "S1", endAtMs: 12345})};
    if (request === "../rank/rankStore") return {ensureRankState: async (_db, env, uid) => {
      if (!state) return null;
      data = data.filter(row => row.env !== env || row.uid !== uid);
      data.push({env, uid, ...state});
      return state;
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
  data = Array.from({length: 110}, (_, i) => ({env: "live", seasonId: "S1", uid: `user${i}`, points: 2000 - i * 10}));
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
  state = {...state, points: 0};
  result = await getRankLeaderboard(request);
  assert.equal(result.self.rank, 0);
  assert.equal(result.entries.some(row => row.isSelf), false);
  state = null;
  await assert.rejects(getRankLeaderboard(request), {code: "failed-precondition"});
  await assert.rejects(getRankLeaderboard({data: {env: "live"}}), {code: "unauthenticated"});
  await assert.rejects(getRankLeaderboard({auth: {uid: "me"}, data: {env: "other"}}), {code: "invalid-argument"});

  const {writeRank} = require("../lib/rank/rankStore");
  function doc(path) {
    const parts = path.split("/");
    return {path, id: parts.at(-1), parent: {parent: parts.length > 2 ? doc(parts.slice(0, -2).join("/")) : null},
      collection: name => ({doc: id => doc(`${path}/${name}/${id}`)})};
  }
  for (const env of ["live", "test"]) {
    const writes = [];
    const rank = {seasonId: "S1", points: 234, bestTierIndex: 2, claimed: {}};
    writeRank({set: (ref, value) => writes.push({path: ref.path, value})}, doc(`envs/${env}/users/me/rank/current`), rank, "now");
    assert.deepEqual(writes.map(write => write.path), [`envs/${env}/users/me/rank/current`, `envs/${env}/rankings/me`]);
    assert.deepEqual(writes[1].value, {seasonId: "S1", points: 234, updatedAt: "now"});
  }
  console.log("rank leaderboard: ties, top100, self, env/season, privacy and atomic projection OK");
}
main().catch(error => {console.error(error); process.exitCode = 1;});
