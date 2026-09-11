using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>미리 본 CSV·원격 값과 업로드 요청을 함께 고정한다. 토큰은 보관하지 않는다.</summary>
public sealed class SpecSheetsUploadPlan
{
    public string SpreadsheetId { get; internal set; }
    public string SpreadsheetTitle { get; internal set; }
    public string Report { get; internal set; }
    public int ChangedCellCount { get; internal set; }
    public bool CanUpload { get; internal set; }
    internal List<SpecSheetsCsv> Files;
    internal string RemoteFingerprint;
    internal JArray Requests;
}

internal sealed class SpecSheetsCsv
{
    internal string Path;
    internal string Title;
    internal string Hash;
    internal List<List<string>> Rows;
    internal int TypeRow;
    internal List<string> Types;
}

/// <summary>로컬 CSV → Google Sheets 값 업로드. bytes·생성 CS·Firestore에는 접근하지 않는다.</summary>
public static class SpecSheetsUploader
{
    const string SUFFIX = "_sheet.csv";
    const string ENDPOINT = "https://sheets.googleapis.com/v4/spreadsheets/";
    const int MAX_REQUEST_BYTES = 1800000;
    static readonly HttpClient s_client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

    public static List<string> GetCsvFiles()
        => Directory.Exists(SpecDocsCsvExporter.DocsDirectory)
            ? Directory.GetFiles(SpecDocsCsvExporter.DocsDirectory, "*" + SUFFIX)
                .OrderBy(Path.GetFileName, StringComparer.Ordinal).ToList()
            : new List<string>();

    public static string ReadConfiguredSpreadsheetId()
    {
        var t_asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/CookApps/Editor/SpecDataAsset.asset");
        if (t_asset == null) return string.Empty;
        return new SerializedObject(t_asset).FindProperty("SheetId")?.stringValue ?? string.Empty;
    }

    public static string NormalizeSpreadsheetId(string _value)
    {
        string t_value = (_value ?? string.Empty).Trim();
        if (Uri.TryCreate(t_value, UriKind.Absolute, out Uri t_uri))
        {
            if (t_uri.Scheme != "https" || t_uri.Host != "docs.google.com")
                throw new InvalidOperationException("Google Sheets 문서 URL 또는 문서 ID를 입력하세요.");
            Match t_match = Regex.Match(t_uri.AbsolutePath, @"^/spreadsheets/d/([A-Za-z0-9_-]+)(?:/|$)");
            if (!t_match.Success) throw new InvalidOperationException("Google Sheets 문서 URL을 확인하세요.");
            t_value = t_match.Groups[1].Value;
        }
        if (!Regex.IsMatch(t_value, @"^[A-Za-z0-9_-]+$"))
            throw new InvalidOperationException("Google Sheets 문서 ID가 비어 있거나 잘못되었습니다.");
        return t_value;
    }

    public static async Task<SpecSheetsUploadPlan> PreviewAsync(string _spreadsheetId,
        IReadOnlyList<string> _files, string _accessToken, CancellationToken _cancellation)
    {
        string t_id = NormalizeSpreadsheetId(_spreadsheetId);
        List<SpecSheetsCsv> t_files = ReadFiles(_files);
        JObject t_remote = await ReadRemote(t_id, t_files, _accessToken, _cancellation);
        return BuildPlan(t_id, t_files, t_remote);
    }

