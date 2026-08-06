using UnityEditor;

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

    static void Select(EContentRunMode _mode)
    {
        EditorPrefs.SetInt(ContentProfileConfig.EditorRunModeKey, (int)_mode);
        Validate(_mode);
    }

    static bool Validate(EContentRunMode _mode)
    {
        EContentRunMode t_current = (EContentRunMode)EditorPrefs.GetInt(
            ContentProfileConfig.EditorRunModeKey, (int)EContentRunMode.Test);
        Menu.SetChecked(LIVE_MENU, t_current == EContentRunMode.Live);
        Menu.SetChecked(TEST_MENU, t_current == EContentRunMode.Test);
        return true;
    }
}
