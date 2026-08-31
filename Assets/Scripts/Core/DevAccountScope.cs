using UnityEngine;

/// <summary>같은 PC에서 클라이언트를 둘 이상 띄울 때 저장소를 갈라 주는 개발용 스코프.
///
/// ParrelSync 클론과 원본, 같은 제품명의 두 빌드는 <see cref="Application.persistentDataPath"/>와
/// PlayerPrefs를 통째로 공유한다 — Firebase 계정을 갈라도 세이브 폴더와 캐시 소유자 키가 부딪혀
/// 한쪽이 차단된다. 실행 인자(-testAccountId=b)나 ParrelSync 인자에서 이름을 받아 경로·키에 접미사를 붙인다.
///
/// 라이브 빌드에서는 항상 빈 값이라 경로가 그대로다 — 유저 세이브 위치는 절대 바뀌지 않는다.</summary>
public static class DevAccountScope
{
    const string COMMAND_LINE_PREFIX = "-testAccountId=";
    const string PARRELSYNC_ARGUMENT_FILE = ".parrelsyncarg";

    static string s_id;
    static bool s_resolved;

    /// <summary>이 인스턴스의 스코프 이름(없으면 빈 문자열).</summary>
    public static string Id
    {
        get
        {
            if (!s_resolved)
            {
                s_resolved = true;
                s_id = Resolve();
            }
            return s_id;
        }
    }

    public static bool IsActive => !string.IsNullOrEmpty(Id);

    /// <summary>세이브 폴더 이름에 스코프를 붙인다.</summary>
    public static string Folder(string _folder) => IsActive ? $"{_folder}_{Id}" : _folder;

    /// <summary>PlayerPrefs 키에 스코프를 붙인다.</summary>
    public static string Key(string _key) => IsActive ? $"{_key}.{Id}" : _key;

    static string Resolve()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        string t_fromCommandLine = CommandLineValue(COMMAND_LINE_PREFIX);
        if (!string.IsNullOrWhiteSpace(t_fromCommandLine)) return Sanitize(t_fromCommandLine);

#if UNITY_EDITOR
        // ParrelSync의 ClonesManager는 Editor 전용 어셈블리라 런타임에서 참조할 수 없다 — 같은 파일을 직접 읽는다.
        try
        {
            string t_path = System.IO.Path.Combine(Application.dataPath, "..", PARRELSYNC_ARGUMENT_FILE);
            if (System.IO.File.Exists(t_path)) return Sanitize(System.IO.File.ReadAllText(t_path));
        }
        catch (System.Exception t_exception)
        {
            Debug.LogWarning($"[DevAccountScope] ParrelSync 인자 파일을 읽지 못했다: {t_exception.Message}");
        }
#endif
#endif
        return string.Empty;
    }

    static string CommandLineValue(string _prefix)
    {
        string[] t_arguments = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < t_arguments.Length; i++)
            if (t_arguments[i].StartsWith(_prefix, System.StringComparison.OrdinalIgnoreCase))
                return t_arguments[i].Substring(_prefix.Length);

        return string.Empty;
    }

    // 폴더·키에 그대로 들어가므로 안전한 문자만 남긴다.
    static string Sanitize(string _value)
    {
        string t_trimmed = _value.Trim();
        var t_builder = new System.Text.StringBuilder(t_trimmed.Length);
        for (int i = 0; i < t_trimmed.Length; i++)
        {
            char t_char = t_trimmed[i];
            if (char.IsLetterOrDigit(t_char) || t_char == '-' || t_char == '_') t_builder.Append(t_char);
        }
        return t_builder.ToString();
    }
}
