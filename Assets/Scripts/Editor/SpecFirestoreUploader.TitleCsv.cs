using System;
using System.Collections;
using System.Collections.Generic;

public static partial class SpecFirestoreUploader
{
    sealed class TitleUploadRow
    {
        public int id;
        public string titleId;
        public string eventKey;
        public string synergyId;
        public int targetCount;
        public string description;
    }

    static bool ValidateTitles(IList _rows, out string _error)
    {
        _error = null;
        var t_ids = new HashSet<int>();
        var t_titles = new HashSet<string>(StringComparer.Ordinal);
        foreach (TitleUploadRow t_row in _rows)
        {
            if (t_row.id <= 0 || !t_ids.Add(t_row.id) || string.IsNullOrWhiteSpace(t_row.titleId) ||
                t_row.titleId != t_row.titleId.Trim() || !t_titles.Add(t_row.titleId) ||
                string.IsNullOrWhiteSpace(t_row.description) || t_row.description != t_row.description.Trim())
            {
                _error = $"Title ID는 중복 없이 지정해야 한다: {t_row.id} / {t_row.titleId}";
                return false;
            }
            if (string.IsNullOrEmpty(t_row.eventKey) && string.IsNullOrEmpty(t_row.synergyId) && t_row.targetCount == 0)
                continue;
            if (t_row.targetCount <= 0 ||
                Array.IndexOf(new[] { "WinBattle", "DestroyCards", "PlaySynergy", "CompleteAlbum", "WinStreak", "OpenPack" }, t_row.eventKey) < 0 ||
                (t_row.eventKey == "PlaySynergy"
                    ? !System.Text.RegularExpressions.Regex.IsMatch(t_row.synergyId ?? string.Empty, "^[A-Za-z][A-Za-z0-9_]{0,63}$")
                    : !string.IsNullOrEmpty(t_row.synergyId)))
            {
                _error = $"Title 해금 이벤트·목표값·설명을 확인해야 한다: {t_row.titleId}";
                return false;
            }
        }
        return true;
    }
}
