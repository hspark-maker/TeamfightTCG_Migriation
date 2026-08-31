using TMPro;
using UnityEngine;

/// <summary>
/// 코드 빌드 튜토리얼 UI 공용 스타일. 폰트는 Addressables의 MalgunGothic_TMP 단일 진실원.
/// (튜토리얼 UI는 프리팹 없이 코드로 텍스트를 만들어 인스펙터로 폰트를 못 꽂으므로 여기서 일괄 적용.)
/// </summary>
public static class TutorialUIStyle
{
    const string FontAddress = "MalgunGothic_TMP";
    static TMP_FontAsset s_font;

    static TMP_FontAsset Font =>
        s_font != null ? s_font : (s_font = SyncAddressable.Load<TMP_FontAsset>(FontAddress));

    /// <summary>튜토리얼 텍스트에 공용 폰트 적용. 로드 실패 시 TMP 기본 폰트 유지.</summary>
    public static void ApplyFont(TMP_Text _text)
    {
        if (_text == null) return;
        var t_font = Font;
        if (t_font != null) _text.font = t_font;
    }
}
