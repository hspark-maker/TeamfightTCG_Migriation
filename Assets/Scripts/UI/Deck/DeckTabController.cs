using UnityEngine;

// 덱 탭 루트(Tab_Deck에 부착). 목록/편집 두 패널의 SetActive 전환만 담당한다(씬 로드 없음).
// 탭 셸(LobbyTabController)이 단순 SetActive 토글이라 라이프사이클 훅이 없으므로,
// "탭이 켜지면 항상 목록부터"를 OnEnable로 보장한다.
public class DeckTabController : MonoBehaviour
{
    [SerializeField] GameObject         listPanel;
    [SerializeField] GameObject         editPanel;
    [SerializeField] DeckEditController editController;   // 옵션 — 미배선이면 패널 토글만 한다

    void OnEnable()
    {
        ShowList();
    }

    // 덱 구성 화면 진입. _slotIndex는 DeckSaveManager 슬롯 좌표(신규 생성도 빈 슬롯 인덱스를 받는다).
    public void OpenEditor(int _slotIndex)
    {
        if (_slotIndex < 0 || _slotIndex >= DeckSaveManager.SLOT_COUNT) return;

        if (listPanel != null) listPanel.SetActive(false);
        if (editPanel != null) editPanel.SetActive(true);
        if (editController != null) editController.Open(_slotIndex);
    }

    // 목록 복귀. listPanel.SetActive(true) → DeckListController.OnEnable → 자동 재빌드가 갱신 경로다.
    public void CloseEditor()
    {
        ShowList();
    }

    void ShowList()
    {
        if (editController != null) editController.Close();
        if (editPanel != null) editPanel.SetActive(false);
        if (listPanel != null) listPanel.SetActive(true);
    }
}
