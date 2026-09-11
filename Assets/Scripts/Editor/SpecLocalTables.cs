using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

/// <summary>
/// 생성된 SpecData 리소스(Assets/Resources/SpecData.bytes)를 에디터에서 읽어 표를 열거한다.
/// 로컬 표를 읽는 단일 창구다 — 업로더와 docs CSV 내보내기가 같은 경로를 쓴다.
/// RankAiEncounter는 생성 리소스에 앞서 추가된 서버 전용 표라 CSV에서 별도로 읽는다.
/// </summary>
public static class SpecLocalTables
{
    public const string CsvOnlyTableName = "RankAiEncounter";

    /// <summary>신규 서버 전용 표는 생성 리소스와 무관하게 저작 CSV에서 발행한다.</summary>
    public static bool TryLoadRankAiEncounter(out List<RankAiEncounterRow> _rows, out string _error)
    {
        _rows = null;
        try
        {
            return TryParseRankAiEncounter(File.ReadAllText(Path.Combine(
                SpecDocsCsvExporter.DocsDirectory, CsvOnlyTableName + "_sheet.csv")), out _rows, out _error);
        }
        catch (Exception t_exception) when (t_exception is IOException || t_exception is UnauthorizedAccessException)
        { _error = $"{CsvOnlyTableName} CSV 읽기 실패: {t_exception.Message}"; return false; }
    }

    internal static bool TryParseRankAiEncounter(string _csv, out List<RankAiEncounterRow> _rows, out string _error)
    {
        _rows = null;
        _error = null;
        if (!SpecDocsCsvExporter.TryParseCsv(_csv, out List<List<string>> t_matrix))
        { _error = "RankAiEncounter CSV 형식 오류."; return false; }
        int t_header = t_matrix.FindIndex(t => t.Count > 0 && t[0] == "id");
        if (t_header < 0 || t_header > 1 || t_matrix.Count <= t_header + 2)
        { _error = "RankAiEncounter 필드명·타입·데이터 행이 필요하다."; return false; }
        FieldInfo[] t_fields = typeof(RankAiEncounterRow).GetFields(BindingFlags.Public | BindingFlags.Instance);
        List<string> t_columns = t_matrix[t_header];
        List<string> t_types = t_matrix[t_header + 1];
        if (t_columns.Count < t_fields.Length || t_types.Count != t_columns.Count)
        { _error = "RankAiEncounter 열 수가 스키마와 다르다."; return false; }
        for (int c = 0; c < t_columns.Count; c++)
        {
            bool t_valid = c < t_fields.Length
                ? t_columns[c] == t_fields[c].Name && t_types[c] == (t_fields[c].FieldType == typeof(int) ? "int" : "string")
                : t_columns[c].StartsWith("#", StringComparison.Ordinal) && t_types[c] == "string";
            if (!t_valid) { _error = $"RankAiEncounter {c + 1}열 이름·타입 오류."; return false; }
        }
        var t_rows = new List<RankAiEncounterRow>();
        var t_ids = new HashSet<int>();
        var t_keys = new HashSet<string>(StringComparer.Ordinal);
        for (int r = t_header + 2; r < t_matrix.Count; r++)
        {
            List<string> t_values = t_matrix[r];
            if (t_values.TrueForAll(string.IsNullOrWhiteSpace)) continue;
            if (t_values.Count != t_columns.Count)
            { _error = $"RankAiEncounter {r + 1}행 열 수 오류."; return false; }
            var t_row = new RankAiEncounterRow();
            for (int c = 0; c < t_fields.Length; c++)
            {
                if (t_fields[c].FieldType == typeof(string)) { t_fields[c].SetValue(t_row, t_values[c]); continue; }
                if (!int.TryParse(t_values[c], NumberStyles.Integer, CultureInfo.InvariantCulture, out int t_value))
                { _error = $"RankAiEncounter {r + 1}행 {t_fields[c].Name} 정수 오류."; return false; }
                if ((t_fields[c].Name.StartsWith("level", StringComparison.Ordinal) && (t_value < 1 || t_value > 4)) ||
                    (t_fields[c].Name.StartsWith("limitBreak", StringComparison.Ordinal) && (t_value < 0 || t_value > 3)))
                { _error = $"RankAiEncounter {r + 1}행 {t_fields[c].Name} 범위 오류."; return false; }
                t_fields[c].SetValue(t_row, t_value);
            }
            if (t_row.id <= 0 || t_row.tierIndex < 0 || t_row.tierIndex > 19 || string.IsNullOrWhiteSpace(t_row.deckId) ||
                t_row.highlightSlot < 0 || t_row.highlightSlot > 6 ||
                (t_row.battleKind != "Normal" && t_row.battleKind != "DivisionFinal" && t_row.battleKind != "GradeFinal"))
            { _error = $"RankAiEncounter {r + 1}행 식별자·티어·전투 종류·강조 슬롯 오류."; return false; }
            int[] t_levels = { t_row.level1, t_row.level2, t_row.level3, t_row.level4, t_row.level5, t_row.level6 };
            int[] t_breaks = { t_row.limitBreak1, t_row.limitBreak2, t_row.limitBreak3, t_row.limitBreak4, t_row.limitBreak5, t_row.limitBreak6 };
            for (int slot = 0; slot < t_levels.Length; slot++)
                if (t_breaks[slot] > 0 && t_levels[slot] != 4)
                { _error = $"RankAiEncounter {r + 1}행 {slot + 1}번 슬롯 한계돌파는 Lv4에서만 가능하다."; return false; }
            if (!t_ids.Add(t_row.id) || !t_keys.Add($"{t_row.tierIndex}:{t_row.battleKind}:{t_row.deckId}"))
            { _error = $"RankAiEncounter {r + 1}행 id 또는 티어·전투 종류·덱 중복."; return false; }
            t_rows.Add(t_row);
        }
        if (t_rows.Count == 0) { _error = "RankAiEncounter 데이터가 비어 있다."; return false; }
        t_rows.Sort((a, b) => a.id.CompareTo(b.id));
        _rows = t_rows;
        return true;
    }

