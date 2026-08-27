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
    public const int SchemaVersion = 3;
    public static readonly string[] TableNames =
    {
        "Card", "Card_Test", "CardPack", "CardPackDrop", "TournamentReward", "AlbumReward", "CardEnhance",
    };

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

    public static bool TryBuildRemoteTable(
        string _table, IReadOnlyList<string> _columns,
        IEnumerable<IDictionary<string, object>> _documents,
        out SpecTablePayload _payload, out string _error)
    {
        _payload = null;
        _error = null;
        Type t_rowType = RowTypeOf(_table);
        if (t_rowType == null) { _error = $"Unknown spec table '{_table}'."; return false; }
        FieldInfo[] t_fields = t_rowType.GetFields(BindingFlags.Public | BindingFlags.Instance);
        if (!ValidateFields(_table, t_fields, out _error)) return false;
        if (_columns == null || _columns.Count != t_fields.Length)
        { _error = $"'{_table}' column count mismatch."; return false; }
        for (int i = 0; i < t_fields.Length; i++)
            if (!string.Equals(_columns[i], t_fields[i].Name, StringComparison.Ordinal))
            { _error = $"'{_table}' column mismatch at {i}."; return false; }

        var t_rows = new List<IReadOnlyList<string>>();
        var t_ids = new HashSet<int>();
        foreach (IDictionary<string, object> t_document in _documents)
        {
            var t_values = new string[t_fields.Length];
            for (int i = 0; i < t_fields.Length; i++)
            {
                if (!t_document.TryGetValue(t_fields[i].Name, out object t_value))
                { _error = $"'{_table}' row is missing '{t_fields[i].Name}'."; return false; }
                if (!TryText(t_value, t_fields[i].FieldType, out t_values[i]))
                { _error = $"'{_table}.{t_fields[i].Name}' has an invalid value."; return false; }
            }
            int t_id = int.Parse(t_values[0], CultureInfo.InvariantCulture);
            if (!t_ids.Add(t_id)) { _error = $"'{_table}' contains duplicate id {t_id}."; return false; }
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
        "Card" => typeof(Card), "Card_Test" => typeof(Card_Test), "CardPack" => typeof(CardPack),
        "CardPackDrop" => typeof(CardPackDrop), "TournamentReward" => typeof(TournamentReward),
        "AlbumReward" => typeof(AlbumReward), "CardEnhance" => typeof(CardEnhance), _ => null,
    };

    static bool TryText(object _value, Type _type, out string _text)
    {
        _text = null;
        if (_type == typeof(string)) { _text = _value as string; return _text != null; }
        if (_value is long t_long)
        {
            if (_type == typeof(int) && (t_long < int.MinValue || t_long > int.MaxValue)) return false;
            _text = t_long.ToString(CultureInfo.InvariantCulture); return true;
        }
        if (_value is int t_int) { _text = t_int.ToString(CultureInfo.InvariantCulture); return true; }
        return false;
    }

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
