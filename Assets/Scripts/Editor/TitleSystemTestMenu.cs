using UnityEditor;
using UnityEngine;

/// <summary>해금 조건 없이 칭호의 보유·잠금·장착을 확인하는 에디터 진입점.</summary>
public static class TitleSystemTestMenu
{
    const string GRANT_MENU = "Tools/칭호/테스트 칭호 3종 지급";
    const string OPEN_MENU = "Tools/칭호/칭호 선택창 열기";

    [MenuItem(GRANT_MENU)]
    public static void GrantTestTitles()
    {
        if (!IsReady()) return;
        ProfileManager.GrantTitle("test_first_step");
        ProfileManager.GrantTitle("test_blue_traveler");
        ProfileManager.GrantTitle("test_golden_collector");
        Debug.Log("[Title] 테스트 칭호 3종 지급. 나머지 5종은 잠금 미리보기용이며 장착은 바꾸지 않습니다.");
    }

    [MenuItem(OPEN_MENU)]
    public static void OpenTitles()
    {
        if (!IsReady()) return;
        ProfileEditPanel t_panel = UIPoolManager.Instance?.AddOrUpdateUI<ProfileEditPanel>(new UIData());
        if (t_panel != null) t_panel.EditTitles();
    }

    [MenuItem(GRANT_MENU, true)]
    [MenuItem(OPEN_MENU, true)]
    static bool IsReady()
        => EditorApplication.isPlaying && GameInitialization.IsReady && ProfileManager.TitleCatalog != null;
}
