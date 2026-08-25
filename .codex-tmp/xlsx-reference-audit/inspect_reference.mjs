import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const sourcePath = "C:\\Users\\cookapps\\Downloads\\Goddess_Card_Arena_레벨디자인_마스터.xlsx";
const input = await FileBlob.load(sourcePath);
const workbook = await SpreadsheetFile.importXlsx(input);

const sheets = await workbook.inspect({
  kind: "sheet",
  include: "id,name",
  maxChars: 12000,
});
console.log("=== SHEETS ===");
console.log(sheets.ndjson);

const overview = await workbook.inspect({
  kind: "workbook,sheet,table,definedName",
  maxChars: 30000,
  tableMaxRows: 12,
  tableMaxCols: 12,
  tableMaxCellChars: 120,
});
console.log("=== OVERVIEW ===");
console.log(overview.ndjson);

const formulas = await workbook.inspect({
  kind: "formula",
  maxChars: 30000,
  options: { maxResults: 500 },
});
console.log("=== FORMULAS ===");
console.log(formulas.ndjson);
