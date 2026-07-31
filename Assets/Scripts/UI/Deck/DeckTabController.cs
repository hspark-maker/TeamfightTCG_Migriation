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
        // 편집 중 탭이 꺼졌다 켜지면 여기서 무저장 폐기된다.
        // 편집은 DeckEditController의 메모리 사본에서만 일어나고 세이브는 손대지 않으므로
        // 손실은 "이번 편집분"뿐이고 기존 덱은 온전하다 — 그래서 확인 팝업 없이 목록으로 되돌려도 안전하다.
        ShowList();
    }

    // 기존 덱 편집 진입. _slotIndex는 DeckSaveManager 슬롯 좌표.
    public void OpenEditor(int _slotIndex)
    {
        if (_slotIndex < 0 || _slotIndex >= DeckSaveManager.SLOT_COUNT) return;

        ShowEditor();
        if (editController != null) editController.Open(_slotIndex);
    }

    // 신규 덱 진입. 좌표는 저장이 확정되는 순간(TryInsertFront)에 생기므로 여기서는 만석만 막는다.
    public void OpenNewDeckEditor()
    {
        if (DeckSaveManager.IsFull) return;

        ShowEditor();
        if (editController != null) editController.OpenNew();
    }

    // 목록 복귀. listPanel.SetActive(true) → DeckListController.OnEnable → 자동 재빌드가 갱신 경로다.
    public void CloseEditor()
    {
        ShowList();
    }

    void ShowEditor()
    {
        if (listPanel != null) listPanel.SetActive(false);
        if (editPanel != null) editPanel.SetActive(true);
    }

    void ShowList()
    {
        if (editController != null) editController.Close();
        if (editPanel != null) editPanel.SetActive(false);
        if (listPanel != null) listPanel.SetActive(true);
    }
}
