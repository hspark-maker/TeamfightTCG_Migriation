using UnityEngine;
using UnityEngine.UI;

// 컬렉션 탭 루트(Tab_Collection에 부착). 그리드 뷰 / 테마 아코디언 뷰의 SetActive 전환만 담당한다(씬 로드 없음).
public class CollectionTabController : MonoBehaviour
{
    [SerializeField] Toggle     viewToggle;    // on = 테마 뷰
    [SerializeField] GameObject gridPanel;     // Panel_Grid
    [SerializeField] GameObject themePanel;    // Panel_Themes

    // 뷰 모드를 저장하지 않는다 — Tab_Collection은 LobbyCanvas 안 중첩 인스턴스라 탭을 오가도 파괴되지 않고
    // Toggle.isOn이 그대로 남는다. 그 상태를 OnEnable이 다시 반영하는 것만으로 왕복 유지가 성립한다.
    public void SetThemeView(bool _on)
    {
        if (gridPanel  != null) gridPanel.SetActive(!_on);
        if (themePanel != null) themePanel.SetActive(_on);
    }

    // 인스펙터 UnityEvent 배선을 쓰지 않는다 — 끊겨도 컴파일이 통과해 조용히 죽는다(RankRewardPanel 관례).
    // Remove 후 Add라 재활성마다 중복 등록도 남지 않는다.
    void OnEnable()
    {
        // 토글 미배선이면 두 패널이 저작 상태 그대로 남아 겹쳐 보인다 → 기본인 그리드 뷰로 수렴시킨다.
        if (viewToggle == null)
        {
            SetThemeView(false);
            return;
        }

        viewToggle.onValueChanged.RemoveAllListeners();
        viewToggle.onValueChanged.AddListener(SetThemeView);

        SetThemeView(viewToggle.isOn);
    }
}
