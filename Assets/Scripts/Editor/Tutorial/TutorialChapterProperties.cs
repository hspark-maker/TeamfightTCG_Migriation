using UnityEditor;

/// <summary>FTUE·가이드의 연속 챕터 좌표를 각 저작 목록에 연결한다.</summary>
public static class TutorialChapterProperties
{
    /// <summary>FTUE 다음 가이드 순서의 챕터 직렬화 프로퍼티를 반환한다.</summary>
    public static SerializedProperty GetChapter(SerializedObject _serialized, int _globalIndex)
    {
        if (_serialized == null || _globalIndex < 0) return null;
        var t_ftue = _serialized.FindProperty("ftueChapters");
        var t_guides = _serialized.FindProperty("guide.guideChapters");
        if (t_ftue == null || t_guides == null) return null;
        if (_globalIndex < t_ftue.arraySize) return t_ftue.GetArrayElementAtIndex(_globalIndex);
        int t_guideIndex = _globalIndex - t_ftue.arraySize;
        return t_guideIndex < t_guides.arraySize ? t_guides.GetArrayElementAtIndex(t_guideIndex) : null;
    }
}
