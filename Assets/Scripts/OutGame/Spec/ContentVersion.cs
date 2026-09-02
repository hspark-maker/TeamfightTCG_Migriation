using System;

/// <summary>
/// Firebase로 배포하는 콘텐츠 데이터의 호환 계약이다.
/// major 변경은 앱 빌드가 필요하고, minor 변경은 같은 계약 안의 값 변경이다.
/// </summary>
public static class ContentVersion
{
    public const int Major = 4;

    // 직전 major 롤백을 지원하는 빌드는 여기에 실제로 해석 가능한 major를 함께 둔다.
    static readonly int[] SupportedMajors = { Major };

    public static bool IsSupportedMajor(int _major)
        => Array.IndexOf(SupportedMajors, _major) >= 0;

    public static string Format(int _major, long _minor)
        => _minor >= 0 ? $"{_major}.{_minor}" : $"{_major}.legacy";
}
