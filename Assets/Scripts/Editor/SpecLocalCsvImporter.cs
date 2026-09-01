using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <c>docs/SpecData/{표}_sheet.csv</c> 를 근거로 <c>Assets/Resources/SpecData.bytes</c> 를 다시 쓴다.
/// <see cref="SpecDocsCsvExporter"/>(bytes → CSV)의 **역방향**이다.
///
/// 언제 쓰나: 스프레드시트를 거치지 않고 로컬에서 표를 고쳐 바로 돌려 보고 싶을 때.
/// 진실원은 여전히 스프레드시트다 — 여기서 만든 bytes 는 **로컬 실험본**이고, 시트에 반영하지 않으면
/// 다음 '시트 적용 & CS 생성'에서 덮여 사라진다. 그래서 갱신할 때마다 그 경고를 로그로 남긴다.
///
/// CSV 를 갖지 않은 표(enum·수기 시트 등)는 기존 JSON 값을 그대로 둔다.
/// 폐기된 표는 <see cref="REMOVED_TABLES"/>에 명시된 것만 제거한다.
/// </summary>
public static class SpecLocalCsvImporter
{
    const string CSV_SUFFIX = "_sheet.csv";
    const string BYTES_PATH = "Assets/Resources/SpecData.bytes";
    static readonly HashSet<string> REMOVED_TABLES = new(StringComparer.Ordinal)
    {
        "SynergyEffectParamDef",
    };

    /// <summary>SpecDataAsset.EncryptKey. 로더의 _key 는 생성기가 난독화한 값이라 여기 쓸 수 없다.</summary>
    const string ENCRYPT_KEY = "cRM1fuNZDwvqnjzY";

