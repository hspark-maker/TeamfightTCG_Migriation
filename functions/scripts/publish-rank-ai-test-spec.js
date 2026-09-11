// Add the CSV-only RankAiEncounter table to test; preserve every existing published pin.
// Never imports or writes SpecData.bytes. plan performs reads only; apply commits one reviewed plan.
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const os = require("node:os");
const crypto = require("node:crypto");
const {client, unpack, valueOf, localSnapshot, hash, csv, checkCommit, releaseHistory} = require("./publish-local-spec");
const {parseAiDeckRows} = require("../lib/matchmaking/aiDeckDraw");
const {parseRankAiEncounters} = require("../lib/matchmaking/rankAiEncounter");
const {parseCardSpecRow, buildAiDeckSnapshots} = require("../lib/deckValidation");
const {parseCardEnhanceRule} = require("../lib/growth/enhanceRules");
const {parseLimitBreakCurve} = require("../lib/growth/limitBreakTable");
const ROOT = path.resolve(__dirname, "../..");
const DATABASE = "projects/bm-cardbattle/databases/cardbattle/documents";
const BASE = `${DATABASE}/envs/test/specs`;
const TABLE = "RankAiEncounter";
const DEPENDENCIES = ["AIDeck", "Card", "CardEnhanceRule", "CardLimitBreak"];
const fields = (doc) => Object.fromEntries(Object.entries(doc.fields || {}).map(([k,v]) => [k,unpack(v)]));
const wire = (data) => valueOf(data).mapValue.fields;
const create = (name, data) => ({update:{name,fields:wire(data)},currentDocument:{exists:false}});
const sourcePaths = [`docs/SpecData/${TABLE}_sheet.csv`, "Assets/Scripts/OutGame/Spec/RankAiEncounterRow.cs",
  "Assets/Scripts/OutGame/Spec/ContentVersion.cs", "functions/scripts/publish-rank-ai-test-spec.js"];
const sources = () => Object.fromEntries(sourcePaths.map(file => [file,crypto.createHash("sha256")
  .update(fs.readFileSync(path.join(ROOT,file))).digest("hex")]));
