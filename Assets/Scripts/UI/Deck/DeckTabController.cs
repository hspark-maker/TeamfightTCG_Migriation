using UnityEngine;

// 덱 탭 루트(Tab_Deck에 부착). 목록/편집 두 패널의 SetActive 전환만 담당한다(씬 로드 없음).
// 탭 셸(LobbyTabController)이 단순 SetActive 토글이라 라이프사이클 훅이 없으므로,
// "탭이 켜지면 항상 목록부터"를 OnEnable로 보장한다.
public class DeckTabController : MonoBehaviour
{
    [SerializeField] GameObject         listPanel;
    [SerializeField] GameObject         editPanel;
    [SerializeField] DeckEditController editController;   // 옵션 — 미배선이면 패널 토글만 한다

    // 편집 중 여부. 탭 셸이나 뒤로가기 처리가 이탈을 막아야 할 때 물어보는 창구.
    public bool IsEditing => editController != null && editController.IsOpen;

    void OnEnable()
    {
        // 편집 중 탭이 꺼졌다 켜지면 여기서 무저장 폐기된다.
        // 편집은 DeckEditController의 메모리 사본에서만 일어나고 세이브는 손대지 않으므로
        // 손실은 "이번 편집분"뿐이고 기존 덱은 온전하다 — 그래서 확인 팝업 없이 목록으로 되돌려도 안전하다.
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