    /// <summary>생성된 리소스를 파싱해 SpecDataManager 인스턴스를 만든다.</summary>
    public static bool TryLoadManager(out object _manager, out string _error)
    {
        _manager = null;
        _error = null;

        string t_json = SpecDataResourceLoader.LoadSpecData();
        if (string.IsNullOrEmpty(t_json))
        {
            _error = "SpecData 리소스를 못 읽었다. CookApps > SpecData 창에서 '시트 적용 & CS 생성'을 먼저 실행할 것.";
            return false;
        }

        var t_manager = new SpecDataManager();
        if (!t_manager.Load(t_json))
        {
            _error = "SpecData 파싱 실패. 생성된 리소스가 손상됐을 수 있다(재생성 필요).";
            return false;
        }

        _manager = t_manager;
        return true;
    }

    /// <summary>표 하나. 행이 0개여도 컬럼을 알 수 있게 행 타입을 같이 나른다.</summary>
    public readonly struct SpecTable
    {
        public readonly string Name;
        public readonly IEnumerable Rows;
        /// <summary>컨테이너의 All 프로퍼티에서 뽑은 원소 타입. 못 뽑으면 null.</summary>
        public readonly Type RowType;

        public SpecTable(string _name, IEnumerable _rows, Type _rowType)
        {
            Name = _name;
            Rows = _rows;
            RowType = _rowType;
        }
    }

    /// <summary>manager의 공개 표 프로퍼티를 훑는다. 행이 없는 표도 그대로 나온다.</summary>
    public static IEnumerable<SpecTable> EnumerateTables(object _manager)
    {
        foreach (PropertyInfo t_property in _manager.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (t_property.GetIndexParameters().Length > 0) continue;

            object t_container;
            try { t_container = t_property.GetValue(_manager); }
            catch (Exception) { continue; }
            if (t_container == null) continue;

            PropertyInfo t_all = t_container.GetType().GetProperty("All", BindingFlags.Public | BindingFlags.Instance);
            if (t_all?.GetValue(t_container) is IEnumerable t_rows)
                yield return new SpecTable(t_property.Name, t_rows, ElementType(t_all.PropertyType));
        }
    }

    /// <summary>이름과 행만 필요한 호출부용 얇은 겉면.</summary>
    public static IEnumerable<KeyValuePair<string, IEnumerable>> Enumerate(object _manager)
    {
        foreach (SpecTable t_table in EnumerateTables(_manager))
            yield return new KeyValuePair<string, IEnumerable>(t_table.Name, t_table.Rows);
    }

    /// <summary>IReadOnlyList&lt;T&gt;·List&lt;T&gt;·T[] 어느 쪽이든 T를 꺼낸다.</summary>
    static Type ElementType(Type _collection)
    {
        if (_collection == null) return null;
        if (_collection.IsArray) return _collection.GetElementType();

        if (_collection.IsGenericType)
        {
            Type[] t_args = _collection.GetGenericArguments();
            if (t_args.Length == 1) return t_args[0];
        }

        foreach (Type t_interface in _collection.GetInterfaces())
            if (t_interface.IsGenericType && t_interface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return t_interface.GetGenericArguments()[0];

        return null;
    }
}
