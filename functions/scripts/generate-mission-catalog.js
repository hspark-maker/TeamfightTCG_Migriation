// Mission CSV가 값의 진실원이다. 서버 번들 사본은 빌드마다 다시 만든다.
const fs = require("node:fs");
const path = require("node:path");
const source = fs.readFileSync(path.join(__dirname, "../../docs/SpecData/Mission_sheet.csv"), "utf8");
const records = [];
let row = [], cell = "", quoted = false;
for (let i = 0; i < source.length; i++) {
  const ch = source[i];
  if (ch === '"') {
    if (quoted && source[i + 1] === '"') { cell += '"'; i++; } else quoted = !quoted;
  } else if (!quoted && (ch === "," || ch === "\n")) {
    row.push(cell.replace(/\r$/, "")); cell = "";
    if (ch === "\n") { records.push(row); row = []; }
  } else cell += ch;
}
if (cell || row.length) { row.push(cell.replace(/\r$/, "")); records.push(row); }
if (quoted) throw new Error("Unterminated Mission CSV field");
const columns = records[1];
const data = records.slice(3).filter((entry) => entry[0]).map((entry) => {
  const r = Object.fromEntries(columns.map((key, i) => [key, entry[i]]));
  return {id: r.missionId, enabled: Number(r.enabled) === 1, period: r.period, event: r.eventKey,
    target: Number(r.targetCount), title: r.title, description: r.description,
    passExp: Number(r.passExp), sortOrder: Number(r.sortOrder)};
});
const output = "/* eslint-disable */\n// Generated from docs/SpecData/Mission_sheet.csv. Do not edit.\n" +
  'import type {MissionDef} from "./catalog";\n' +
  "export const GENERATED_MISSIONS: MissionDef[] = " + JSON.stringify(data, null, 2) + ";\n";
const destination = path.join(__dirname, "../src/missions/catalogData.ts");
if (!fs.existsSync(destination) || fs.readFileSync(destination, "utf8") !== output) fs.writeFileSync(destination, output);
