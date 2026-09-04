using System;

/// <summary>채택한 콘텐츠 공개분의 사용자 공지를 기기에 보류한다.</summary>
public static class ContentUpdateNotice
{
    const string PendingIdKey = "contentUpdate.notice.pendingId";
    const string PendingTitleKey = "contentUpdate.notice.pendingTitle";
    const string PendingBodyKey = "contentUpdate.notice.pendingBody";
    const string SeenIdKey = "contentUpdate.notice.seenId";

    public static bool HasPending
    {
        get
        {
            string t_id = LocalPrefs.GetString(PendingIdKey);
            return !string.IsNullOrEmpty(t_id) &&
                   !string.IsNullOrEmpty(LocalPrefs.GetString(PendingBodyKey)) &&
                   !string.Equals(LocalPrefs.GetString(SeenIdKey), t_id, StringComparison.Ordinal);
        }
    }

    public static string Title => LocalPrefs.GetString(PendingTitleKey);
    public static string Body => LocalPrefs.GetString(PendingBodyKey);

    /// <summary>새 공개분을 실제로 사용할 수 있게 된 뒤 호출한다. 첫 채택은 신규 설치라 공지 없이 본 것으로 심는다.</summary>
    public static void RecordIfUpdated(
        bool _hadPreviousVersion, string _previousVersion,
        string _currentVersion, string _title, string _body)
    {
        if (string.IsNullOrEmpty(_currentVersion)) return;
        if (_hadPreviousVersion && string.Equals(_previousVersion, _currentVersion, StringComparison.Ordinal)) return;

        // 신규 설치. 첫 채택에 패치노트를 띄우면 방금 받은 유저가 "갱신되었습니다"부터 본다.
        if (!_hadPreviousVersion)
        {
            ClearPending();
            LocalPrefs.SetString(SeenIdKey, _currentVersion);
            LocalPrefs.Save();
            return;
        }

        // 이번 공개에 공지가 없다는 것은 "이번엔 알릴 게 없다"일 뿐이다 — 앞서 못 본 공지를 무효화할 이유가 없다.
        // 여기서 seenId를 밀면 본문 없는 hotfix 한 번이 직전 패치노트를 조용히 삼킨다.
        string t_body = (_body ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(t_body)) return;

        // 이미 본 버전은 다시 적지 않는다. 남아 있는 보류는 HasPending이 seenId와 대조해 걸러 준다.
        if (string.Equals(LocalPrefs.GetString(SeenIdKey), _currentVersion, StringComparison.Ordinal)) return;

        LocalPrefs.SetString(PendingIdKey, _currentVersion);
        LocalPrefs.SetString(PendingTitleKey, (_title ?? string.Empty).Trim());
        LocalPrefs.SetString(PendingBodyKey, t_body);
        LocalPrefs.Save();
    }

    public static void MarkSeen()
    {
        string t_id = LocalPrefs.GetString(PendingIdKey);
        if (!string.IsNullOrEmpty(t_id)) LocalPrefs.SetString(SeenIdKey, t_id);
        ClearPending();
        LocalPrefs.Save();
    }

    static void ClearPending()
    {
        LocalPrefs.DeleteKey(PendingIdKey);
        LocalPrefs.DeleteKey(PendingTitleKey);
        LocalPrefs.DeleteKey(PendingBodyKey);
    }
}
