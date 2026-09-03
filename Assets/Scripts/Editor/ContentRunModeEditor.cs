using UnityEditor;
using UnityEngine;

/// <summary>에디터와 빌드가 사용할 콘텐츠 프로필을 선택한다. 카드 값은 SpecData가 직접 소유한다.</summary>
public static class ContentRunModeEditor
{
    public static EContentRunMode Current
    {
        get => (EContentRunMode)EditorPrefs.GetInt(
            ContentProfileConfig.EditorRunModeKey,
            (int)EContentRunMode.Test);
        private set => EditorPrefs.SetInt(ContentProfileConfig.EditorRunModeKey, (int)value);
    }

    public static EContentRunMode Other(EContentRunMode _mode)
        => _mode == EContentRunMode.Live ? EContentRunMode.Test : EContentRunMode.Live;

    public static string Label(EContentRunMode _mode)
        => _mode == EContentRunMode.Live ? "라이브" : "테스트";

    /// <summary>카드 표는 모드와 무관하게 하나다(Card_Test 표는 폐기).</summary>
    public static string SheetNameOf(EContentRunMode _mode) => "Card";

    public static ContentProfileConfig ProfileOf(EContentRunMode _mode)
        => Resources.Load<ContentProfileConfig>(
            _mode == EContentRunMode.Live ? "ContentProfiles/Live" : "ContentProfiles/Test");

    public static string SwitchTo(EContentRunMode _mode, out string _error)
    {
        _error = null;
        Current = _mode;
        return $"{Label(_mode)} 프로필로 전환했습니다. 런타임 카드 값은 {SheetNameOf(_mode)} SpecData를 직접 읽습니다.";
    }
}