async function absent(request, name) {
  try { await request(name); } catch (e) { if (e.message.startsWith("Firestore 404:")) return; throw e; }
  throw new Error(`Document already exists: ${name}`);
}
function rowsOf(blob, table) {
  const matrix = JSON.parse(blob.payload);
  const schema = csv(fs.readFileSync(path.join(ROOT,`docs/SpecData/${table}_sheet.csv`),"utf8"));
  const header = schema.findIndex(row => row[0] === "id");
  assert(header >= 0);
  const names = schema[header], types = schema[header+1];
  return matrix.slice(1).map(row => Object.fromEntries(matrix[0].map((key,i) => {
    const slot = names.indexOf(key); assert(slot >= 0,`Missing CSV column: ${table}.${key}`);
    return [key,types[slot] === "string" ? row[i] : Number(row[i])];
  })));
}
function validateCompatibility(local, blobs) {
  const rows = Object.fromEntries(DEPENDENCIES.map(name => [name,rowsOf(blobs[name],name)]));
  const decks = parseAiDeckRows(rows.AIDeck).rows;
  const profiles = parseRankAiEncounters(local.tables[TABLE].rows,decks);
  const specs = new Map(rows.Card.map(row => [row.id,parseCardSpecRow(row)]));
  const curve = parseLimitBreakCurve(rows.CardLimitBreak,parseCardEnhanceRule(rows.CardEnhanceRule).maxLimitBreak);
  for (const profile of profiles) {
    const deck = decks.find(entry => entry.deckId === profile.deckId);
    assert(buildAiDeckSnapshots(deck.cardIds,profile.cardGrowth,specs,curve),`Invalid growth: ${profile.id}`);
  }
  const tiers = new Set(profiles.filter(profile => profile.battleKind === "Normal").map(profile => profile.tierIndex));
  assert.equal(tiers.size,20,"Every ranked tier requires Normal candidates");
  return {profiles:profiles.length,tiers:tiers.size,dependencyHashes:Object.fromEntries(DEPENDENCIES.map(name => [name,blobs[name].payloadHash]))};
}
function writesFor(indexDoc, local, publishedAt, publishedBy) {
  const index = fields(indexDoc), table = local.tables[TABLE];
  assert.equal(index.major,local.major); assert.equal(index.minAppMajor,local.minAppMajor);
  assert(!index.tables[TABLE],"Table already published");
  assert(Number.isSafeInteger(index.nextMinor) && index.nextMinor > index.minor);
  const minor=index.nextMinor, version=`${index.major}.${minor}`;
  const blobPath=`envs/test/specs/_release_${index.major}_${minor}_${TABLE}`;
  const blob={schemaVersion:index.major,major:index.major,revision:1,rowCount:table.rows.length,
    payloadHash:table.payloadHash,payload:table.payload};
  const tables={...index.tables,[TABLE]:{revision:1,payloadHash:table.payloadHash,blobPath}};
  const appVersion=fs.readFileSync(path.join(ROOT,"ProjectSettings/ProjectSettings.asset"),"utf8")
    .match(/^\s*bundleVersion:\s*(.+)$/m)[1].trim();
  const meta={table:TABLE,schemaVersion:index.major,major:index.major,revision:1,rowsRevision:1,
    rowCount:table.rows.length,columns:table.columns,idColumn:"id",payloadHash:table.payloadHash,
    uploadedBy:publishedBy,appVersion};
  const release={major:index.major,minor,minAppMajor:index.minAppMajor,contentVersion:version,
    noticeTitle:index.noticeTitle || "",noticeBody:index.noticeBody || ""};
  const writes=table.rows.map(row => create(`${BASE}/${TABLE}/rows/${row.id}`,row));
  const metaWrite=create(`${BASE}/${TABLE}`,meta);
  metaWrite.update.fields.updatedAt={timestampValue:publishedAt};
  writes.push(metaWrite,create(`${BASE}/${TABLE}/blob/current`,blob),create(`${DATABASE}/${blobPath}`,blob),
    create(`${BASE}/_release_index_${index.major}_${minor}`,{...release,publishedAt,publishedBy,tablesJson:JSON.stringify(valueOf(tables))}),
    {update:{name:`${BASE}/_index`,fields:wire({...index,...release,nextMinor:minor+1,
      history:releaseHistory(index.history || [],version),tables})},currentDocument:{updateTime:indexDoc.updateTime}});
  checkCommit(writes);
  return {writes,version,expectedTables:tables};
}
async function makePlan(request) {
  const local=localSnapshot(), indexDoc=await request(`${BASE}/_index`), index=fields(indexDoc);
  await absent(request,`${BASE}/${TABLE}`);
  await absent(request,`${BASE}/${TABLE}/blob/current`);
  const orphanRows=await request(`${BASE}/${TABLE}/rows?pageSize=1`);
  assert(!orphanRows.documents?.length,"Orphan RankAiEncounter rows require review");
  const blobs={};
  await Promise.all(Object.entries(index.tables).map(async ([name,pin]) => {
    assert(pin.blobPath.startsWith("envs/test/specs/_release_"),`Unsafe pin: ${name}`);
    const blob=fields(await request(`${DATABASE}/${pin.blobPath}`));
    assert.equal(hash(blob.payload),pin.payloadHash,`Remote hash: ${name}`);
    assert.equal(blob.payloadHash,pin.payloadHash); assert.equal(blob.revision,pin.revision);
    assert.equal(JSON.parse(blob.payload).length-1,blob.rowCount);
    blobs[name]=blob;
  }));
  const compatibility=validateCompatibility(local,blobs);
  const publishedAt=new Date().toISOString(),publishedBy=os.userInfo().username;
  return {kind:"rank-ai-test-add-v1",env:"test",previousVersion:index.contentVersion,publishedAt,publishedBy,
    sources:sources(),indexDoc,compatibility,...writesFor(indexDoc,local,publishedAt,publishedBy)};
}
async function applyPlan(plan,request) {
  assert.equal(plan.kind,"rank-ai-test-add-v1"); assert.equal(plan.env,"test");
  assert.equal(plan.indexDoc.name,`${BASE}/_index`);
  assert.deepEqual(plan.sources,sources(),"Plan inputs changed; create a new read-only plan");
  const local=localSnapshot();
  const expected=writesFor(plan.indexDoc,local,plan.publishedAt,plan.publishedBy);
  assert.deepEqual(plan.writes,expected.writes,"Plan writes do not match constrained generator");
  assert.deepEqual(plan.expectedTables,expected.expectedTables);
  assert.equal(plan.version,expected.version);
  await request(`${DATABASE}:commit`,{writes:plan.writes});
  // No automatic retry after an ambiguous commit. Read back pointer and payload.
  const index=fields(await request(`${BASE}/_index`));
  assert.equal(index.contentVersion,plan.version); assert.deepEqual(index.tables,plan.expectedTables);
  const blob=fields(await request(`${DATABASE}/${index.tables[TABLE].blobPath}`));
  assert.equal(blob.payload,local.tables[TABLE].payload); assert.equal(hash(blob.payload),index.tables[TABLE].payloadHash);
  const meta=fields(await request(`${BASE}/${TABLE}`));
  assert.equal(meta.rowCount,local.tables[TABLE].rows.length); assert.equal(meta.rowsRevision,1);
  return {published:plan.version,env:"test",profiles:meta.rowCount,preservedPins:Object.keys(index.tables).length-1};
}
async function main() {
  const [mode,file]=process.argv.slice(2); assert(file,"Output/plan file required");
  if (mode === "plan") {
    const plan=await makePlan(await client());
    fs.writeFileSync(file,JSON.stringify(plan,null,2),{flag:"wx"});
    console.log(JSON.stringify({from:plan.previousVersion,to:plan.version,writes:plan.writes.length,
      preservedPins:Object.keys(plan.expectedTables).length-1,compatibility:plan.compatibility,plan:path.resolve(file)},null,2));
  } else if (mode === "apply") console.log(JSON.stringify(await applyPlan(JSON.parse(fs.readFileSync(file,"utf8")),await client())));
  else throw new Error("Use: plan <new-plan.json> | apply <reviewed-plan.json>");
}
module.exports={makePlan,applyPlan,writesFor,validateCompatibility,fields};
if(require.main === module)main().catch(e=>{console.error(e.message);process.exitCode=1;});
