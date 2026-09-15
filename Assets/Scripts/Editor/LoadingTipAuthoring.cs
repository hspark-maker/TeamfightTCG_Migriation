using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

/// <summary>발행할 로딩 팁을 CSV 진실원에서 읽고 검증한다.</summary>
public static class LoadingTipAuthoring
{
    public const string TABLE_NAME = "LoadingTip";

    /// <summary>발행할 로딩 팁을 CSV 진실원에서 읽는다.</summary>
    public static bool TryLoad(out List<LoadingTip> _rows, out string _error)
    {
        _rows = new List<LoadingTip>();
        if (!TryReadMatrix(out List<List<string>> t_matrix, out _error)) return false;
        var t_ids = new HashSet<int>();
        for (int i = 3; i < t_matrix.Count; i++)
        {
            List<string> t_row = t_matrix[i];
            if (t_row.Count != 3 ||
                !int.TryParse(t_row[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int t_id) ||
                t_id <= 0 || !t_ids.Add(t_id) ||
                !int.TryParse(t_row[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int t_enabled) ||
                (t_enabled != 0 && t_enabled != 1))
            {
                _error = $"LoadingTip CSV {i + 1}행: 고유한 양수 id, 문구, enabled(0/1)가 필요하다.";
                return false;
            }
            _rows.Add(new LoadingTip { id = t_id, text = t_row[1], enabled = t_enabled });
        }
        _rows.Sort((left, right) => left.id.CompareTo(right.id));
        return true;
    }

    static bool TryReadMatrix(out List<List<string>> _matrix, out string _error)
    {
        _matrix = null;
        _error = null;
        try
        {
            string t_path = Path.Combine(SpecDocsCsvExporter.DocsDirectory, TABLE_NAME + "_sheet.csv");
            if (!SpecDocsCsvExporter.TryParseCsv(File.ReadAllText(t_path), out _matrix) ||
                _matrix.Count < 3 || _matrix[0].Count != 3 ||
                string.Join(",", _matrix[1]) != "id,text,enabled" ||
                string.Join(",", _matrix[2]) != "int,string,int")
            {
                _error = "LoadingTip CSV에 설명·id,text,enabled·int,string,int 3줄 헤더가 필요하다.";
                return false;
            }
            return true;
        }
        catch (Exception t_exception)
        {
            _error = "LoadingTip CSV를 읽지 못했다: " + t_exception.Message;
            return false;
        }
    }
}
