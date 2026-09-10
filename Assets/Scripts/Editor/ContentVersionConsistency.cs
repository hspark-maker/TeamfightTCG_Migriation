using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>테이블 세대 상수가 C#·content-version.json·서버 TS 세 곳에서 같은지 본다.
/// 앱 빌드 버전(bundleVersion)은 여기서 보지 않는다 — 테이블 세대와 묶여 있지 않다.</summary>
public static class ContentVersionConsistency
{
    // OnGUI 블로커에서 매 리페인트마다 불린다 — 파일 두 개를 읽고 정규식을 돌리는 비용을 그대로 두면
    // 창을 띄워둔 동안 계속 디스크를 친다. 짧게 캐시하고 만료되면 다시 본다.
    const double CacheSeconds = 2d;
    static double s_checkedAt = double.NegativeInfinity;
    static bool s_cachedOk;
    static string s_cachedError;

    /// <summary>파일을 고친 직후처럼 즉시 다시 보고 싶을 때 캐시를 버린다.</summary>
    public static void Invalidate() => s_checkedAt = double.NegativeInfinity;

    [Serializable]
    sealed class Manifest
    {
        public int major;
        public int minAppMajor;
        public int[] supported;
    }

    /// <summary>호환 계약 변경용 새 세대를 준비한다. 공개·산출물 생성은 하지 않는다.</summary>
    public static bool TryAdvanceGeneration(out int _nextMajor, out string _error)
    {
        _nextMajor = ContentVersion.Major;
        Invalidate();
        if (!TryValidate(out _error)) return false;
        try
        {
            string t_root = Directory.GetParent(Application.dataPath)?.FullName;
            AdvanceGeneration(t_root, ContentVersion.Major);
            _nextMajor = checked(ContentVersion.Major + 1);
            Invalidate();
            return true;
        }
        catch (Exception t_exception)
        {
            Invalidate();
            _error = "테이블 세대 변경 실패: " + t_exception.Message;
            return false;
        }
    }

    // 모든 선언을 먼저 검사한 뒤 쓴다. 실패하면 이미 쓴 파일도 원래 바이트로 복원한다.
    internal static void AdvanceGeneration(string _root, int _expectedMajor)
    {
        int t_next = checked(_expectedMajor + 1);
        string[] t_paths =
        {
            Path.Combine(_root, "content-version.json"),
            Path.Combine(_root, "Assets/Scripts/OutGame/Spec/ContentVersion.cs"),
            Path.Combine(_root, "functions/src/specs/specBlobReader.ts"),
        };
        var t_original = new byte[t_paths.Length][];
        var t_text = new string[t_paths.Length];
        for (int i = 0; i < t_paths.Length; i++)
        {
            t_original[i] = File.ReadAllBytes(t_paths[i]);
            t_text[i] = File.ReadAllText(t_paths[i]);
        }
        Manifest t_manifest = JsonUtility.FromJson<Manifest>(t_text[0]);
        if (t_manifest == null || t_manifest.major != _expectedMajor)
            throw new InvalidOperationException("테이블 세대가 이미 변경됐다. 컴파일 완료 후 다시 확인할 것.");

        string t_number = t_next.ToString(CultureInfo.InvariantCulture);
        t_text[0] = ReplaceVersionValue(t_text[0], "(\"major\"\\s*:\\s*)(\\d+)", t_number);
        t_text[0] = ReplaceVersionValue(t_text[0], "(\"minAppMajor\"\\s*:\\s*)(\\d+)", t_number);
        t_text[0] = ReplaceVersionValue(t_text[0], "(\"supported\"\\s*:\\s*\\[)([^\\]]*)", t_number);
        t_text[1] = ReplaceVersionValue(t_text[1],
            @"(content-version:major\s*[\r\n]+\s*public const int Major = )(\d+)", t_number);
        t_text[1] = ReplaceVersionValue(t_text[1],
            @"(content-version:min-app-major\s*[\r\n]+\s*public const int MinAppMajor = )(\d+)", t_number);
        t_text[1] = ReplaceVersionValue(t_text[1],
            @"(content-version:supported\s*[\r\n]+\s*static readonly int\[\] SupportedMajors = \{)([^}]*)", " Major ");
        t_text[2] = ReplaceVersionValue(t_text[2],
            @"(content-version:major\s*[\r\n]+\s*const CONTENT_MAJOR = )(\d+)", t_number);
        t_text[2] = ReplaceVersionValue(t_text[2],
            @"(content-version:supported\s*[\r\n]+\s*const SUPPORTED_CONTENT_MAJORS = new Set<number>\(\[)([^\]]*)", "CONTENT_MAJOR");

        int t_writing = -1;
        try
        {
            for (int i = 0; i < t_paths.Length; i++)
            {
                // 미리보기 이후 외부 편집이 끼어들면 덮어쓰지 않는다.
                if (File.ReadAllText(t_paths[i]) != Encoding.UTF8.GetString(t_original[i]).TrimStart('\uFEFF'))
                    throw new IOException($"파일이 변경됐다: {t_paths[i]}");
                t_writing = i;
                bool t_bom = t_original[i].Length >= 3 && t_original[i][0] == 0xEF &&
                             t_original[i][1] == 0xBB && t_original[i][2] == 0xBF;
                File.WriteAllText(t_paths[i], t_text[i], new UTF8Encoding(t_bom));
            }
        }
        catch (Exception t_writeError)
        {
            var t_errors = new System.Collections.Generic.List<Exception> { t_writeError };
            for (int i = t_writing; i >= 0; i--)
            {
                try
                {
                    if (!File.ReadAllBytes(t_paths[i]).SequenceEqual(t_original[i]))
                        File.WriteAllBytes(t_paths[i], t_original[i]);
                }
                catch (Exception t_restoreError) { t_errors.Add(t_restoreError); }
            }
            if (t_errors.Count > 1)
                throw new AggregateException("테이블 세대 파일 복원 실패. 파일 상태를 확인할 것.", t_errors);
            throw;
        }
    }

