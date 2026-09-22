using UnityEditor;
using UnityEngine;

/// <summary>칭호 선택 화면을 확인하는 에디터 진입점.</summary>
public static class TitleSystemTestMenu
{
    const string OPEN_MENU = "Tools/칭호/칭호 선택창 열기";

    [MenuItem(OPEN_MENU)]
    public static void OpenTitles()
    {
        if (!IsReady()) return;
        ProfileEditPanel t_panel = UIPoolManager.Instance?.AddOrUpdateUI<ProfileEditPanel>(new UIData());
        if (t_panel != null) t_panel.EditTitles();
    }

    [MenuItem(OPEN_MENU, true)]
    static bool IsReady()
        => EditorApplication.isPlaying && GameInitialization.IsReady && TitleManager.Catalog != null;
}
