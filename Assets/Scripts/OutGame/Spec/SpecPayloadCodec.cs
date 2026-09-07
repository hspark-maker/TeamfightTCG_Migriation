using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

public sealed class SpecTablePayload
{
    public string Table { get; internal set; }
    public IReadOnlyList<string> Columns { get; internal set; }
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; internal set; }
    public string PayloadHash { get; internal set; }
}

public static class SpecPayloadCodec
{
    // 기존 메타·구클라이언트와의 호환 alias. 값의 의미는 앱 콘텐츠 호환 세대다.
    public const int SchemaVersion = ContentVersion.Major;
    public static readonly string[] TableNames =
    {
        "Card", "CardPack", "CardPackDrop", "AIDeck", "Reward",
        "RankGrade", "KeywordEnhance", "CardEnhance", "CardEnhanceRule", "CardLimitBreak",
        "AlbumEntry", "AlbumThemeInfo",
        "SynergyDef", "SynergyTierDef", "SynergyEffectDef",
        "AccountLevel",
        "Roulette", "RouletteSlot",
        "AdventureChapter",
        "PassSeason", "PassLevel",
    };

    /// <summary>
    /// 매니저에 있지만 <b>일부러</b> 클라로 동기화하지 않는 표. 아래 경고에서 제외한다.
    ///
    /// <para><c>Mission</c> — 미션 정의의 진실원은 서버다. 클라는 <c>getMissions</c> 응답으로 정의를
    /// 받아 그리고 사본을 두지 않는다. 여기서 동기화하면 그 사본이 생겨 밸런스 수정이 앱 배포에 묶이고,
    /// 서버 카탈로그와 갈리는 순간 화면에 보이는 목표와 실제 거절 조건이 달라진다.</para>
    ///
    /// <para>목록으로 거르는 이유는 <see cref="ServerOwnedRewardOwners"/> 와 같다 — 통째로 조용히
    /// 만들면 진짜로 빠뜨린 표까지 묻힌다. 제외는 이름을 적는 의도적 행위여야 한다.</para>
    /// </summary>
    static readonly string[] IntentionallyUnsynced = { "Mission" };

    /// <summary>
    /// 매니저가 들고 있는데 <see cref="TableNames"/> 에 없는 표를 찾아 알린다.
    ///
    /// <para><b>이 목록에 빠진 표는 동기화가 통째로 버린다.</b> 채택은 여기 적힌 표로만 스냅샷을 다시
    /// 짓기 때문에, 목록에 없으면 로컬에 데이터가 있어도 채택 뒤 0행이 된다. 그런데 대조 로그는
    /// "불일치 0/N" 으로 정상처럼 보이고(없는 표끼리는 비교하지 않는다) 컴파일도 통과한다 —
    /// 실제로 <c>AdventureChapter</c> 가 이 상태로 초기화를 막았고, 원인이 표 데이터가 아니라
    /// 이 배열이라는 걸 알아내는 데 로그만으로는 도달하지 못했다.</para>
    ///
    /// <para>막지는 않는다. 표 하나가 목록에서 빠진 것과 게임을 못 켜는 것은 다른 무게다 —
    /// 여기서 세우면 저작 실수가 전 유저의 초기화를 끊는다.</para>
    /// </summary>
    public static void WarnUncoveredTables(object _manager)
    {
        if (_manager == null) return;

        var t_covered = new HashSet<string>(TableNames, StringComparer.Ordinal);
        foreach (string t_skip in IntentionallyUnsynced) t_covered.Add(t_skip);

        foreach (PropertyInfo t_property in _manager.GetType()
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (t_covered.Contains(t_property.Name)) continue;

            // 표 컨테이너만 고른다 — All 을 가진 프로퍼티가 표다(TryBuildLocalTable 과 같은 판정).
            object t_container;
            try { t_container = t_property.GetValue(_manager); }
            catch (Exception) { continue; }
            if (t_container?.GetType().GetProperty("All", BindingFlags.Public | BindingFlags.Instance) == null)
                continue;

            UnityEngine.Debug.LogError(
                $"[SpecPayloadCodec] 표 '{t_property.Name}' 가 동기화 목록(TableNames)에 없다 — " +
                "채택 뒤 0행이 되어 이 표를 읽는 초기화가 실패한다. TableNames 와 RowTypeOf 에 함께 추가할 것.");
        }
    }

