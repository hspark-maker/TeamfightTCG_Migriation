// Projection only. Source ranks, seasons, wallets and saves are never changed.
const assert = require("node:assert/strict");
const {isDeepStrictEqual} = require("node:util");
const {client, valueOf} = require("./publish-local-spec");
const {publicRankProfile} = require("../lib/rank/publicProfile");
const DATABASE = "projects/bm-cardbattle/databases/cardbattle/documents";
const pattern = /^envs\/(live|test)\/users\/([^/]+)\/rank\/current$/;

async function backfillRankings(request, apply) {
  let cursor;
  let scanned = 0;
  let changed = 0;
  const seasons = {};
  for (;;) {
    const page = await request(`${DATABASE}:runQuery`, {structuredQuery: {
      from: [{collectionId: "rank", allDescendants: true}],
      orderBy: [{field: {fieldPath: "__name__"}, direction: "ASCENDING"}], limit: 100,
      ...(cursor ? {startAt: {values: [{referenceValue: cursor}], before: false}} : {}),
    }});
    const docs = page.filter(row => row.document).map(row => row.document);
    if (!docs.length) break;
    cursor = docs[docs.length - 1].name;
    const names = docs.map(doc => doc.name).filter(name => pattern.test(name.slice(DATABASE.length + 1)));
    scanned += names.length;
    for (const doc of docs) {
      const match = pattern.exec(doc.name.slice(DATABASE.length + 1));
      if (match) {
        const key = `${match[1]}:${doc.fields?.seasonId?.stringValue || "unassigned"}`;
        seasons[key] = (seasons[key] || 0) + 1;
      }
    }
    if (!apply || !names.length) continue;
    for (let attempt = 0; ; attempt++) {
      const {transaction} = await request(`${DATABASE}:beginTransaction`, {});
      try {
        // Re-read both sources and projection. Concurrent rank/profile changes force a retry.
        const target = name => name.replace(/\/users\/([^/]+)\/rank\/current$/, "/rankings/$1");
        const save = name => name.replace(/\/rank\/current$/, "/save/current");
        const snapshots = await request(`${DATABASE}:batchGet`, {
          documents: [...names, ...names.map(save), ...names.map(target)], transaction,
        });
        const byName = new Map(snapshots.filter(row => row.found).map(row => [row.found.name, row.found]));
        const writes = [];
        for (const name of names) {
          const source = byName.get(name);
          if (!source) continue;
          const fields = source.fields;
          const seasonId = fields?.seasonId?.stringValue;
          const points = Number(fields?.points?.integerValue);
          assert(typeof seasonId === "string" && Number.isSafeInteger(points) && points >= 0, "Invalid rank source; no writes committed");
          const savedProfile = byName.get(save(name))?.fields?.profile?.mapValue?.fields;
          const profile = valueOf(publicRankProfile(Object.fromEntries(
            ["nickname", "avatarId", "frameId"].map(key => [key, savedProfile?.[key]?.stringValue])
          )));
          const current = byName.get(target(name))?.fields;
          if (current?.seasonId?.stringValue === seasonId && Number(current?.points?.integerValue) === points &&
              isDeepStrictEqual(current?.profile, profile)) continue;
          writes.push({update: {name: target(name), fields: {
            seasonId: {stringValue: seasonId}, points: {integerValue: String(points)},
            profile,
            ...(fields.updatedAt ? {updatedAt: fields.updatedAt} : {}),
          }}});
        }
        if (writes.length) await request(`${DATABASE}:commit`, {transaction, writes});
        else await request(`${DATABASE}:rollback`, {transaction});
        changed += writes.length;
        break;
      } catch (error) {
        await request(`${DATABASE}:rollback`, {transaction}).catch(() => {});
        if (attempt >= 2 || !/409|ABORTED|aborted/i.test(error.message)) throw error;
      }
    }
  }
  return {mode: apply ? "apply" : "plan", scanned, changed, seasons};
}
async function main() {
  const result = await backfillRankings(await client(), process.argv.includes("--apply"));
  console.log(JSON.stringify(result));
}
if (require.main === module) {
  main().catch(error => {console.error(error.message); process.exitCode = 1;});
}
module.exports = {backfillRankings};