    public static async Task<string> UploadAsync(SpecSheetsUploadPlan _plan,
        string _accessToken, CancellationToken _cancellation)
    {
        if (_plan == null || !_plan.CanUpload) throw new InvalidOperationException("업로드할 변경을 먼저 미리보기로 확인하세요.");
        List<SpecSheetsCsv> t_files = ReadFiles(_plan.Files.Select(t => t.Path).ToList());
        if (!t_files.Select(t => t.Hash).SequenceEqual(_plan.Files.Select(t => t.Hash)))
            throw new InvalidOperationException("미리보기 이후 CSV가 바뀌었습니다. 다시 미리보기를 실행하세요.");

        JObject t_remote = await ReadRemote(_plan.SpreadsheetId, t_files, _accessToken, _cancellation);
        if (Fingerprint(t_remote) != _plan.RemoteFingerprint)
            throw new InvalidOperationException("미리보기 이후 Google Sheet 내용이나 탭 구성이 바뀌었습니다. 다시 미리보기를 실행하세요.");
        _cancellation.ThrowIfCancellationRequested();
        string t_body = new JObject { ["requests"] = _plan.Requests }.ToString(Formatting.None);
        // 서버에서는 한 batchUpdate의 요청 전체가 원자적으로 적용된다. 자동 재시도하지 않는다.
        // https://developers.google.com/workspace/sheets/api/guides/batch
        try
        {
            await Send(HttpMethod.Post, ENDPOINT + _plan.SpreadsheetId + ":batchUpdate", t_body,
                _accessToken, _cancellation);
            JObject t_after = await ReadRemote(_plan.SpreadsheetId, t_files, _accessToken, _cancellation);
            SpecSheetsUploadPlan t_verified = BuildPlan(_plan.SpreadsheetId, t_files, t_after);
            if (t_verified.ChangedCellCount != 0)
                throw new InvalidOperationException("업로드 후 CSV와 다른 값이 있습니다. 다시 미리보기로 확인하세요.");
        }
        catch (Exception t_exception) when (t_exception is HttpRequestException || t_exception is TaskCanceledException)
        {
            throw new InvalidOperationException("업로드 요청 이후 통신이 중단됐습니다. 이미 반영됐을 수 있으므로 다시 미리보기로 원격 상태를 확인하세요.");
        }
        _plan.CanUpload = false;
        return $"업로드 및 원격 재조회 검증 완료: {_plan.SpreadsheetTitle}, {t_files.Count}개 표, {_plan.ChangedCellCount}개 셀.\nGoogle Sheet만 변경했습니다.";
    }

    internal static List<SpecSheetsCsv> ReadFiles(IReadOnlyList<string> _paths)
    {
        if (_paths == null || _paths.Count == 0) throw new InvalidOperationException("업로드할 CSV를 선택하세요.");
        string t_directory = Path.GetFullPath(SpecDocsCsvExporter.DocsDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var t_files = new List<SpecSheetsCsv>();
        var t_titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string t_path in _paths.OrderBy(t => t, StringComparer.Ordinal))
        {
            string t_full = Path.GetFullPath(t_path);
            if (!string.Equals(Path.GetDirectoryName(t_full), t_directory, StringComparison.OrdinalIgnoreCase) ||
                !t_full.EndsWith(SUFFIX, StringComparison.Ordinal) || !File.Exists(t_full))
                throw new InvalidOperationException("docs/SpecData의 *_sheet.csv 파일만 업로드할 수 있습니다.");
            string t_text = File.ReadAllText(t_full, Encoding.UTF8);
            string t_title = Path.GetFileName(t_full).Substring(0, Path.GetFileName(t_full).Length - SUFFIX.Length);
            if (!t_titles.Add(t_title)) throw new InvalidOperationException("같은 표가 중복 선택됐습니다.");
            t_files.Add(Parse(t_full, t_title, t_text));
        }
        return t_files;
    }

    internal static SpecSheetsCsv Parse(string _path, string _title, string _text)
    {
        if (!SpecDocsCsvExporter.TryParseCsv(_text, out List<List<string>> t_rows))
            throw new InvalidOperationException($"{_title}: CSV 따옴표 또는 행 구성이 올바르지 않습니다.");
        while (t_rows.Count > 0 && t_rows[t_rows.Count - 1].All(string.IsNullOrEmpty)) t_rows.RemoveAt(t_rows.Count - 1);
        bool t_enum = _title == "enum" && t_rows.Count > 1 &&
            t_rows[1].Any(t => t.StartsWith("value:", StringComparison.Ordinal) || t.StartsWith("flag_value:", StringComparison.Ordinal));
        int t_typeRow = t_enum || (t_rows.Count > 1 && IsTypeRow(t_rows[1])) ? 1 : 2;
        if (t_rows.Count <= t_typeRow || (!t_enum && !IsTypeRow(t_rows[t_typeRow])))
            throw new InvalidOperationException($"{_title}: 필드명·타입 헤더를 찾지 못했습니다.");
        int t_columns = t_rows[t_typeRow].Count;
        if (t_rows.Any(t => t.Count != t_columns))
            throw new InvalidOperationException($"{_title}: 행마다 열 수가 다릅니다.");
        if (t_rows.Count <= t_typeRow + 1)
            throw new InvalidOperationException($"{_title}: 데이터가 없는 CSV는 업로드하지 않습니다.");
        // ExcelUtil은 일반 표의 1행=설명, 2행=필드명, 3행=타입을 고정으로 읽는다.
        // 로컬 CSV는 설명 없는 2행 헤더도 허용하므로 업로드용 행만 보완한다.
        // 빈 첫 행은 XLSX 내보내기에서 누락될 수 있어 필드명을 설명으로 사용한다.
        // enum은 생성기 자체가 2행 헤더를 쓰므로 옮기지 않는다.
        if (!t_enum && t_typeRow == 1)
        {
            t_rows.Insert(0, new List<string>(t_rows[0]));
            t_typeRow = 2;
        }
        List<string> t_types = t_enum
            ? t_rows[1].Select(t => t.StartsWith("value:", StringComparison.Ordinal) || t.StartsWith("flag_value:", StringComparison.Ordinal) ? "long" : "string").ToList()
            : t_rows[t_typeRow];
        return new SpecSheetsCsv { Path = _path, Title = _title, Hash = Hash(_text), Rows = t_rows, TypeRow = t_typeRow, Types = t_types };
    }