    /// <summary>저장소 루트의 docs/SpecData. Assets의 형제 디렉터리다.</summary>
    static string DocsDir => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "docs", "SpecData");

    [MenuItem("CookApps/로컬 CSV로 SpecData 갱신 (실험본)")]
    static void MenuImport()
    {
        if (!Import(out string t_summary, out string t_error)) { Debug.LogError("[SpecLocalCsv] " + t_error); return; }
        Debug.LogWarning("[SpecLocalCsv] " + t_summary + "\n※ 이건 로컬 실험본이다. 시트에 반영하지 않으면 다음 '시트 적용 & CS 생성'에서 사라진다.");
    }

    /// <summary>지금 bytes 와 docs CSV 를 비교만 한다(쓰지 않는다).</summary>
    public static bool Inspect(out string _report, out string _error)
    {
        _report = null;
        if (!TryReadJson(out string t_json, out _error)) return false;

        List<Span> t_tables = TopLevelTables(t_json);
        var t_lines = new List<string>();
        foreach (Span t_table in t_tables)
        {
            string t_csv = Path.Combine(DocsDir, t_table.Name + CSV_SUFFIX);
            t_lines.Add($"{t_table.Name}: json행 {CountRows(t_json, t_table)} · CSV {(File.Exists(t_csv) ? "있음" : "없음")}");
        }

        foreach (string t_file in Directory.Exists(DocsDir) ? Directory.GetFiles(DocsDir, "*" + CSV_SUFFIX) : Array.Empty<string>())
        {
            string t_name = Path.GetFileName(t_file);
            t_name = t_name.Substring(0, t_name.Length - CSV_SUFFIX.Length);
            if (!t_tables.Any(t => t.Name == t_name)) t_lines.Add($"{t_name}: json에 없음 · CSV만 있음(새 표는 시트에서 만들어야 한다)");
        }

        _report = string.Join("\n", t_lines);
        return true;
    }

    /// <summary>CSV 가 있는 표를 전부 CSV 내용으로 갈아끼우고 bytes 를 다시 쓴다.</summary>
    public static bool Import(out string _summary, out string _error)
    {
        _summary = null;
        if (!TryReadJson(out string t_json, out _error)) return false;
        if (!Directory.Exists(DocsDir)) { _error = "docs/SpecData 디렉터리가 없다: " + DocsDir; return false; }

        List<Span> t_tables = TopLevelTables(t_json);
        if (t_tables.Count == 0) { _error = "SpecData JSON에서 표를 하나도 못 찾았다(구조가 바뀌었을 수 있다)."; return false; }

        var t_builder = new StringBuilder("{");
        var t_replaced = new List<string>();
        var t_kept = new List<string>();
        var t_dropped = new List<string>();

        foreach (Span t_table in t_tables)
        {
            if (REMOVED_TABLES.Contains(t_table.Name))
            {
                t_dropped.Add(t_table.Name);
                continue;
            }

            string t_csvPath = Path.Combine(DocsDir, t_table.Name + CSV_SUFFIX);
            string t_value;

            if (File.Exists(t_csvPath))
            {
                if (!TryBuildArray(t_csvPath, out t_value, out int t_rows, out string t_rowError))
                { _error = $"'{t_table.Name}' CSV 변환 실패 — {t_rowError}"; return false; }
                t_replaced.Add($"{t_table.Name}({t_rows})");
            }
            else
            {
                t_value = t_json.Substring(t_table.ValueStart, t_table.ValueEnd - t_table.ValueStart);
                t_kept.Add(t_table.Name);
            }

            if (t_builder.Length > 1) t_builder.Append(',');
            t_builder.Append('"').Append(t_table.Name).Append("\":").Append(t_value);
        }
        t_builder.Append('}');

        byte[] t_bytes = EncryptAes128(t_builder.ToString(), Encoding.UTF8.GetBytes(ENCRYPT_KEY));
        if (t_bytes == null) { _error = "암호화 실패."; return false; }

        File.WriteAllBytes(BYTES_PATH, t_bytes);
        AssetDatabase.ImportAsset(BYTES_PATH, ImportAssetOptions.ForceUpdate);

        _summary = $"SpecData.bytes 갱신 — 교체 {t_replaced.Count}개 [{string.Join(", ", t_replaced)}]"
                 + (t_kept.Count > 0 ? $" · 유지 {t_kept.Count}개 [{string.Join(", ", t_kept)}]" : "")
                 + (t_dropped.Count > 0 ? $" · 삭제 {t_dropped.Count}개 [{string.Join(", ", t_dropped)}]" : "");
        return true;
    }

    // ---------- CSV → JSON 배열 ----------

    /// <summary>CSV 3줄 머리(설명 · 필드명 · 타입) 뒤의 행을 JSON 배열로 만든다.</summary>
    static bool TryBuildArray(string _path, out string _json, out int _rowCount, out string _error)
    {
        _json = null;
        _rowCount = 0;
        _error = null;

        List<List<string>> t_rows = ReadCsv(File.ReadAllText(_path));
        if (t_rows.Count < 3) { _error = "머리 3줄(설명·필드명·타입)이 없다."; return false; }

        List<string> t_fields = t_rows[1];
        List<string> t_types  = t_rows[2];
        if (t_fields.Count != t_types.Count) { _error = "필드명 줄과 타입 줄의 칸 수가 다르다."; return false; }

        var t_builder = new StringBuilder("[");
        for (int t_r = 3; t_r < t_rows.Count; t_r++)
        {
            List<string> t_row = t_rows[t_r];
            if (t_row.Count == 0 || t_row.All(string.IsNullOrWhiteSpace)) continue;

            if (t_builder.Length > 1) t_builder.Append(',');
            t_builder.Append('{');
            for (int t_c = 0; t_c < t_fields.Count; t_c++)
            {
                if (string.IsNullOrWhiteSpace(t_fields[t_c])) continue;
                if (t_c > 0) t_builder.Append(',');

                string t_field = t_fields[t_c].Trim();
                string t_cell = t_c < t_row.Count ? t_row[t_c] : string.Empty;
                try
                {
                    t_builder.Append('"').Append(t_field).Append("\":")
                             .Append(Value(t_types[t_c].Trim(), t_cell));
                }
                catch (FormatException t_exception)
                {
                    _error = $"{Path.GetFileName(_path)} {t_r + 1}행 '{t_field}': {t_exception.Message}";
                    return false;
                }
            }
            t_builder.Append('}');
            _rowCount++;
        }
        t_builder.Append(']');
        _json = t_builder.ToString();
        return true;
    }

    /// <summary>타입 줄이 수치면 raw, 아니면 문자열. 잘못된 수치는 조용히 0으로 바꾸지 않는다.</summary>
    static string Value(string _type, string _cell)
    {
        switch (_type)
        {
            case "int":
                if (int.TryParse(_cell.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int t_i))
                    return t_i.ToString(CultureInfo.InvariantCulture);
                throw new FormatException($"정수가 아닌 값 '{_cell}'");
            case "long":
                if (long.TryParse(_cell.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long t_l))
                    return t_l.ToString(CultureInfo.InvariantCulture);
                throw new FormatException($"long 범위의 정수가 아닌 값 '{_cell}'");
            case "float":
                if (float.TryParse(_cell.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float t_f) &&
                    !float.IsNaN(t_f) && !float.IsInfinity(t_f))
                    return t_f.ToString("R", CultureInfo.InvariantCulture);
                throw new FormatException($"유한한 실수가 아닌 값 '{_cell}'");
            case "double":
                if (double.TryParse(_cell.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double t_d) &&
                    !double.IsNaN(t_d) && !double.IsInfinity(t_d))
                    return t_d.ToString("R", CultureInfo.InvariantCulture);
                throw new FormatException($"유한한 실수가 아닌 값 '{_cell}'");
            case "bool":
                string t_bool = _cell.Trim();
                if (t_bool == "1" || string.Equals(t_bool, "true", StringComparison.OrdinalIgnoreCase)) return "true";
                if (t_bool == "0" || string.Equals(t_bool, "false", StringComparison.OrdinalIgnoreCase)) return "false";
                throw new FormatException($"bool이 아닌 값 '{_cell}' (0/1/true/false만 허용)");
            default:
                return Quote(_cell);
        }
    }

    static string Quote(string _value)
    {
        var t_builder = new StringBuilder("\"");
        foreach (char t_c in _value ?? string.Empty)
        {
            switch (t_c)
            {
                case '"':  t_builder.Append("\\\""); break;
                case '\\': t_builder.Append("\\\\"); break;
                case '\n': t_builder.Append("\\n");  break;
                case '\r': t_builder.Append("\\r");  break;
                case '\t': t_builder.Append("\\t");  break;
                default:
                    if (t_c < 0x20) t_builder.Append("\\u").Append(((int)t_c).ToString("x4"));
                    else t_builder.Append(t_c);
                    break;
            }
        }
        return t_builder.Append('"').ToString();
    }

    /// <summary>따옴표 안의 콤마·개행·이중따옴표를 지키는 최소 CSV 리더.</summary>
    static List<List<string>> ReadCsv(string _text)
    {
        if (_text.Length > 0 && _text[0] == '﻿') _text = _text.Substring(1);   // BOM

        var t_rows = new List<List<string>>();
        var t_row = new List<string>();
        var t_cell = new StringBuilder();
        bool t_quoted = false;

        for (int t_i = 0; t_i < _text.Length; t_i++)
        {
            char t_c = _text[t_i];
            if (t_quoted)
            {
                if (t_c != '"') { t_cell.Append(t_c); continue; }
                if (t_i + 1 < _text.Length && _text[t_i + 1] == '"') { t_cell.Append('"'); t_i++; continue; }
                t_quoted = false;
                continue;
            }

            switch (t_c)
            {
                case '"':  t_quoted = true; break;
                case ',':  t_row.Add(t_cell.ToString()); t_cell.Clear(); break;
                case '\r': break;
                case '\n':
                    t_row.Add(t_cell.ToString()); t_cell.Clear();
                    t_rows.Add(t_row); t_row = new List<string>();
                    break;
                default:   t_cell.Append(t_c); break;
            }
        }
        if (t_cell.Length > 0 || t_row.Count > 0) { t_row.Add(t_cell.ToString()); t_rows.Add(t_row); }
        return t_rows;
    }

    // ---------- JSON 최상위 표 훑기 ----------

    readonly struct Span
    {
        public readonly string Name;
        public readonly int ValueStart;
        public readonly int ValueEnd;
        public Span(string _name, int _start, int _end) { Name = _name; ValueStart = _start; ValueEnd = _end; }
    }

    /// <summary>최상위 객체의 "이름": 값 쌍을 순서대로 훑는다. 값은 중첩 깊이를 세어 통째로 잘라 둔다
    /// (CSV 가 없는 표는 손대지 않고 원문 그대로 다시 쓰기 위해서다).</summary>
    static List<Span> TopLevelTables(string _json)
    {
        var t_result = new List<Span>();
        int t_i = _json.IndexOf('{');
        if (t_i < 0) return t_result;
        t_i++;

        while (t_i < _json.Length)
        {
            while (t_i < _json.Length && (_json[t_i] == ',' || char.IsWhiteSpace(_json[t_i]))) t_i++;
            if (t_i >= _json.Length || _json[t_i] == '}') break;
            if (_json[t_i] != '"') break;

            int t_nameEnd = SkipString(_json, t_i);
            string t_name = _json.Substring(t_i + 1, t_nameEnd - t_i - 2);
            t_i = t_nameEnd;

            while (t_i < _json.Length && char.IsWhiteSpace(_json[t_i])) t_i++;
            if (t_i >= _json.Length || _json[t_i] != ':') break;
            t_i++;
            while (t_i < _json.Length && char.IsWhiteSpace(_json[t_i])) t_i++;

            int t_valueEnd = SkipValue(_json, t_i);
            t_result.Add(new Span(t_name, t_i, t_valueEnd));
            t_i = t_valueEnd;
        }
        return t_result;
    }

    /// <summary>여는 따옴표에서 시작해 닫는 따옴표 **다음** 인덱스를 준다.</summary>
    static int SkipString(string _json, int _start)
    {
        int t_i = _start + 1;
        while (t_i < _json.Length)
        {
            if (_json[t_i] == '\\') { t_i += 2; continue; }
            if (_json[t_i] == '"') return t_i + 1;
            t_i++;
        }
        return t_i;
    }

    /// <summary>값 하나를 건너뛴 뒤 인덱스. 문자열·배열·객체·원시값 전부.</summary>
    static int SkipValue(string _json, int _start)
    {
        if (_start >= _json.Length) return _start;
        char t_c = _json[_start];
        if (t_c == '"') return SkipString(_json, _start);

        if (t_c == '[' || t_c == '{')
        {
            int t_depth = 0;
            int t_i = _start;
            while (t_i < _json.Length)
            {
                char t_x = _json[t_i];
                if (t_x == '"') { t_i = SkipString(_json, t_i); continue; }
                if (t_x == '[' || t_x == '{') t_depth++;
                else if (t_x == ']' || t_x == '}') { t_depth--; if (t_depth == 0) return t_i + 1; }
                t_i++;
            }
            return t_i;
        }

        int t_end = _start;
        while (t_end < _json.Length && _json[t_end] != ',' && _json[t_end] != '}' && _json[t_end] != ']') t_end++;
        return t_end;
    }

    static int CountRows(string _json, Span _table)
    {
        int t_count = 0;
        int t_i = _table.ValueStart;
        if (t_i >= _json.Length || _json[t_i] != '[') return -1;

        t_i++;
        while (t_i < _table.ValueEnd)
        {
            while (t_i < _table.ValueEnd && (_json[t_i] == ',' || char.IsWhiteSpace(_json[t_i]))) t_i++;
            if (t_i >= _table.ValueEnd || _json[t_i] == ']') break;
            t_i = SkipValue(_json, t_i);
            t_count++;
        }
        return t_count;
    }

    // ---------- 암복호 (패키지 CryptoUtil이 internal이라 같은 규약을 여기서 다시 쓴다) ----------

    static bool TryReadJson(out string _json, out string _error)
    {
        _json = null;
        _error = null;
        if (!File.Exists(BYTES_PATH)) { _error = "SpecData.bytes가 없다: " + BYTES_PATH; return false; }

        byte[] t_plain = DecryptAes128(File.ReadAllBytes(BYTES_PATH), Encoding.UTF8.GetBytes(ENCRYPT_KEY));
        if (t_plain == null) { _error = "복호화 실패 — EncryptKey가 바뀌었을 수 있다(SpecDataAsset 확인)."; return false; }

        _json = Encoding.UTF8.GetString(t_plain);
        return true;
    }

    static byte[] EncryptAes128(string _text, byte[] _key)
    {
        try
        {
            using var t_aes = Aes.Create();
            t_aes.Mode = CipherMode.CBC;
            t_aes.Padding = PaddingMode.PKCS7;
            t_aes.Key = _key;
            t_aes.IV = _key.Reverse().ToArray();
            byte[] t_body = Encoding.UTF8.GetBytes(_text);
            return t_aes.CreateEncryptor().TransformFinalBlock(t_body, 0, t_body.Length);
        }
        catch (Exception t_exception) { Debug.LogError("[SpecLocalCsv] 암호화 실패: " + t_exception.Message); return null; }
    }

    static byte[] DecryptAes128(byte[] _data, byte[] _key)
    {
        try
        {
            using var t_aes = Aes.Create();
            t_aes.Mode = CipherMode.CBC;
            t_aes.Padding = PaddingMode.PKCS7;
            t_aes.Key = _key;
            t_aes.IV = _key.Reverse().ToArray();
            return t_aes.CreateDecryptor().TransformFinalBlock(_data, 0, _data.Length);
        }
        catch { return null; }
    }
}
