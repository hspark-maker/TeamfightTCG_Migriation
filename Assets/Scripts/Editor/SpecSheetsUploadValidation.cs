using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>네트워크·로그인·산출물 쓰기 없이 CSV 업로드 계획을 검증한다.</summary>
public static class SpecSheetsUploadValidation
{
    [MenuItem("Tools/Card Battle/검증/Google Sheet 업로드 계획")]
    public static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        int t_checks = 0;
        void Check(bool _ok, string _message)
        {
            if (!_ok) throw new InvalidOperationException("Sheets upload validation: " + _message);
            t_checks++;
        }
        void Reject(Action _action, string _message)
        {
            bool t_rejected = false;
            try { _action(); } catch (InvalidOperationException) { t_rejected = true; }
            Check(t_rejected, _message);
        }

        Check(SpecDocsCsvExporter.TryParseCsv("\uFEFFid,text\nint,string\n1,\"a,b\n\"\"quoted\"\"\"\n", out var t_csv) &&
            t_csv[2][1] == "a,b\n\"quoted\"", "quoted CSV/BOM/newline");
        Check(SpecDocsCsvExporter.TryParseCsv("\"\"", out var t_empty) && t_empty.Count == 1 && t_empty[0][0] == "", "empty quoted field");
        Check(!SpecDocsCsvExporter.TryParseCsv("id,text\n1,\"open", out _), "unclosed quote");
        Check(!SpecDocsCsvExporter.TryParseCsv("id,text\n1,a\"b", out _), "quote in bare field");
        Check(!SpecDocsCsvExporter.TryParseCsv("id,text\n1,\"a\"b", out _), "characters after quote");
        Check(SpecSheetsUploader.NormalizeSpreadsheetId("https://docs.google.com/spreadsheets/d/test_123/edit#gid=0") == "test_123", "URL normalization");
        Reject(() => SpecSheetsUploader.NormalizeSpreadsheetId("https://example.com/spreadsheets/d/test"), "foreign URL");
        var t_file = SpecSheetsUploader.Parse("unused", "Test", "id,value\nint,int\n1,2\n");
        Check(t_file.TypeRow == 2 && t_file.Rows[1].SequenceEqual(new[] { "id", "value" }) &&
            t_file.Rows[2].SequenceEqual(new[] { "int", "int" }), "two-header CSV normalized for ExcelUtil");
        var t_described = SpecSheetsUploader.Parse("unused", "Test", "identifier,amount\nid,value\nint,int\n1,2\n");
        Check(t_described.TypeRow == 2 && t_described.Rows.Count == 4 &&
            t_described.Rows[0][0] == "identifier", "existing descriptions preserved");
        var t_enumFile = SpecSheetsUploader.Parse("unused", "enum", "Currency,Value\nECurrencyType,value:ECurrencyType\nGold,0\n");
        Check(t_enumFile.TypeRow == 1 && t_enumFile.Rows.Count == 3, "enum retains two-header layout");
        var t_same = Remote(new[] { "id", "value" }, new[] { "int", "int" }, new[] { "1", "2" });
        SpecSheetsUploadPlan Plan(SpecSheetsCsv _file, JObject _remote)
            => SpecSheetsUploader.BuildPlan("test", new List<SpecSheetsCsv> { _file }, _remote);
        Check(Plan(t_file, t_same).ChangedCellCount == 0, "numeric strings equal numeric CSV");
        JObject t_legacy = (JObject)t_same.DeepClone();
        ((JArray)t_legacy["sheets"][0]["data"][0]["rowData"]).RemoveAt(0);
        var t_repair = Plan(t_file, t_legacy);
        Check(t_repair.CanUpload && t_repair.Requests.Any(t =>
            (int)t["updateCells"]?["range"]?["startRowIndex"] == 1 &&
            (string)t["updateCells"]?["rows"]?[0]?["values"]?[0]?["userEnteredValue"]?["stringValue"] == "id"),
            "legacy two-header remote repaired with names on row 2");
        Check(t_repair.Requests.Any(t =>
            (int)t["updateCells"]?["range"]?["startRowIndex"] == 2 &&
            (string)t["updateCells"]?["rows"]?[0]?["values"]?[0]?["userEnteredValue"]?["stringValue"] == "int"),
            "legacy type row moved to row 3");
        JObject t_formula = (JObject)t_same.DeepClone();
        t_formula["sheets"][0]["data"][0]["rowData"][3]["values"][1] = new JObject {
            ["userEnteredValue"] = new JObject { ["formulaValue"] = "=1+1" },
            ["effectiveValue"] = new JObject { ["numberValue"] = 2 } };
        Check(Plan(t_file, t_formula).ChangedCellCount == 0, "unchanged formula retained");
        var t_changed = SpecSheetsUploader.Parse("unused", "Test", "id,value\nint,int\n1,3\n");
        Check(!Plan(t_changed, t_formula).CanUpload && Plan(t_changed, t_formula).ChangedCellCount == 1, "formula overwrite blocked");
        var t_plan = Plan(t_changed, t_same);
        Check(t_plan.CanUpload && t_plan.ChangedCellCount == 1 && t_plan.Requests.Count == 1, "one changed cell");
        Check((string)t_plan.Requests[0]["updateCells"]["fields"] == "userEnteredValue", "format unchanged");
        Check((int)t_plan.Requests[0]["updateCells"]["range"]["startRowIndex"] == 3 &&
            (int)t_plan.Requests[0]["updateCells"]["range"]["startColumnIndex"] == 1, "exact cell range");
        JObject t_extra = Remote(new[] { "id", "value", "extra" }, new[] { "int", "int", "string" }, new[] { "1", "2", "old" }, new[] { "9", "99" });
        var t_clear = Plan(t_file, t_extra);
        Check(t_clear.CanUpload && t_clear.ChangedCellCount == 6, "trailing row and column values cleared");
        Check(t_clear.Requests.All(t => (string)t["updateCells"]?["fields"] == "userEnteredValue"), "clear touches only values");
        var t_new = Plan(t_file, new JObject { ["properties"] = new JObject { ["title"] = "Test doc" }, ["sheets"] = new JArray() });
        Check(t_new.CanUpload && t_new.Requests[0]["addSheet"] != null, "missing tab created atomically");
        var t_literal = SpecSheetsUploader.Parse("unused", "Test", "id,text\nint,string\n1,=1+1\n");
        var t_literalPlan = Plan(t_literal, Remote(new[] { "id", "text" }, new[] { "int", "string" }, new[] { "1", "before" }));
        Check((string)t_literalPlan.Requests[0]["updateCells"]["rows"][0]["values"][0]["userEnteredValue"]["stringValue"] == "=1+1", "CSV formula injection stays literal");
        var t_long = SpecSheetsUploader.Parse("unused", "Test", "id,value\nint,long\n1,9007199254740993\n");
        var t_longPlan = Plan(t_long, Remote(new[] { "id", "value" }, new[] { "int", "long" }, new[] { "1", "1" }));
        Check((string)t_longPlan.Requests[0]["updateCells"]["rows"][0]["values"][0]["userEnteredValue"]["stringValue"] == "9007199254740993", "large integer exact");
        Reject(() => Plan(SpecSheetsUploader.Parse("unused", "Test", "id,value\nint,int\n1,2147483648\n"), t_same), "int overflow");
        Reject(() => SpecSheetsUploader.Parse("unused", "Test", "id,value\nint,int\n"), "empty data cannot clear sheet");
        Reject(() => SpecSheetsUploader.Parse("unused", "Test", "id,value\nint,int\n1\n"), "unequal columns");
        // 실제 CSV는 읽기만 한다. 시트·bytes·CSV를 갱신하는 코드는 호출하지 않는다.
        var t_local = SpecSheetsUploader.ReadFiles(SpecSheetsUploader.GetCsvFiles());
        Check(t_local.Count > 0, "local CSV parse");
        foreach (var t_table in t_local)
        {
            if (t_table.Title != "enum")
                Check(t_table.TypeRow == 2 && t_table.Rows[1].Distinct().Count() == t_table.Rows[1].Count,
                    t_table.Title + " generator field row has unique names");
            var t_only = Plan(t_table, new JObject { ["properties"] = new JObject { ["title"] = "Offline" }, ["sheets"] = new JArray() });
            Check(t_only.CanUpload, t_table.Title + " valid upload plan");
        }
        return $"Google Sheet 업로드 오프라인 검증 통과: {t_checks}항목 / 로컬 CSV {t_local.Count}개. 원격·산출물 변경 없음.";
    }

    static JObject Remote(params string[][] _rows)
    {
        var t_rows = new JArray();
        foreach (var t_row in new[] { _rows[0] }.Concat(_rows))
            t_rows.Add(new JObject { ["values"] = new JArray(t_row.Select(t => new JObject {
                ["userEnteredValue"] = new JObject { ["stringValue"] = t } })) });
        return new JObject { ["properties"] = new JObject { ["title"] = "Test doc" }, ["sheets"] = new JArray(new JObject {
            ["properties"] = new JObject { ["title"] = "Test", ["sheetId"] = 7,
                ["gridProperties"] = new JObject { ["rowCount"] = 100, ["columnCount"] = 26 } },
            ["data"] = new JArray(new JObject { ["rowData"] = t_rows }) }) };
    }
}
