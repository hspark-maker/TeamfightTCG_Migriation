using System;
using UnityEngine;

/// <summary>스토어 표기 버전(<c>PlayerSettings.bundleVersion</c> → <c>Application.version</c>) 하나를 담는 값.
///
/// <para>비교는 반드시 이 타입을 거친다 — 문자열 비교로 재면 <c>"0.10.0" &lt; "0.9.0"</c> 이 참이라
/// 업데이트 안내가 정확히 거꾸로 뜬다.</para>
///
/// <para>Android <c>versionCode</c>·iOS build number 는 여기 들어오지 않는다. 판정 축을 스토어 표기와
/// 어긋나게 두면 "업데이트하라"는 안내를 받은 유저가 스토어에서 같은 숫자를 보고 되돌아온다.</para>
///
/// <para><c>ContentVersion</c>(표 세대)과도 무관하다. 둘은 서로 다른 속도로 움직이는 별개의 축이다.</para></summary>
public readonly struct AppSemVer : IComparable<AppSemVer>, IEquatable<AppSemVer>
{
    /// <summary>받아들이는 최대 마디 수. "1.2.3.4"(일부 빌드 파이프라인 표기)까지는 읽되 앞 세 마디만 본다.</summary>
    const int MaxSegments = 4;

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    public AppSemVer(int _major, int _minor, int _patch)
    {
        Major = _major;
        Minor = _minor;
        Patch = _patch;
    }

    /// <summary>이 빌드의 버전. 파싱에 실패하면 false — 호출부는 그때 <b>막지 말고 통과</b>시켜야 한다.
    /// 버전 문자열 오저작 하나로 전 유저가 게임을 못 켜는 쪽이 훨씬 큰 사고다.</summary>
    public static bool TryGetCurrent(out AppSemVer _version) => TryParse(Application.version, out _version);

    /// <summary>"1.2.3" / "1.2" / "1" / "1.2.3-beta" / "1.2.3+build" 를 읽는다.
    /// 빠진 마디는 0으로 채우고, '-' 또는 '+' 뒤 꼬리표는 버린다(같은 숫자면 같은 버전으로 본다).</summary>
    public static bool TryParse(string _text, out AppSemVer _version)
    {
        _version = default;
        if (string.IsNullOrWhiteSpace(_text)) return false;

        string t_core = _text.Trim();

        // 사전배포 꼬리표는 순서를 만들지 않는다 — "1.2.3-beta"와 "1.2.3"의 우열을 정하기 시작하면
        // 서버 저작자가 그 규칙까지 알아야 한다. 숫자 세 마디만 비교 축으로 남긴다.
        int t_suffix = t_core.IndexOfAny(new[] { '-', '+' });
        if (t_suffix >= 0) t_core = t_core.Substring(0, t_suffix);
        if (t_core.Length == 0) return false;

        string[] t_parts = t_core.Split('.');
        if (t_parts.Length > MaxSegments) return false;

        var t_numbers = new int[3];
        for (int i = 0; i < t_parts.Length; i++)
        {
            if (!TryParseSegment(t_parts[i], out int t_value)) return false;
            if (i < t_numbers.Length) t_numbers[i] = t_value;
        }

        _version = new AppSemVer(t_numbers[0], t_numbers[1], t_numbers[2]);
        return true;
    }

    // int.TryParse 를 그냥 쓰지 않는 이유: 그쪽은 "+3"·" 3"·"-3" 을 받아 준다.
    // 버전 마디에 부호가 섞이면 비교가 조용히 뒤집히므로 숫자만 받는다.
    static bool TryParseSegment(string _segment, out int _value)
    {
        _value = 0;
        if (string.IsNullOrEmpty(_segment) || _segment.Length > 9) return false;

        int t_result = 0;
        for (int i = 0; i < _segment.Length; i++)
        {
            char t_char = _segment[i];
            if (t_char < '0' || t_char > '9') return false;
            t_result = t_result * 10 + (t_char - '0');
        }

        _value = t_result;
        return true;
    }

    public int CompareTo(AppSemVer _other)
    {
        if (Major != _other.Major) return Major.CompareTo(_other.Major);
        if (Minor != _other.Minor) return Minor.CompareTo(_other.Minor);
        return Patch.CompareTo(_other.Patch);
    }

    public bool Equals(AppSemVer _other) => CompareTo(_other) == 0;
    public override bool Equals(object _obj) => _obj is AppSemVer t_other && Equals(t_other);
    public override int GetHashCode() => (Major * 397 ^ Minor) * 397 ^ Patch;

    public static bool operator <(AppSemVer _left, AppSemVer _right) => _left.CompareTo(_right) < 0;
    public static bool operator >(AppSemVer _left, AppSemVer _right) => _left.CompareTo(_right) > 0;
    public static bool operator <=(AppSemVer _left, AppSemVer _right) => _left.CompareTo(_right) <= 0;
    public static bool operator >=(AppSemVer _left, AppSemVer _right) => _left.CompareTo(_right) >= 0;
    public static bool operator ==(AppSemVer _left, AppSemVer _right) => _left.Equals(_right);
    public static bool operator !=(AppSemVer _left, AppSemVer _right) => !_left.Equals(_right);

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