    public static bool TryBuildLocalTable(object _manager, string _table, out SpecTablePayload _payload, out string _error)
    {
        _payload = null;
        _error = null;
        if (_manager == null) { _error = "SpecData manager is null."; return false; }

        PropertyInfo t_property = _manager.GetType().GetProperty(_table, BindingFlags.Public | BindingFlags.Instance);
        object t_container = t_property?.GetValue(_manager);
        IEnumerable t_source = t_container?.GetType().GetProperty("All", BindingFlags.Public | BindingFlags.Instance)?.GetValue(t_container) as IEnumerable;
        if (t_source == null) { _error = $"Spec table '{_table}' is missing."; return false; }

        Type t_rowType = RowTypeOf(_table);
        if (t_rowType == null) { _error = $"Unknown spec table '{_table}'."; return false; }
        FieldInfo[] t_fields = t_rowType.GetFields(BindingFlags.Public | BindingFlags.Instance);
        if (!ValidateFields(_table, t_fields, out _error)) return false;

        var t_rows = new List<IReadOnlyList<string>>();
        foreach (object t_row in t_source)
        {
            if (t_row == null) { _error = $"'{_table}' contains a null row."; return false; }
            var t_values = new string[t_fields.Length];
            for (int i = 0; i < t_fields.Length; i++) t_values[i] = Text(t_fields[i].GetValue(t_row));
            t_rows.Add(t_values);
        }
        t_rows.Sort((a, b) => int.Parse(a[0], CultureInfo.InvariantCulture).CompareTo(int.Parse(b[0], CultureInfo.InvariantCulture)));
        return TryCreate(_table, t_fields, t_rows, out _payload, out _error);
    }

    /// <summary>해시를 계산할 때 쓰는 정규화 텍스트(<c>[[열…],[값…],…]</c>)를 되읽는다.
    /// 표 하나를 행 문서 N개가 아니라 블롭 문서 1개로 내려받기 위한 경로다 — read가 행 수에 비례하지 않는다.</summary>
    public static bool TryBuildFromPayloadText(
        string _table, string _text, out SpecTablePayload _payload, out string _error)
    {
        _payload = null;
        _error = null;
        Type t_rowType = RowTypeOf(_table);
        if (t_rowType == null) { _error = $"Unknown spec table '{_table}'."; return false; }
        FieldInfo[] t_fields = t_rowType.GetFields(BindingFlags.Public | BindingFlags.Instance);
        if (!ValidateFields(_table, t_fields, out _error)) return false;
        if (!TryParseStringMatrix(_text, out List<string[]> t_matrix, out string t_parseError))
        { _error = $"'{_table}' payload parse failed: {t_parseError}"; return false; }
        if (t_matrix.Count < 2) { _error = $"'{_table}' payload has no rows."; return false; }

        string[] t_columns = t_matrix[0];
        if (t_columns.Length != t_fields.Length) { _error = $"'{_table}' column count mismatch."; return false; }
        for (int i = 0; i < t_fields.Length; i++)
            if (!string.Equals(t_columns[i], t_fields[i].Name, StringComparison.Ordinal))
            { _error = $"'{_table}' column mismatch at {i}."; return false; }

        var t_rows = new List<IReadOnlyList<string>>(t_matrix.Count - 1);
        var t_ids = new HashSet<int>();
        for (int r = 1; r < t_matrix.Count; r++)
        {
            string[] t_values = t_matrix[r];
            if (t_values.Length != t_fields.Length)
            { _error = $"'{_table}' row {r} has {t_values.Length} values, expected {t_fields.Length}."; return false; }
            for (int c = 0; c < t_fields.Length; c++)
            {
                if (t_fields[c].FieldType == typeof(string)) continue;
                if (!long.TryParse(t_values[c], NumberStyles.Integer, CultureInfo.InvariantCulture, out long t_number) ||
                    (t_fields[c].FieldType == typeof(int) && (t_number < int.MinValue || t_number > int.MaxValue)))
                { _error = $"'{_table}.{t_fields[c].Name}' has an invalid value."; return false; }
            }
            if (!t_ids.Add(int.Parse(t_values[0], CultureInfo.InvariantCulture)))
            { _error = $"'{_table}' contains duplicate id {t_values[0]}."; return false; }
            t_rows.Add(t_values);
        }
        t_rows.Sort((a, b) => int.Parse(a[0], CultureInfo.InvariantCulture).CompareTo(int.Parse(b[0], CultureInfo.InvariantCulture)));
        return TryCreate(_table, t_fields, t_rows, out _payload, out _error);
    }