    static bool IsTypeRow(List<string> _row)
        => _row.Count > 0 && _row.All(t => new[] { "int", "long", "float", "double", "bool", "string" }.Contains(t));

    static async Task<JObject> ReadRemote(string _id, List<SpecSheetsCsv> _files,
        string _token, CancellationToken _cancellation)
    {
        JObject t_metadata = await Send(HttpMethod.Get, ENDPOINT + _id +
            "?fields=properties(title),sheets(properties(sheetId,title,gridProperties(rowCount,columnCount)))", null, _token, _cancellation);
        var t_sheets = (JArray)t_metadata["sheets"] ?? new JArray();
        foreach (SpecSheetsCsv t_file in _files)
        {
            JObject t_sheet = t_sheets.OfType<JObject>().FirstOrDefault(t => (string)t["properties"]?["title"] == t_file.Title);
            if (t_sheet == null) continue;
            string t_range = "'" + t_file.Title.Replace("'", "''") + "'";
            JObject t_values = await Send(HttpMethod.Get, ENDPOINT + _id + "?ranges=" + Uri.EscapeDataString(t_range) +
                "&includeGridData=true&fields=sheets(data(startRow,startColumn,rowData(values(userEnteredValue,effectiveValue))))",
                null, _token, _cancellation);
            t_sheet["data"] = t_values["sheets"]?[0]?["data"]?.DeepClone() ?? new JArray();
        }
        return t_metadata;
    }

