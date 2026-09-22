using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

public static partial class SpecFirestoreUploader
{
    sealed class CosmeticItemUploadRow
    {
        public int id;
        public string itemType;
        public string itemId;
        public int defaultOwned;
    }

    // 서버 전용 제작 가격. 자동 생성 테이블과 SpecData.bytes에 추가하지 않는다.
    sealed class CardCraftUploadRow
    {
        public int id;
        public string grade;
        public int cost;
        public int enabled;
    }

    // 업적은 CSV에서 서버로만 발행하며 생성 테이블과 bytes에 추가하지 않는다.
    sealed class AchievementUploadRow
    {
        public int id;
        public string achievementId;
        public string groupId;
        public int stage;
        public string eventKey;
        public string synergyId;
        public int targetCount;
        public string title;
        public string description;
        public string rewardCurrency;
        public long rewardAmount;
        public int sortOrder;
        public int enabled;
    }

    // 발행 전용 DTO. 자동 생성 Mission과 SpecData.bytes를 수정하지 않는다.
    sealed class MissionUploadRow
    {
        public int id;
        public string missionId;
        public int enabled;
        public string period;
        public string eventKey;
        public int targetCount;
        public string title;
        public string description;
        public long passExp;
        public long accountExp;
        public int sortOrder;
        public int guideActId;
        public string guideActName;
    }

    static bool TryBuildAccountCsvSnapshot(string _table, out TableSnapshot _snapshot, out string _error)
    {
        _snapshot = null;
        _error = null;
        try
        {
            string t_csv = File.ReadAllText(Path.Combine(SpecDocsCsvExporter.DocsDirectory, _table + "_sheet.csv"));
            if (!TryParseAccountCsv(_table, t_csv, out IList t_rows, out _error)) return false;
            return TryBuildSnapshotFrom(t_rows, _table, out _snapshot, out _error);
        }
        catch (Exception t_error) when (t_error is IOException || t_error is UnauthorizedAccessException)
        {
            _error = $"{_table} CSV 읽기 실패: {t_error.Message}";
            return false;
        }
    }

    internal static bool TryParseAccountCsv(string _table, string _csv, out IList _rows, out string _error)
    {
        _rows = null;
        _error = null;
        Type t_type = _table == "CardCraft" ? typeof(CardCraftUploadRow) :
            _table == "CosmeticItem" ? typeof(CosmeticItemUploadRow) :
            _table == "Title" ? typeof(TitleUploadRow) :
            _table == "Achievement" ? typeof(AchievementUploadRow) :
            _table == "Mission" ? typeof(MissionUploadRow) :
            _table == "AccountLevel" ? typeof(AccountLevel) : _table == "Reward" ? typeof(Reward) : null;
        if (t_type == null || !SpecDocsCsvExporter.TryParseCsv(_csv, out List<List<string>> t_matrix))
        { _error = $"{_table} CSV 형식 오류."; return false; }
        int t_header = t_matrix.FindIndex(t => t.Count > 0 && t[0] == "id");
        if (t_header < 0 || t_header > 1 || t_matrix.Count <= t_header + 2)
        { _error = $"{_table} CSV 필드명·타입·데이터 행이 필요하다."; return false; }
        List<string> t_columns = t_matrix[t_header];
        List<string> t_types = t_matrix[t_header + 1];
        FieldInfo[] t_fields = t_type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        if (t_types.Count != t_columns.Count || new HashSet<string>(t_columns).Count != t_columns.Count)
        { _error = $"{_table} CSV 열 수 또는 중복 열 오류."; return false; }
        foreach (string t_column in t_columns)
            if (!t_column.StartsWith("#", StringComparison.Ordinal) && t_type.GetField(t_column) == null)
            { _error = $"{_table} 알 수 없는 열: {t_column}"; return false; }
        foreach (FieldInfo t_field in t_fields)
        {
            int t_index = t_columns.IndexOf(t_field.Name);
            string t_expected = t_field.FieldType == typeof(int) ? "int" : t_field.FieldType == typeof(long) ? "long" : "string";
            if (t_index < 0 || t_types[t_index] != t_expected)
            { _error = $"{_table}.{t_field.Name} 열 또는 타입 오류."; return false; }
        }
        var t_rows = new ArrayList();
        for (int r = t_header + 2; r < t_matrix.Count; r++)
        {
            List<string> t_values = t_matrix[r];
            if (t_values.TrueForAll(string.IsNullOrWhiteSpace)) continue;
            if (t_values.Count != t_columns.Count)
            { _error = $"{_table} {r + 1}행 열 수 오류."; return false; }
            object t_row = Activator.CreateInstance(t_type);
            foreach (FieldInfo t_field in t_fields)
            {
                string t_value = t_values[t_columns.IndexOf(t_field.Name)];
                if (t_field.FieldType == typeof(string)) { t_field.SetValue(t_row, t_value); continue; }
                if (!long.TryParse(t_value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long t_number) ||
                    (t_field.FieldType == typeof(int) && (t_number < int.MinValue || t_number > int.MaxValue)))
                { _error = $"{_table} {r + 1}행 {t_field.Name} 정수 오류."; return false; }
                t_field.SetValue(t_row, t_field.FieldType == typeof(int) ? (object)(int)t_number : t_number);
            }
            t_rows.Add(t_row);
        }
        if (t_rows.Count == 0) { _error = $"{_table} CSV 데이터가 비어 있다."; return false; }
        if (_table == "CosmeticItem" && !ValidateCosmeticItems(t_rows, out _error)) return false;
        if (_table == "Title" && !ValidateTitles(t_rows, out _error)) return false;
        _rows = t_rows;
        return true;
    }

    static bool ValidateCosmeticItems(IList _rows, out string _error)
    {
        _error = null;
        var t_ids = new HashSet<int>();
        var t_items = new HashSet<string>(StringComparer.Ordinal);
        foreach (CosmeticItemUploadRow t_row in _rows)
        {
            if (t_row.id <= 0 || !t_ids.Add(t_row.id))
            { _error = $"CosmeticItem id는 중복 없는 양의 정수여야 한다: {t_row.id}"; return false; }
            if (t_row.itemType != "Avatar" && t_row.itemType != "Frame" && t_row.itemType != "Emote")
            { _error = $"CosmeticItem {t_row.id} 종류 오류: {t_row.itemType}"; return false; }
            if (string.IsNullOrWhiteSpace(t_row.itemId) || t_row.itemId != t_row.itemId.Trim() ||
                !t_items.Add(t_row.itemType + ":" + t_row.itemId))
            { _error = $"CosmeticItem {t_row.id} 아이템 ID가 비었거나 중복이다."; return false; }
            if (t_row.itemType == "Emote" &&
                (!int.TryParse(t_row.itemId, NumberStyles.None, CultureInfo.InvariantCulture, out int t_emoteId) ||
                 t_emoteId <= 0 || t_emoteId.ToString(CultureInfo.InvariantCulture) != t_row.itemId))
            { _error = $"CosmeticItem {t_row.id} 이모티콘 ID는 양의 정수 표기여야 한다."; return false; }
            if (t_row.defaultOwned != 0 && t_row.defaultOwned != 1)
            { _error = $"CosmeticItem {t_row.id} defaultOwned는 0 또는 1이어야 한다."; return false; }
        }
        return true;
    }
}