    static string ReplaceVersionValue(string _source, string _pattern, string _value)
    {
        var t_regex = new Regex(_pattern);
        if (t_regex.Matches(_source).Count != 1)
            throw new InvalidOperationException("버전 선언을 하나로 찾을 수 없다: " + _pattern);
        return t_regex.Replace(_source, t_match => t_match.Groups[1].Value + _value);
    }

    public static bool TryValidate(out string _error)
    {
        double t_now = EditorApplication.timeSinceStartup;
        if (t_now - s_checkedAt < CacheSeconds)
        {
            _error = s_cachedError;
            return s_cachedOk;
        }
        bool t_ok = Validate(out _error);
        s_checkedAt = t_now;
        s_cachedOk = t_ok;
        s_cachedError = _error;
        return t_ok;
    }

    static bool Validate(out string _error)
    {
        _error = null;
        try
        {
            string t_root = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(t_root)) throw new InvalidOperationException("프로젝트 루트를 찾지 못했다.");

            Manifest t_manifest = JsonUtility.FromJson<Manifest>(
                File.ReadAllText(Path.Combine(t_root, "content-version.json")));
            if (t_manifest == null || t_manifest.major != ContentVersion.Major ||
                t_manifest.minAppMajor != ContentVersion.MinAppMajor ||
                t_manifest.supported == null ||
                t_manifest.supported.Length != ContentVersion.SupportedMajorCount)
                throw new InvalidOperationException("content-version.json과 클라이언트 상수가 다르다.");
            for (int i = 0; i < t_manifest.supported.Length; i++)
                if (t_manifest.supported[i] != ContentVersion.SupportedMajorAt(i))
                    throw new InvalidOperationException("content-version.json의 supported 목록이 클라이언트와 다르다.");
            // 매니페스트 자기 정합. 이 셋이 깨지면 클라가 방금 올린 콘텐츠를 못 읽거나
            // 서버가 자기 콘텐츠를 자기 앱에서 거절한다.
            if (t_manifest.supported.Length == 0)
                throw new InvalidOperationException("content-version.json의 supported 목록이 비어 있다.");
            if (Array.IndexOf(t_manifest.supported, t_manifest.major) < 0)
                throw new InvalidOperationException(
                    $"content-version.json의 supported가 현재 테이블 세대 {t_manifest.major}를 포함하지 않는다.");
            if (t_manifest.minAppMajor > t_manifest.major)
                throw new InvalidOperationException(
                    $"content-version.json의 minAppMajor {t_manifest.minAppMajor}가 테이블 세대 {t_manifest.major}보다 크다.");

            string t_ts = File.ReadAllText(Path.Combine(t_root, "functions/src/specs/specBlobReader.ts"));
            Match t_major = Regex.Match(t_ts,
                @"content-version:major\s*[\r\n]+\s*const CONTENT_MAJOR = (\d+);");
            // 목록이 늘어나도 읽는다 — 단일 리터럴만 매치하면 롤백 지원 빌드에서 앵커 미발견으로 죽는다.
            Match t_supported = Regex.Match(t_ts,
                @"content-version:supported\s*[\r\n]+\s*const SUPPORTED_CONTENT_MAJORS = new Set<number>\(\[([^\]]*)\]\);");
            if (!t_major.Success || !t_supported.Success ||
                !int.TryParse(t_major.Groups[1].Value, out int t_serverMajor) ||
                t_serverMajor != t_manifest.major)
                throw new InvalidOperationException("서버 테이블 세대 선언이 content-version.json과 다르다.");
            if (!TryParseMajorList(t_supported.Groups[1].Value, "CONTENT_MAJOR", t_serverMajor, out int[] t_serverSupported))
                throw new InvalidOperationException("서버 supported 목록을 읽을 수 없다.");
            // 환경별 테이블 전환 중 서버는 구 세대도 처리할 수 있다. 클라이언트가
            // 지원하는 모든 세대를 서버가 포함해야 하며, 역방향 포함은 요구하지 않는다.
            foreach (int t_required in t_manifest.supported)
                if (Array.IndexOf(t_serverSupported, t_required) < 0)
                    throw new InvalidOperationException($"서버 supported 목록에 클라이언트 테이블 세대 {t_required}가 없다.");
            return true;
        }
        catch (Exception t_exception)
        {
            _error = "콘텐츠 버전 대조 실패: " + t_exception.Message;
            return false;
        }
    }

    static bool TryParseMajorList(string _listText, string _aliasName, int _aliasValue, out int[] _values)
    {
        _values = null;
        var t_parsed = new System.Collections.Generic.List<int>();
        foreach (string t_raw in _listText.Split(','))
        {
            string t_token = t_raw.Trim();
            if (t_token.Length == 0) continue;
            if (string.Equals(t_token, _aliasName, StringComparison.Ordinal)) t_parsed.Add(_aliasValue);
            else if (int.TryParse(t_token, NumberStyles.None, CultureInfo.InvariantCulture, out int t_value))
                t_parsed.Add(t_value);
            else return false;
        }
        if (t_parsed.Count == 0) return false;
        _values = t_parsed.ToArray();
        return true;
    }
}