    internal static SpecSheetsUploadPlan BuildPlan(string _id, List<SpecSheetsCsv> _files, JObject _remote)
    {
        var t_requests = new JArray();
        var t_report = new StringBuilder();
        var t_sheets = (JArray)_remote["sheets"] ?? new JArray();
        int t_nextId = t_sheets.Count == 0 ? 1 : t_sheets.Max(t => (int)t["properties"]["sheetId"]) + 1;
        int t_changes = 0, t_conflicts = 0, t_cleared = 0, t_samples = 0;
        t_report.AppendLine("대상: " + (string)_remote["properties"]?["title"]);
        t_report.AppendLine("CSV 셀 값으로 선택한 탭을 갱신합니다. CSV 범위 밖의 기존 값은 비웁니다. 서식·메모·다른 탭은 유지합니다.");
        t_report.AppendLine("설명 행이 없는 일반 CSV는 필드명으로 설명 행을 보완합니다(설명·필드명·타입 순서).");
        foreach (SpecSheetsCsv t_file in _files)
        {
            JObject t_sheet = t_sheets.OfType<JObject>().FirstOrDefault(t => (string)t["properties"]?["title"] == t_file.Title);
            bool t_new = t_sheet == null;
            int t_sheetId = t_new ? t_nextId++ : (int)t_sheet["properties"]["sheetId"];
            JArray t_remoteRows = t_sheet?["data"]?[0]?["rowData"] as JArray ?? new JArray();
            int t_oldColumns = t_remoteRows.Count == 0 ? 0 : t_remoteRows.Max(t => (t["values"] as JArray)?.Count ?? 0);
            int t_columns = Math.Max(t_file.Rows[0].Count, t_oldColumns);
            int t_rows = Math.Max(t_file.Rows.Count, t_remoteRows.Count);
            if (t_new)
                t_requests.Add(new JObject { ["addSheet"] = new JObject { ["properties"] = new JObject {
                    ["sheetId"] = t_sheetId, ["title"] = t_file.Title,
                    ["gridProperties"] = new JObject { ["rowCount"] = Math.Max(100, t_rows), ["columnCount"] = Math.Max(26, t_columns) } } } });
            else
            {
                JObject t_grid = (JObject)t_sheet["properties"]["gridProperties"];
                if (t_rows > (int)t_grid["rowCount"] || t_columns > (int)t_grid["columnCount"])
                    t_requests.Add(new JObject { ["updateSheetProperties"] = new JObject {
                        ["properties"] = new JObject { ["sheetId"] = t_sheetId, ["gridProperties"] = new JObject {
                            ["rowCount"] = Math.Max(t_rows, (int)t_grid["rowCount"]),
                            ["columnCount"] = Math.Max(t_columns, (int)t_grid["columnCount"]) } },
                        ["fields"] = "gridProperties.rowCount,gridProperties.columnCount" } });
            }
            int t_before = t_changes;
            var t_lines = new StringBuilder();
            for (int r = 0; r < t_rows; r++)
            {
                JArray t_oldRow = r < t_remoteRows.Count ? t_remoteRows[r]["values"] as JArray : null;
                int t_start = -1;
                var t_cells = new JArray();
                for (int c = 0; c <= t_columns; c++)
                {
                    JObject t_old = c < (t_oldRow?.Count ?? 0) ? t_oldRow[c] as JObject : null;
                    JObject t_wanted = c < t_columns ? CellValue(t_file, r, c) : null;
                    bool t_formula = t_old?["userEnteredValue"]?["formulaValue"] != null;
                    JToken t_existing = t_formula ? t_old?["effectiveValue"] : t_old?["userEnteredValue"];
                    bool t_changed = c < t_columns && !Equivalent(t_existing, t_wanted);
                    if (t_changed)
                    {
                        t_changes++;
                        if (t_formula) t_conflicts++;
                        if (t_wanted == null) t_cleared++;
                        if (t_samples++ < 80)
                            t_lines.AppendLine($"  {Column(c)}{r + 1}: {Display(t_existing)} → {Display(t_wanted)}" + (t_formula ? " [수식 충돌: 업로드 차단]" : ""));
                    }
                    if (t_changed && !t_formula)
                    {
                        if (t_start < 0) t_start = c;
                        t_cells.Add(t_wanted == null ? new JObject() : new JObject { ["userEnteredValue"] = t_wanted });
                    }
                    else if (t_start >= 0)
                    {
                        t_requests.Add(new JObject { ["updateCells"] = new JObject {
                            ["range"] = new JObject { ["sheetId"] = t_sheetId, ["startRowIndex"] = r, ["endRowIndex"] = r + 1,
                                ["startColumnIndex"] = t_start, ["endColumnIndex"] = c },
                            ["rows"] = new JArray(new JObject { ["values"] = t_cells }), ["fields"] = "userEnteredValue" } });
                        t_start = -1;
                        t_cells = new JArray();
                    }
                }
            }
            t_report.AppendLine($"\n{t_file.Title}: {(t_new ? "새 탭 생성" : "기존 탭")} · {t_remoteRows.Count} → {t_file.Rows.Count}행(헤더 포함) · 변경 {t_changes - t_before}셀");
            t_report.Append(t_lines);
        }
        if (t_samples > 80) t_report.AppendLine($"\n나머지 {t_samples - 80}개 셀 변경은 생략했습니다. 표별 변경 수와 선택 CSV를 확인하세요.");
        bool t_oversized = Encoding.UTF8.GetByteCount(new JObject { ["requests"] = t_requests }.ToString(Formatting.None)) > MAX_REQUEST_BYTES;
        t_report.AppendLine($"\n합계: 변경 {t_changes}셀 · 기존 값 비우기 {t_cleared}셀 · 수식 충돌 {t_conflicts}셀");
        if (t_conflicts > 0) t_report.AppendLine("수식 결과와 CSV가 다릅니다. 해당 수식을 시트에서 검토하거나 그 CSV를 선택 해제하세요.");
        if (t_oversized) t_report.AppendLine("한 번에 보낼 변경이 너무 많습니다. CSV를 나누어 선택하세요.");
        return new SpecSheetsUploadPlan { SpreadsheetId = _id, SpreadsheetTitle = (string)_remote["properties"]?["title"] ?? _id,
            Files = _files, Requests = t_requests, RemoteFingerprint = Fingerprint(_remote),
            ChangedCellCount = t_changes, CanUpload = t_changes > 0 && t_conflicts == 0 && !t_oversized, Report = t_report.ToString() };
    }

