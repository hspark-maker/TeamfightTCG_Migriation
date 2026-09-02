using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

/// <summary>
/// 생성된 SpecData를 저장소의 <c>docs/SpecData/{표}_sheet.csv</c>로 다시 떨군다.
/// 시트를 새로 받으면(Assets/Resources/SpecData.bytes 갱신) 자동으로 뒤따라 실행돼
/// 문서용 CSV가 스펙시트와 어긋나지 않게 한다.
/// 원본 시트에만 있는 설명 행(1행)과 <c>#</c> 접두 컬럼은 기존 파일에서 그대로 물려받는다.
/// </summary>
public static class SpecDocsCsvExporter
{
    const string AUTO_PREF = "SpecDocsCsvExporter.Auto";
    const string PENDING_KEY = "SpecDocsCsvExporter.Pending";
    // 진입점은 릴리즈 관리 창(데이터 탭)의 'SpecData ↔ docs CSV' 하나다.
    // 예전엔 CookApps 메뉴에 이름만 던져 놨는데, 무엇을 어느 방향으로 덮는지가 이름에 안 드러나
    // 아무도 못 쓰는 메뉴가 됐다. 설명을 붙일 수 있는 창으로 옮겼다.
    const string FORCE_LABEL = "행 삭제 허용";
    public const string SPEC_BYTES_PATH = "Assets/Resources/SpecData.bytes";
    const string FILE_SUFFIX = "_sheet.csv";
    const int HEADER_SEARCH_LIMIT = 8;

    public static bool AutoExport
    {
        get => EditorPrefs.GetBool(AUTO_PREF, true);
        set => EditorPrefs.SetBool(AUTO_PREF, value);
    }

    /// <summary>저장소 루트의 docs/SpecData. Assets의 형제 디렉터리다.</summary>
    public static string DocsDirectory
    {
        get
        {
            string t_root = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            return Path.Combine(t_root, "docs", "SpecData").Replace('\\', '/');
        }
    }

    /// <summary>사람이 눌러서 도는 내보내기(확인 대화 포함). 릴리즈 관리 창이 유일한 호출부다.</summary>
    public static void RunExportInteractive(bool _allowRowDeletion)
    {
        // 손으로 부를 때는 로컬 SpecData가 시트보다 낡았을 수 있다. 그대로 쓰면 문서가 과거로 돌아간다.
        bool t_confirmed = EditorUtility.DisplayDialog(
            "SpecData docs CSV",
            "로컬에 받아둔 SpecData 기준으로 docs/SpecData CSV를 덮어쓴다.\n" +
            "시트를 받은 지 오래됐다면 문서가 과거 값으로 되돌아갈 수 있다.\n\n계속할까?",
            _allowRowDeletion ? "덮어쓰기(행 삭제 허용)" : "덮어쓰기", "취소");
        if (!t_confirmed) return;

        if (!Export(_allowRowDeletion, out string t_summary, out string t_error))
        {
            EditorUtility.DisplayDialog("SpecData docs CSV", t_error, "확인");
            return;
        }

        Debug.Log($"[SpecDocsCsv] {t_summary}");
    }

    /// <summary>시트 적용 직후에는 코드 생성·컴파일이 걸려 있어 바로 못 읽는다. 한 박자 뒤로 미룬다.</summary>
    public static void RequestExport()
    {
        SessionState.SetBool(PENDING_KEY, true);
        EditorApplication.delayCall += RunPending;
    }

    [DidReloadScripts]
    static void OnScriptsReloaded() => EditorApplication.delayCall += RunPending;

    static void RunPending()
    {
        if (!SessionState.GetBool(PENDING_KEY, false)) return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += RunPending;
            return;
        }

        SessionState.SetBool(PENDING_KEY, false);

        // 방금 시트를 받은 직후다 — 로컬이 최신이므로 시트에서 지워진 행은 문서에서도 지운다.
        if (!Export(true, out string t_summary, out string t_error))
        {
            Debug.LogWarning($"[SpecDocsCsv] docs CSV 갱신 실패: {t_error}");
            return;
        }

