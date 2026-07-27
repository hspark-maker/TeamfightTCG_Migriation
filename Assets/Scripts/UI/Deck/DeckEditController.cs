using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 덱 구성 화면(DeckEditPanel에 부착). 이번 범위는 진입/이탈 껍데기만이다.
// 내용물(카드 편성 UI)은 DeckBuilderUI(MainMenu 전용) 이식 시 채운다.
public class DeckEditController : MonoBehaviour
{
    [SerializeField] TMP_Text          titleText;      // 임시 — 어떤 슬롯이 열렸는지 눈으로 검증용
    [SerializeField] Button            backButton;
    [SerializeField] DeckTabController tabController;

    // 현재 편집 중인 저장 슬롯 인덱스(닫힌 상태는 -1).
    int m_slotIndex = -1;
    public int SlotIndex => m_slotIndex;

    void Awake()
    {
        if (backButton != null) backButton.onClick.AddListener(OnBackClicked);
    }

    // 편집 진입. _slotIndex는 DeckSaveManager 슬롯 좌표(신규 생성도 빈 슬롯 인덱스를 받는다).
    public void Open(int _slotIndex)
    {
        m_slotIndex = _slotIndex;

        if (titleText != null)
            titleText.text = DeckSaveManager.IsSlotValid(_slotIndex)
                ? $"{DeckSaveManager.GetName(_slotIndex)} (슬롯 {_slotIndex})"
                : $"새 덱 (슬롯 {_slotIndex})";
    }

    public void Close()
    {
        m_slotIndex = -1;
    }

    void OnBackClicked()
    {
        if (tabController != null) tabController.CloseEditor();
    }
}
