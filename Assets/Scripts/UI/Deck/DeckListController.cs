using System.Collections.Generic;
using UnityEngine;
using TMPro;

// 덱 목록 패널(DeckListPanel에 부착).
// DeckSaveManager 6슬롯을 읽어 유효 덱만 칸으로 만들고, 0행 0열에 "신규 생성" 칸을 고정한다.
// DeckSaveManager에 변경 통지 이벤트가 없으므로 "패널이 켜질 때 재빌드"가 유일한 갱신 경로다.
// 덱 세이브 접근은 이 클래스에만 가둔다(향후 decks.json → UserSaveData.deck 통합 시 손댈 파일 1개).
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
        int t_empty = FindFirstEmptySlot();
        var t_create = Instantiate(slotPrefab, content);
        t_create.BindCreate(t_empty, OnSlotClicked);
        m_slots.Add(t_create);

        // 2) 유효 덱 칸 — 슬롯 순서대로 훑되 번호는 표시 순번(1-base)으로 다시 매긴다.
        //    슬롯 인덱스를 그대로 쓰면 슬롯 1·4만 유효할 때 "02, 05"로 구멍 난 것처럼 보인다.
        int t_display = 1;
        for (int t_i = 0; t_i < DeckSaveManager.SLOT_COUNT; t_i++)
        {
            if (!DeckSaveManager.IsSlotValid(t_i)) continue;

            var t_view = Instantiate(slotPrefab, content);
            t_view.BindDeck(
                t_i,                                    // 슬롯 인덱스 = 클릭 시 전달값
                t_display,                              // 표시 번호 = 화면 순번
                DeckSaveManager.GetName(t_i),
                ResolvePreview(DeckSaveManager.GetSlot(t_i)),
                OnSlotClicked);
            m_slots.Add(t_view);
            t_display++;
        }

        if (countText != null) countText.text = $"{t_display - 1} / {DeckSaveManager.SLOT_COUNT}";
    }

    // 덱 첫 카드의 deckPreview → 없으면 fullImage → 둘 다 없으면 null.
    // null이면 DeckSlotView가 sprite 대입을 건너뛰어 프리팹 기본 스프라이트가 남는다.
    static Sprite ResolvePreview(List<CardData> _deck)
    {
        if (_deck == null || _deck.Count == 0) return null;

        var t_first = _deck[0];
        if (t_first == null) return null;

        return t_first.deckPreview != null ? t_first.deckPreview : t_first.fullImage;
    }

    // 신규 덱이 쓸 슬롯. IsSlotValid 기준이라 6장 미만으로 저장된 불완전 슬롯도 재사용 대상이 된다
    // (그렇지 않으면 목록에 보이지도 않으면서 슬롯만 영구 점유하는 유령 슬롯이 된다).
    static int FindFirstEmptySlot()
    {
        for (int t_i = 0; t_i < DeckSaveManager.SLOT_COUNT; t_i++)
            if (!DeckSaveManager.IsSlotValid(t_i)) return t_i;

        return -1;
    }

    void OnSlotClicked(int _slotIndex)
    {
        if (tabController != null) tabController.OpenEditor(_slotIndex);
    }

    void ClearSlots()
    {
        for (int t_i = 0; t_i < m_slots.Count; t_i++)
            if (m_slots[t_i] != null) Destroy(m_slots[t_i].gameObject);

        m_slots.Clear();
    }
}