    public static string BuildManagerJson(IReadOnlyList<SpecTablePayload> _tables)
    {
        var t_builder = new StringBuilder(16384).Append('{');
        for (int i = 0; i < _tables.Count; i++)
        {
            if (i > 0) t_builder.Append(',');
            SpecTablePayload t_table = _tables[i];
            AppendJsonString(t_builder, t_table.Table);
            t_builder.Append(':').Append('[');
            Type t_type = RowTypeOf(t_table.Table);
            FieldInfo[] t_fields = t_type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int r = 0; r < t_table.Rows.Count; r++)
            {
                if (r > 0) t_builder.Append(',');
                t_builder.Append('{');
                for (int c = 0; c < t_fields.Length; c++)
                {
                    if (c > 0) t_builder.Append(',');
                    AppendJsonString(t_builder, t_fields[c].Name);
                    t_builder.Append(':');
                    if (t_fields[c].FieldType == typeof(string)) AppendJsonString(t_builder, t_table.Rows[r][c]);
                    else t_builder.Append(t_table.Rows[r][c]);
                }
                t_builder.Append('}');
            }
            t_builder.Append(']');
        }
        return t_builder.Append('}').ToString();
    }

    public static string CombinedHash(string _envId, IReadOnlyList<SpecTablePayload> _tables)
    {
        var t_builder = new StringBuilder(_envId ?? string.Empty);
        foreach (SpecTablePayload t_table in _tables) t_builder.Append('|').Append(t_table.Table).Append('=').Append(t_table.PayloadHash);
        using SHA256 t_sha = SHA256.Create();
        return Hex(t_sha.ComputeHash(Encoding.UTF8.GetBytes(t_builder.ToString())));
    }

    static bool TryCreate(string _table, FieldInfo[] _fields, List<IReadOnlyList<string>> _rows,
                          out SpecTablePayload _payload, out string _error)
    {
        _payload = null;
        _error = null;
        if (_rows.Count == 0) { _error = $"'{_table}' has no rows."; return false; }
        var t_columns = new List<string>(_fields.Length);
        foreach (FieldInfo t_field in _fields) t_columns.Add(t_field.Name);
        var t_normalized = new StringBuilder(4096).Append('[');
        AppendStringArray(t_normalized, t_columns);
        foreach (IReadOnlyList<string> t_row in _rows) { t_normalized.Append(','); AppendStringArray(t_normalized, t_row); }
        t_normalized.Append(']');
        using MD5 t_md5 = MD5.Create();
        byte[] t_hash = t_md5.ComputeHash(Encoding.UTF8.GetBytes(t_normalized.ToString()));
        _payload = new SpecTablePayload
        {
            Table = _table, Columns = t_columns.AsReadOnly(), Rows = _rows.AsReadOnly(),
            PayloadHash = Hex(t_hash, 8),
        };
        return true;
    }

    /// <summary><see cref="AppendStringArray"/>가 만든 형태만 받는다 — 공백도 중첩도 없는 2단 문자열 배열.
    /// 관대한 JSON 파서가 아니다. 형태가 어긋나면 조용히 넘기지 않고 실패시킨다.
    /// 표 타입을 모르는 호출부(에디터 업로더의 행 대조)도 쓰므로 public이다 — payload 형식을 아는 곳은 여기 하나다.</summary>
    public static bool TryParseStringMatrix(string _text, out List<string[]> _matrix, out string _error)
    {
        _matrix = new List<string[]>();
        _error = null;
        int t_length = _text?.Length ?? 0;
        int t_index = 0;
        if (t_length < 2 || _text[t_index++] != '[') { _error = "missing outer '['"; return false; }
        if (_text[t_index] == ']')
            return ++t_index == t_length || Fail(out _error, "trailing text after payload");

        while (true)
        {
            if (t_index >= t_length || _text[t_index++] != '[') return Fail(out _error, "missing row '['");
            var t_values = new List<string>();
            if (t_index < t_length && _text[t_index] == ']') t_index++;
            else
                while (true)
                {
                    if (!TryReadJsonString(_text, ref t_index, out string t_value, out _error)) return false;
                    t_values.Add(t_value);
                    if (t_index >= t_length) return Fail(out _error, "unterminated row");
                    char t_delimiter = _text[t_index++];
                    if (t_delimiter == ',') continue;
                    if (t_delimiter == ']') break;
                    return Fail(out _error, $"unexpected '{t_delimiter}' in row");
                }

            _matrix.Add(t_values.ToArray());
            if (t_index >= t_length) return Fail(out _error, "unterminated payload");
            char t_next = _text[t_index++];
            if (t_next == ',') continue;
            if (t_next == ']') break;
            return Fail(out _error, $"unexpected '{t_next}' between rows");
        }
        return t_index == t_length || Fail(out _error, "trailing text after payload");
    }

    static bool TryReadJsonString(string _text, ref int _index, out string _value, out string _error)
    {
        _value = null;
        _error = null;
        int t_length = _text.Length;
        if (_index >= t_length || _text[_index++] != '"') { _error = "missing '\"'"; return false; }

        var t_builder = new StringBuilder();
        while (_index < t_length)
        {
            char t_char = _text[_index++];
            if (t_char == '"') { _value = t_builder.ToString(); return true; }
            if (t_char != '\\') { t_builder.Append(t_char); continue; }
            if (_index >= t_length) break;
            char t_escape = _text[_index++];
            switch (t_escape)
            {
                case '"': t_builder.Append('"'); break;
                case '\\': t_builder.Append('\\'); break;
                case '/': t_builder.Append('/'); break;
                case 'b': t_builder.Append('\b'); break;
                case 'f': t_builder.Append('\f'); break;
                case 'n': t_builder.Append('\n'); break;
                case 'r': t_builder.Append('\r'); break;
                case 't': t_builder.Append('\t'); break;
                case 'u':
                    if (_index + 4 > t_length ||
                        !int.TryParse(_text.Substring(_index, 4), NumberStyles.HexNumber,
                                      CultureInfo.InvariantCulture, out int t_code))
                        return Fail(out _error, "bad \\u escape");
                    t_builder.Append((char)t_code);
                    _index += 4;
                    break;
                default: return Fail(out _error, $"bad escape '\\{t_escape}'");
            }
        }
        _error = "unterminated string";
        return false;
    }

    static bool Fail(out string _error, string _message)
    {
        _error = _message;
        return false;
    }

    static bool ValidateFields(string _table, FieldInfo[] _fields, out string _error)
    {
        _error = null;
        if (_fields.Length == 0 || _fields[0].Name != "id" || _fields[0].FieldType != typeof(int))
        { _error = $"'{_table}' requires int id as its first field."; return false; }
        foreach (FieldInfo t_field in _fields)
            if (t_field.FieldType != typeof(int) && t_field.FieldType != typeof(long) && t_field.FieldType != typeof(string))
            { _error = $"Unsupported field type: {_table}.{t_field.Name}."; return false; }
        return true;
    }

    static Type RowTypeOf(string _table) => _table switch
    {
        "Card" => typeof(Card), "CardPack" => typeof(CardPack),
        "CardPackDrop" => typeof(CardPackDrop), "AIDeck" => typeof(AIDeck), "Reward" => typeof(Reward),
        "RankGrade" => typeof(RankGrade), "KeywordEnhance" => typeof(KeywordEnhance),
        "CardEnhance" => typeof(CardEnhance), "CardEnhanceRule" => typeof(CardEnhanceRule),
        "CardLimitBreak" => typeof(CardLimitBreak),
        "AlbumEntry" => typeof(AlbumEntry), "AlbumThemeInfo" => typeof(AlbumThemeInfo),
        "SynergyDef" => typeof(SynergyDef), "SynergyTierDef" => typeof(SynergyTierDef),
        "SynergyEffectDef" => typeof(SynergyEffectDef),
        "AccountLevel" => typeof(AccountLevel),
        "Roulette" => typeof(Roulette), "RouletteSlot" => typeof(RouletteSlot),
        "AdventureChapter" => typeof(AdventureChapter),
        "PassSeason" => typeof(PassSeason), "PassLevel" => typeof(PassLevel),
        _ => null,
    };

    static string Text(object _value) => _value switch
    {
        null => string.Empty, string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => _value.ToString(),
    };

    static void AppendStringArray(StringBuilder _builder, IEnumerable<string> _values)
    {
        _builder.Append('['); bool t_first = true;
        foreach (string t_value in _values) { if (!t_first) _builder.Append(','); t_first = false; AppendJsonString(_builder, t_value); }
        _builder.Append(']');
    }

    static void AppendJsonString(StringBuilder _builder, string _value)
    {
        _builder.Append('"');
        foreach (char t_char in _value ?? string.Empty)
        {
            switch (t_char)
            {
                case '"': _builder.Append("\\\""); break; case '\\': _builder.Append("\\\\"); break;
                case '\b': _builder.Append("\\u0008"); break; case '\f': _builder.Append("\\u000c"); break;
                case '\n': _builder.Append("\\n"); break; case '\r': _builder.Append("\\r"); break;
                case '\t': _builder.Append("\\t"); break;
                default: if (t_char < 32) _builder.Append("\\u").Append(((int)t_char).ToString("x4")); else _builder.Append(t_char); break;
            }
        }
        _builder.Append('"');
    }

    static string Hex(byte[] _bytes, int _count = -1)
    {
        if (_count < 0) _count = _bytes.Length;
        var t_builder = new StringBuilder(_count * 2);
        for (int i = 0; i < _count; i++) t_builder.Append(_bytes[i].ToString("x2"));
        return t_builder.ToString();
    }
}
