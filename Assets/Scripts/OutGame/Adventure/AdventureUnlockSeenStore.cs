using System.Collections.Generic;

/// <summary>해금 연출을 이미 보여준 챕터·정점의 표식 보관소(기기 로컬).
///
/// <para>세이브 슬롯에 두지 않는 이유는 이 값이 계정의 진행도가 아니라 "이 기기에서 이 연출을 봤다"는
/// 화면 이력이기 때문이다. 서버는 이 값을 판정할 것이 없어 받은 그대로 되돌려 쓰기만 하는데,
/// 그 되돌려 쓰는 값은 서버가 트랜잭션에서 읽은 시점의 것이라 방금 세운 표식이 아직 올라가기 전이면
/// 채택이 그 표식을 지워 이미 본 연출이 다음 진입에서 되풀이됐다.</para>
///
/// <para>기기를 바꾸면 표식이 비지만 연출이 무더기로 터지지는 않는다 —
/// <see cref="AdventureProgress.TryBackfillSeenUnlocks"/> 가 진행 흔적을 보고 조용히 소급 표식한다.</para></summary>
public static class AdventureUnlockSeenStore
{
    const string PREFS_KEY = "outgame.adventure.seenUnlocks";

    // 첫 줄은 이 목록의 주인(uid), 이후 줄이 표식이다. 계정이 갈리면 통째로 버린다 —
    // 같은 기기에서 계정을 바꿨을 때 남의 표식이 새 계정의 첫 해금 연출을 삼킨다.
    const char LINE_SEPARATOR = '\n';

    static readonly List<string> s_seen = new List<string>();

    static string s_loadedOwner;
    static bool s_loaded;

    /// <summary>표식 수. 0이면 이 기기에서 아직 아무 해금 연출도 보지 않았다.</summary>
    public static int Count
    {
        get
        {
            EnsureLoaded();
            return s_seen.Count;
        }
    }

    /// <summary>이미 보여준 해금인가.</summary>
    public static bool Contains(string _id)
    {
        if (string.IsNullOrEmpty(_id)) return false;

        EnsureLoaded();
        return s_seen.Contains(_id);
    }

    /// <summary>표식을 세운다(중복은 무시). 디스크에 굳히는 것은 <see cref="Flush"/> 가 한다 —
    /// 배치로 세우는 자리가 있어 한 건마다 파일을 쓰지 않는다.</summary>
    public static bool Add(string _id)
    {
        if (string.IsNullOrEmpty(_id)) return false;

        EnsureLoaded();
        if (s_seen.Contains(_id)) return false;

        s_seen.Add(_id);
        return true;
    }

    /// <summary>세운 표식을 디스크에 굳힌다.</summary>
    public static void Flush()
    {
        EnsureLoaded();

        LocalPrefs.SetString(PREFS_KEY, Serialize());
        LocalPrefs.Save();
    }

    /// <summary>표식을 전부 지운다(튜토리얼 되감기·디버그 초기화). 첫실행과 같은 자리로 되돌린다.</summary>
    // 키를 남기지 않고 지운다 — 로그인 전이라 주인을 모르는 상태에서 빈 목록을 써 두면
    // 그 줄이 남의 uid로 저장되어, 실제 계정이 붙었을 때 무엇이 지워진 것인지 읽을 수 없다.
    public static void Clear()
    {
        s_seen.Clear();
        s_loaded = false;
        s_loadedOwner = null;

        LocalPrefs.DeleteKey(PREFS_KEY);
        LocalPrefs.Save();
    }

    static void EnsureLoaded()
    {
        string t_owner = CurrentOwner();

        // 로그인 전에는 uid가 비어 있다 — 그 상태로 읽어 둔 목록은 계정이 붙는 순간 주인이 갈릴 수 있으므로
        // 주인이 바뀔 때마다 다시 읽는다(파일이 아니라 메모리 캐시만 버린다).
        if (s_loaded && string.Equals(s_loadedOwner, t_owner)) return;

        s_loaded = true;
        s_loadedOwner = t_owner;
        s_seen.Clear();

        string t_raw = LocalPrefs.GetString(PREFS_KEY, string.Empty);
        if (string.IsNullOrEmpty(t_raw)) return;

        string[] t_lines = t_raw.Split(LINE_SEPARATOR);

        // 주인이 다른 목록은 읽지 않는다(빈 목록으로 시작해 소급 표식이 받아 준다).
        if (t_lines.Length == 0 || !string.Equals(t_lines[0], t_owner)) return;

        for (int t_i = 1; t_i < t_lines.Length; t_i++)
            if (!string.IsNullOrEmpty(t_lines[t_i])) s_seen.Add(t_lines[t_i]);
    }

    static string Serialize() => s_loadedOwner + LINE_SEPARATOR + string.Join(LINE_SEPARATOR.ToString(), s_seen);

    static string CurrentOwner() => FirebaseAuthService.Instance != null
        ? FirebaseAuthService.Instance.UserId ?? string.Empty
        : string.Empty;
}
