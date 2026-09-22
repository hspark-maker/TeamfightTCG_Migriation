using System;
using System.Collections;
using System.Collections.Generic;

public static partial class SpecFirestoreUploader
{
    sealed class TitleUploadRow
    {
        public int id;
        public string titleId;
    }

    static bool ValidateTitles(IList _rows, out string _error)
    {
        _error = null;
        var t_ids = new HashSet<int>();
        var t_titles = new HashSet<string>(StringComparer.Ordinal);
        foreach (TitleUploadRow t_row in _rows)
        {
            if (t_row.id <= 0 || !t_ids.Add(t_row.id) || string.IsNullOrWhiteSpace(t_row.titleId) ||
                t_row.titleId != t_row.titleId.Trim() || !t_titles.Add(t_row.titleId))
            {
                _error = $"Title ID는 중복 없이 지정해야 한다: {t_row.id} / {t_row.titleId}";
                return false;
            }
        }
        return true;
    }
}