        Debug.Log($"[SpecDocsCsv] {t_summary}");
    }

    /// <summary>
    /// 생성된 표 전부를 docs CSV로 쓴다. 대응하는 표가 없는 CSV(enum·수기 시트)는 건드리지 않는다.
    /// <paramref name="_allowRowDeletion"/>이 false면 기존 CSV에만 있던 행이 있는 표는 쓰지 않는다 —
    /// 로컬 SpecData가 시트보다 낡았을 때 문서를 되돌려 버리는 사고를 막는다.
    /// 시트를 막 받은 직후(자동 경로)에는 로컬이 최신이므로 삭제를 허용한다.
    /// </summary>
    public static bool Export(bool _allowRowDeletion, out string _summary, out string _error)
    {
        _summary = null;
        if (!SpecLocalTables.TryLoadManager(out object t_manager, out _error)) return false;

        string t_directory = DocsDirectory;
        try { Directory.CreateDirectory(t_directory); }
        catch (Exception t_exception) { _error = $"docs 디렉터리를 만들지 못했다: {t_exception.Message}"; return false; }

        var t_written = new List<string>();
        int t_same = 0;
        int t_skipped = 0;
        int t_created = 0;

        foreach (SpecLocalTables.SpecTable t_table in SpecLocalTables.EnumerateTables(t_manager))
        {
            string t_path = Path.Combine(t_directory, t_table.Name + FILE_SUFFIX).Replace('\\', '/');
            bool t_isNew = !File.Exists(t_path);
            string t_existing = t_isNew ? null : File.ReadAllText(t_path);

            if (!TryBuildCsv(t_table, t_existing, out string t_csv, out List<string> t_droppedIds, out string t_tableError))
            {
                t_skipped++;
                Debug.LogWarning($"[SpecDocsCsv] '{t_table.Name}' 건너뜀 — {t_tableError}");
                continue;
            }

            if (t_droppedIds.Count > 0 && !_allowRowDeletion)
            {
                t_skipped++;
                Debug.LogWarning(
                    $"[SpecDocsCsv] '{t_table.Name}' 건너뜀 — 기존 CSV에만 있는 id {t_droppedIds.Count}개가 지워진다" +
                    $"({string.Join(", ", t_droppedIds)}). 시트를 다시 받거나 '{FORCE_LABEL}'로 강행할 것.");
                continue;
            }

            // 줄바꿈만 다른 파일을 다시 쓰면 git 잡음만 는다.
            if (string.Equals(t_existing?.Replace("\r\n", "\n"), t_csv, StringComparison.Ordinal)) { t_same++; continue; }

            File.WriteAllText(t_path, t_csv, new UTF8Encoding(true));
            t_written.Add(t_table.Name);
            if (t_isNew) t_created++;
        }

        _summary = $"docs/SpecData 갱신 {t_written.Count}개" +
                   (t_written.Count > 0 ? $" ({string.Join(", ", t_written)})" : string.Empty) +
                   (t_created > 0 ? $", 그중 새로 만듦 {t_created}개" : string.Empty) +
                   $", 변화 없음 {t_same}개" + (t_skipped > 0 ? $", 건너뜀 {t_skipped}개" : string.Empty);
        return true;
    }

    /// <summary>
    /// 표 하나를 CSV 본문으로 만든다. 행이 0개여도 표 타입에서 컬럼을 꺼내 머리글만 있는 골격을 만든다 —
    /// 코드에만 있고 시트에 아직 안 올라온 표도 docs에 자리를 잡게 한다.
    /// 다만 파일이 이미 있는데 행이 0개면 손저작 행을 날리게 되므로 쓰지 않는다.
    /// </summary>
    static bool TryBuildCsv(
        SpecLocalTables.SpecTable _table, string _existing,
        out string _csv, out List<string> _droppedIds, out string _error)
    {
        _csv = null;
        _droppedIds = new List<string>();
        _error = null;

        FieldInfo[] t_fields = _table.RowType?.GetFields(BindingFlags.Public | BindingFlags.Instance);
        var t_values = new List<string[]>();

        foreach (object t_row in _table.Rows)
        {
            if (t_row == null) { _error = "null 행이 있다."; return false; }
            t_fields ??= t_row.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);

            var t_cells = new string[t_fields.Length];
            for (int i = 0; i < t_fields.Length; i++) t_cells[i] = Text(t_fields[i].GetValue(t_row));
            t_values.Add(t_cells);
        }

        if (t_fields == null) { _error = "행이 없고 표 타입도 알 수 없어 컬럼을 못 만든다."; return false; }
        if (t_fields.Length == 0) { _error = "공개 필드가 없다."; return false; }

        // 행이 없는 표를 기존 파일 위에 쓰면 손으로 채워둔 행이 통째로 날아간다. 새 파일일 때만 골격을 만든다.
        if (t_values.Count == 0 && !string.IsNullOrEmpty(_existing))
        {
            _error = "행이 0개다 — 기존 CSV를 비우지 않으려고 그대로 둔다.";
            return false;
        }

        t_values.Sort(CompareByFirstCell);
        _csv = Compose(_table.Name, t_fields, t_values, _existing, _droppedIds);
        return true;
    }

    /// <summary>
    /// 시트 원본 형식(설명 행 · 헤더 · 타입 행 · 데이터)을 유지한 CSV 본문을 만든다.
    /// 설명 행과 생성 코드에 없는 컬럼(<c>#</c> 접두 메모 등)은 기존 파일에서 컬럼 이름·id 기준으로 물려받는다.
    /// </summary>
    static string Compose(
        string _table, FieldInfo[] _fields, List<string[]> _rows, string _existing, List<string> _droppedIds)
    {
        var t_columns = new List<string>(_fields.Length);
        foreach (FieldInfo t_field in _fields) t_columns.Add(t_field.Name);

        List<string> t_description = null;
        var t_types = new List<string>(_fields.Length);
        var t_extraColumns = new List<string>();
        var t_extraByFirstCell = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var t_extraDescription = new Dictionary<string, string>(StringComparer.Ordinal);
        var t_extraTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        var t_typeByColumn = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrEmpty(_existing) && TryParseCsv(_existing, out List<List<string>> t_matrix))
        {
            int t_headerIndex = FindHeaderIndex(t_matrix, t_columns.Count > 0 ? t_columns[0] : "id");
            if (t_headerIndex >= 0)
            {
                List<string> t_oldColumns = t_matrix[t_headerIndex];
                List<string> t_oldDescription =
                    t_headerIndex > 0 && t_matrix[t_headerIndex - 1].Count == t_oldColumns.Count
                        ? t_matrix[t_headerIndex - 1]
                        : null;

                // 헤더 바로 아래 타입 행은 데이터가 아니다. 데이터 행 스캔은 그 다음부터 시작한다.
                int t_dataStart = t_headerIndex + 1;
                List<string> t_oldTypes = null;
                if (t_dataStart < t_matrix.Count && t_matrix[t_dataStart].Count == t_oldColumns.Count &&
                    t_matrix[t_dataStart].Count > 0 && IsTypeToken(t_matrix[t_dataStart][0]))
                {
                    t_oldTypes = t_matrix[t_dataStart];
                    t_dataStart++;
                }

                // 생성 코드에 없는 컬럼(#메모, 아직 코드에 안 붙은 신규 컬럼)은 지우지 않고 값째로 물려받는다.
                var t_generated = new HashSet<string>(t_columns, StringComparer.Ordinal);
                var t_descriptionByColumn = new Dictionary<string, string>(StringComparer.Ordinal);
                var t_extraIndexes = new List<int>();
                for (int c = 0; c < t_oldColumns.Count; c++)
                {
                    string t_name = t_oldColumns[c];
                    if (string.IsNullOrEmpty(t_name)) continue;
                    if (t_oldDescription != null && !t_descriptionByColumn.ContainsKey(t_name))
                        t_descriptionByColumn[t_name] = t_oldDescription[c];
                    if (t_oldTypes != null && !t_typeByColumn.ContainsKey(t_name))
                        t_typeByColumn[t_name] = t_oldTypes[c];
                    if (!t_generated.Contains(t_name)) t_extraIndexes.Add(c);
                }

                foreach (int t_index in t_extraIndexes)
                {
                    string t_name = t_oldColumns[t_index];
                    t_extraColumns.Add(t_name);
                    if (t_descriptionByColumn.TryGetValue(t_name, out string t_extraDesc)) t_extraDescription[t_name] = t_extraDesc;
                    if (t_typeByColumn.TryGetValue(t_name, out string t_extraType)) t_extraTypes[t_name] = t_extraType;
                }

                if (t_oldDescription != null)
                {
                    t_description = new List<string>(t_columns.Count);
                    foreach (string t_column in t_columns)
                        t_description.Add(t_descriptionByColumn.TryGetValue(t_column, out string t_text) ? t_text : string.Empty);
                }

                if (t_extraIndexes.Count > 0)
                {
                    for (int r = t_dataStart; r < t_matrix.Count; r++)
                    {
                        List<string> t_oldRow = t_matrix[r];
                        if (t_oldRow.Count != t_oldColumns.Count || t_oldRow.Count == 0) continue;

                        var t_carried = new string[t_extraIndexes.Count];
                        for (int i = 0; i < t_extraIndexes.Count; i++) t_carried[i] = t_oldRow[t_extraIndexes[i]];
                        t_extraByFirstCell[t_oldRow[0]] = t_carried;
                    }
                }

                if (t_extraColumns.Count > 0)
                    Debug.Log($"[SpecDocsCsv] '{_table}' 생성 코드에 없는 컬럼을 기존 값 그대로 유지한다: {string.Join(", ", t_extraColumns)}");

                CollectDroppedRows(t_matrix, t_dataStart, t_oldColumns.Count, _rows, _droppedIds);
            }
        }

        for (int i = 0; i < _fields.Length; i++)
            t_types.Add(t_typeByColumn.TryGetValue(t_columns[i], out string t_token) ? t_token : TypeToken(_fields[i]));

        var t_builder = new StringBuilder(8192);
        if (t_description != null)
        {
            var t_line = new List<string>(t_description);
            foreach (string t_extra in t_extraColumns)
                t_line.Add(t_extraDescription.TryGetValue(t_extra, out string t_text) ? t_text : string.Empty);
            AppendLine(t_builder, t_line);
        }

        var t_header = new List<string>(t_columns);
        t_header.AddRange(t_extraColumns);
        AppendLine(t_builder, t_header);

        var t_typeLine = new List<string>(t_types);
        foreach (string t_extra in t_extraColumns)
            t_typeLine.Add(t_extraTypes.TryGetValue(t_extra, out string t_token) ? t_token : "string");
        AppendLine(t_builder, t_typeLine);

        foreach (string[] t_row in _rows)
        {
            var t_line = new List<string>(t_row);
            if (t_extraColumns.Count > 0)
            {
                t_extraByFirstCell.TryGetValue(t_row.Length > 0 ? t_row[0] : string.Empty, out string[] t_carried);
                for (int i = 0; i < t_extraColumns.Count; i++)
                    t_line.Add(t_carried != null && i < t_carried.Length ? t_carried[i] : string.Empty);
            }

            AppendLine(t_builder, t_line);
        }

        return t_builder.ToString();
    }

    /// <summary>기존 CSV에만 있고 생성된 표에는 없는 행 id를 모은다 — 문서를 되돌릴 위험의 신호다.</summary>
    static void CollectDroppedRows(
        List<List<string>> _matrix, int _dataStart, int _columnCount, List<string[]> _rows, List<string> _droppedIds)
    {
        var t_newIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string[] t_row in _rows) if (t_row.Length > 0) t_newIds.Add(t_row[0]);

        for (int r = _dataStart; r < _matrix.Count; r++)
        {
            List<string> t_row = _matrix[r];
            if (t_row.Count != _columnCount || t_row.Count == 0) continue;
            if (string.IsNullOrEmpty(t_row[0])) continue;
            if (!t_newIds.Contains(t_row[0])) _droppedIds.Add(t_row[0]);
        }
    }

    static readonly string[] TYPE_TOKENS = { "int", "long", "float", "double", "bool", "string" };

    static bool IsTypeToken(string _value) => Array.IndexOf(TYPE_TOKENS, _value) >= 0;

    /// <summary>새 컬럼의 타입 행 값. 기존 파일에 있던 컬럼은 저작값을 우선한다.</summary>
    static string TypeToken(FieldInfo _field)
    {
        Type t_type = _field.FieldType;
        if (t_type == typeof(int)) return "int";
        if (t_type == typeof(long)) return "long";
        if (t_type == typeof(float)) return "float";
        if (t_type == typeof(double)) return "double";
        if (t_type == typeof(bool)) return "bool";
        return "string";
    }

    static int FindHeaderIndex(List<List<string>> _matrix, string _firstColumn)
    {
        int t_limit = Math.Min(_matrix.Count, HEADER_SEARCH_LIMIT);
        for (int i = 0; i < t_limit; i++)
            if (_matrix[i].Count > 0 && string.Equals(_matrix[i][0], _firstColumn, StringComparison.Ordinal)) return i;
        return -1;
    }

    static int CompareByFirstCell(string[] _left, string[] _right)
    {
        string t_left = _left.Length > 0 ? _left[0] : string.Empty;
        string t_right = _right.Length > 0 ? _right[0] : string.Empty;
        if (long.TryParse(t_left, NumberStyles.Integer, CultureInfo.InvariantCulture, out long t_leftId) &&
            long.TryParse(t_right, NumberStyles.Integer, CultureInfo.InvariantCulture, out long t_rightId))
            return t_leftId.CompareTo(t_rightId);
        return string.CompareOrdinal(t_left, t_right);
    }

    static string Text(object _value) => _value switch
    {
        null => string.Empty,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => _value.ToString(),
    };

    static void AppendLine(StringBuilder _builder, List<string> _cells)
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            if (i > 0) _builder.Append(',');
            _builder.Append(Escape(_cells[i]));
        }

        _builder.Append('\n');
    }

    static readonly char[] QUOTE_TRIGGERS = { ',', '"', '\n', '\r' };

    static string Escape(string _value)
    {
        string t_value = _value ?? string.Empty;
        bool t_needsQuote = t_value.IndexOfAny(QUOTE_TRIGGERS) >= 0 ||
                            t_value.StartsWith(" ", StringComparison.Ordinal) ||
                            t_value.EndsWith(" ", StringComparison.Ordinal);
        return t_needsQuote ? '"' + t_value.Replace("\"", "\"\"") + '"' : t_value;
    }

    /// <summary>RFC4180 형태의 CSV를 셀 행렬로 읽는다(따옴표 안의 쉼표·줄바꿈 포함).</summary>
    static bool TryParseCsv(string _text, out List<List<string>> _matrix)
    {
        _matrix = new List<List<string>>();
        if (_text == null) return false;

        string t_text = _text.Length > 0 && _text[0] == '﻿' ? _text.Substring(1) : _text;
        var t_row = new List<string>();
        var t_cell = new StringBuilder();
        bool t_quoted = false;

        for (int i = 0; i < t_text.Length; i++)
        {
            char t_char = t_text[i];
            if (t_quoted)
            {
                if (t_char != '"') { t_cell.Append(t_char); continue; }
                if (i + 1 < t_text.Length && t_text[i + 1] == '"') { t_cell.Append('"'); i++; continue; }
                t_quoted = false;
                continue;
            }

            switch (t_char)
            {
                case '"': t_quoted = true; break;
                case ',': t_row.Add(t_cell.ToString()); t_cell.Clear(); break;
                case '\r': break;
                case '\n':
                    t_row.Add(t_cell.ToString());
                    t_cell.Clear();
                    _matrix.Add(t_row);
                    t_row = new List<string>();
                    break;
                default: t_cell.Append(t_char); break;
            }
        }

        if (t_cell.Length > 0 || t_row.Count > 0)
        {
            t_row.Add(t_cell.ToString());
            _matrix.Add(t_row);
        }

        return _matrix.Count > 0;
    }
}

/// <summary>시트를 새로 받으면 SpecData.bytes가 다시 써진다 — 그 뒤를 물고 docs CSV를 맞춘다.</summary>
sealed class SpecDataBytesWatcher : AssetPostprocessor
{
    static void OnPostprocessAllAssets(
        string[] _imported, string[] _deleted, string[] _moved, string[] _movedFrom)
    {
        if (!SpecDocsCsvExporter.AutoExport) return;

        foreach (string t_path in _imported)
        {
            if (!t_path.Replace('\\', '/').EndsWith(SpecDocsCsvExporter.SPEC_BYTES_PATH, StringComparison.OrdinalIgnoreCase))
                continue;

            SpecDocsCsvExporter.RequestExport();
            return;
        }
    }
}
