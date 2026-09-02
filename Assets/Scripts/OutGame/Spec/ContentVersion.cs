using System;

/// <summary>
/// Firebase 테이블의 호환 계약이다. 앱 빌드 버전(bundleVersion)과는 **묶여 있지 않다** —
/// 스토어 표기 버전과 테이블 세대는 서로 다른 속도로 움직이므로 각자 관리한다.
/// major는 테이블 세대, minor는 공개마다 자동 증가하는 시리얼이다.
/// </summary>
public static class ContentVersion
{
    /// <summary>테이블 세대. 컬럼 계약이 바뀌거나 앱이 못 읽는 내용이 들어갈 때 올린다.</summary>
    // content-version:major
    public const int Major = 4;

    /// <summary>새 테이블을 해석하는 데 필요한 최소 테이블 세대.</summary>
    // content-version:min-app-major
    public const int MinAppMajor = 4;

    // 직전 테이블 세대 롤백을 지원하는 빌드는 실제로 해석 가능한 세대를 함께 둔다.
    // content-version:supported
    static readonly int[] SupportedMajors = { Major };

    public static bool IsSupportedMajor(int _major)
        => Array.IndexOf(SupportedMajors, _major) >= 0;

    public static int SupportedMajorCount => SupportedMajors.Length;
    public static int SupportedMajorAt(int _index) => SupportedMajors[_index];

    public static string Format(int _major, long _minor)
        => _minor >= 0 ? $"{_major}.{_minor}" : $"{_major}.legacy";
}