    static JObject CellValue(SpecSheetsCsv _file, int _row, int _column)
    {
        if (_row >= _file.Rows.Count || _column >= _file.Rows[_row].Count || _file.Rows[_row][_column].Length == 0) return null;
        string t_value = _file.Rows[_row][_column];
        string t_type = _row > _file.TypeRow ? _file.Types[_column] : "string";
        if (t_type == "bool")
        {
            if (t_value == "1" || string.Equals(t_value, "true", StringComparison.OrdinalIgnoreCase)) return new JObject { ["boolValue"] = true };
            if (t_value == "0" || string.Equals(t_value, "false", StringComparison.OrdinalIgnoreCase)) return new JObject { ["boolValue"] = false };
            throw new InvalidOperationException($"{_file.Title} {Column(_column)}{_row + 1}: bool 값이 잘못됐습니다.");
        }
        if (t_type != "string")
        {
            if (!double.TryParse(t_value, NumberStyles.Float, CultureInfo.InvariantCulture, out double t_number) || double.IsNaN(t_number) || double.IsInfinity(t_number) ||
                (t_type == "int" && !int.TryParse(t_value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) ||
                (t_type == "long" && !long.TryParse(t_value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                throw new InvalidOperationException($"{_file.Title} {Column(_column)}{_row + 1}: 숫자 값이 잘못됐습니다.");
            // Sheets 숫자는 double이다. 큰 정수는 문자열로 전송해 손실을 피한다.
            if (Math.Abs(t_number) <= 9007199254740991d) return new JObject { ["numberValue"] = t_number };
        }
        // '='로 시작하는 문자열도 수식으로 실행하지 않는다.
        return new JObject { ["stringValue"] = t_value };
    }

    static bool Equivalent(JToken _left, JToken _right)
    {
        string t_left = Scalar(_left), t_right = Scalar(_right);
        if (t_left == t_right) return true;
        return _right?["numberValue"] != null && double.TryParse(t_left, NumberStyles.Float, CultureInfo.InvariantCulture, out double t_number)
            && t_number == (double)_right["numberValue"];
    }

    static string Scalar(JToken _value)
    {
        if (_value == null || !_value.HasValues) return string.Empty;
        JToken t_scalar = _value["stringValue"] ?? _value["numberValue"] ?? _value["boolValue"];
        return t_scalar is JValue t_value ? Convert.ToString(t_value.Value, CultureInfo.InvariantCulture) : _value.ToString(Formatting.None);
    }

    static string Display(JToken _value)
    {
        string t_value = Scalar(_value).Replace("\r", "\\r").Replace("\n", "\\n");
        return t_value.Length == 0 ? "(빈칸)" : t_value.Length > 70 ? t_value.Substring(0, 70) + "…" : t_value;
    }

    static string Column(int _column)
    {
        string t_text = string.Empty;
        for (int n = _column + 1; n > 0; n = (n - 1) / 26) t_text = (char)('A' + (n - 1) % 26) + t_text;
        return t_text;
    }

    static string Fingerprint(JObject _remote) => Hash(_remote.ToString(Formatting.None));
    static string Hash(string _text)
    {
        using var t_hash = SHA256.Create();
        return Convert.ToBase64String(t_hash.ComputeHash(Encoding.UTF8.GetBytes(_text)));
    }

    static async Task<JObject> Send(HttpMethod _method, string _url, string _body, string _token, CancellationToken _cancellation)
    {
        if (string.IsNullOrWhiteSpace(_token)) throw new InvalidOperationException("Google Sheet 연결이 필요합니다.");
        using var t_request = new HttpRequestMessage(_method, _url);
        t_request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (_body != null) t_request.Content = new StringContent(_body, Encoding.UTF8, "application/json");
        using HttpResponseMessage t_response = await s_client.SendAsync(t_request, _cancellation);
        if (!t_response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Google Sheets 요청 실패 (HTTP {(int)t_response.StatusCode}). 문서 ID·편집 권한·Sheets API 사용 설정을 확인하고 필요하면 다시 연결하세요.");
        string t_text = await t_response.Content.ReadAsStringAsync();
        try { return JObject.Parse(t_text); }
        catch (JsonException) { throw new InvalidOperationException("Google Sheets 응답을 읽지 못했습니다. 다시 미리보기로 확인하세요."); }
    }
}
