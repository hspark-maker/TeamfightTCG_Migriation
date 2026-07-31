using System.Collections.Generic;
using UnityEngine;
using TMPro;

// 덱 목록 패널(DeckListPanel에 부착).
// DeckSaveManager 6슬롯을 읽어 유효 덱만 칸으로 만들고, 0행 0열에 "신규 생성" 칸을 고정한다.
// DeckSaveManager에 변경 통지 이벤트가 없으므로 "패널이 켜질 때 재빌드"가 유일한 갱신 경로다.
// 덱 세이브 접근은 이 클래스에만 가둔다(덱 목록 화면에서 세이브를 만지는 파일 1개).
public class DeckListController : MonoBehaviour
{
    [SerializeField] Transform         content;        // GridLayoutGroup(2열) + ContentSizeFitter
    [SerializeField] DeckSlotView      slotPrefab;     // DeckCard.prefab
    [SerializeField] DeckTabController tabController;  // 편집 패널 진입점
    [SerializeField] TMP_Text          countText;      // 옵션 헤더 "3 / 6"

    readonly List<DeckSlotView> m_slots = new List<DeckSlotView>();

    void OnEnable()
    {
        Build();
    }

    // 외부에서 강제 갱신할 때의 공개 창구(목록이 켜진 채 덱이 바뀌는 경로가 생기면 사용).
    public void Refresh()
    {
        Build();
    }

    void Build()
    {
        ClearSlots();
        if (content == null || slotPrefab == null) return;

        // 씬에 남은 목업 하드코딩 칸 제거.
        // Destroy는 프레임 끝에 반영되므로 먼저 SetActive(false)로 꺼야 이번 프레임 그리드 배치에 끼지 않는다
        // (LayoutGroup은 비활성 자식을 무시한다). 0행 0열 고정이 요구사항이라 이 가드가 필수다.
        for (int t_i = content.childCount - 1; t_i >= 0; t_i--)
        {
            var t_child = content.GetChild(t_i).gameObject;
            t_child.SetActive(false);
            Destroy(t_child);
        }

        // 1) 신규 생성 칸 — 가장 먼저 Instantiate = 첫 자식 = GridLayoutGroup 0행 0열 고정.
        //    (Start Corner=Upper Left, Start Axis=Horizontal이라 자식 순서가 곧 배치 순서다)
        //    큐 구조라 삽입 좌표는 저장이 확정될 때 생긴다 → 여기서는 만석 여부만 넘긴다.
        //    결과적으로 +칸 바로 다음이 가장 최근에 만든 덱이다.
        var t_create = Instantiate(slotPrefab, content);
        t_create.BindCreate(!DeckSaveManager.IsFull, OnCreateClicked);
        // 재빌드마다 새 인스턴스가 같은 키를 덮어쓰고, 파괴된 옛 항목은 TutorialAnchorRegistry.TryGet의 fake-null 정리가 걷어낸다 → Unregister 불필요.
        t_create.RegisterTutorialAnchor(EOutgameTutorialAnchor.DeckCreateSlot);
        m_slots.Add(t_create);

        // 2) 유효 덱 칸 — 압축 불변식상 [0..DeckCount-1]이 연속 점유지만, 상한은 SLOT_COUNT로 둔다.
        //    DeckCount로 끊으면 불변식이 깨진 세이브(중간 구멍)에서 뒤쪽 덱이 화면에서 통째로 사라진다.
        //    IsSlotValid 가드와 표시 번호 재매핑도 그때만 발동한다("02, 05"처럼 구멍 난 번호 방지).
        int t_display = 1;
        for (int t_i = 0; t_i < DeckSaveManager.SLOT_COUNT; t_i++)
        {
            if (!DeckSaveManager.IsSlotValid(t_i)) continue;

            var t_view = Instantiate(slotPrefab, content);
            t_view.BindDeck(
                t_i,                                    // 슬롯 인덱스 = 클릭 시 전달값
                t_display,                              // 표시 번호 = 화면 순번
                DeckSaveManager.GetDisplayName(t_i),
                DeckImages.ResolveForSlot(t_i),
                OnSlotClicked);
            m_slots.Add(t_view);
            t_display++;
        }

        // 개수는 DeckCount가 아니라 실제로 그린 칸 수로 센다 — 불변식이 깨져도 화면과 숫자가 어긋나지 않게.
        if (countText != null) countText.text = $"{t_display - 1} / {DeckSaveManager.SLOT_COUNT}";
    }

    void OnSlotClicked(int _slotIndex)
    {
        if (tabController != null) tabController.OpenEditor(_slotIndex);
    }

    void OnCreateClicked()
    {
        if (tabController != null) tabController.OpenNewDeckEditor();
    }

    void ClearSlots()
    {
        for (int t_i = 0; t_i < m_slots.Count; t_i++)
            if (m_slots[t_i] != null) Destroy(m_slots[t_i].gameObject);

        m_slots.Clear();
    }
}
