const fs = require("node:fs");
const path = require("node:path");
const {parseMissionCatalog} = require("../lib/missions/catalog");

// 테스트만 CSV를 읽는다. 배포 코드의 정의는 발행된 Mission 블롭에서 온다.
function readSheet(name) {
  const source = fs.readFileSync(path.join(__dirname, "../../docs/SpecData", `${name}_sheet.csv`), "utf8");
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
  if (quoted) throw new Error(`Unterminated ${name} CSV field`);
  const columns = records[1];
  return records.slice(3).filter(entry => entry[0]).map(entry => {
    if (entry.length !== columns.length) throw new Error(`Invalid ${name} CSV row`);
    return Object.fromEntries(columns.map((key, i) => [key, entry[i]]));
  });
}

module.exports = {readSheet, catalog: parseMissionCatalog(readSheet("Mission"))};
