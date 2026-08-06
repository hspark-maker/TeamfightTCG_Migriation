using UnityEditor;
using UnityEngine;

public static class ContentProfileMenu
{
    const string LIVE_MENU = "Tools/Card Battle/Content Profile/Live";
    const string TEST_MENU = "Tools/Card Battle/Content Profile/Test";

    [MenuItem(LIVE_MENU)]
    static void SelectLive() => Select(EContentRunMode.Live);

    [MenuItem(TEST_MENU)]
    static void SelectTest() => Select(EContentRunMode.Test);

    [MenuItem(LIVE_MENU, true)]
    static bool ValidateLive() => Validate(EContentRunMode.Live);

    [MenuItem(TEST_MENU, true)]
    static bool ValidateTest() => Validate(EContentRunMode.Test);

    // 모드만 바꾸면 카드 수치가 전 모드 표인 채로 남는다 — 창과 같은 경로(SwitchTo)로 표까지 싣는다.
    static void Select(EContentRunMode _mode)
    {
        string t_report = ContentRunModeEditor.SwitchTo(_mode, out string t_error);
        if (t_error != null) EditorUtility.DisplayDialog("전환 불가", t_error, "확인");
        else                 Debug.Log($"[ContentProfile] {t_report}");

        Validate(_mode);
    }

    static bool Validate(EContentRunMode _mode)
    {
        EContentRunMode t_current = ContentRunModeEditor.Current;
        Menu.SetChecked(LIVE_MENU, t_current == EContentRunMode.Live);
        Menu.SetChecked(TEST_MENU, t_current == EContentRunMode.Test);
        return true;
    }
}
