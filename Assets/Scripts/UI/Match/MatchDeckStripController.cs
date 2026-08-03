using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 매치 덱 편집 패널 상단 가로 덱 리스트(TopBar/DeckStrip에 부착). 저장된 유효 덱만 나열하고 세이브는 읽기만 한다.
// 로비 DeckListController를 재사용하지 않은 이유 셋: "+"칸 강제 삽입 · 덱 삭제 경로 · static 튜토리얼 앵커 덮어쓰기.
public class MatchDeckStripController : MonoBehaviour
{
    [SerializeField] Transform    content;      // HorizontalLayoutGroup + ContentSizeFitter(Horizontal=PreferredSize)
    [SerializeField] DeckSlotView slotPrefab;   // MatchDeckStripCard.prefab (DeckCard.prefab 배리언트)
    [SerializeField] ScrollRect   scroll;       // 옵션 — 선택 칸으로 스크롤을 맞출 때만 사용

    readonly List<DeckSlotView> m_slots = new List<DeckSlotView>();

    // m_slots와 같은 순서로 각 칸의 저장 슬롯 인덱스를 보관한다.
    // DeckSlotView는 슬롯 인덱스를 외부로 노출하지 않으므로, 선택 이동 시 대조할 좌표를 리스트가 직접 들고 있어야 한다.
    readonly List<int> m_slotIndices = new List<int>();

    // 선택 상태는 여기서 들지 않는다 — 진실원은 셸(MatchDeckShell.SelectedSlot)이고,
    // 리스트는 셸이 지시한 좌표를 칸 표시에 반영하기만 한다(같은 상태를 두 곳이 들면 어긋난다).

    // OnEnable에서 자동 Build 하지 않는다 — "어느 슬롯이 선택됐는지"와 클릭 콜백은 셸(편집 패널)만 아는 정보다.
    // 여기서 임의로 그리면 선택 없는 목록이 한 프레임 떴다가 셸의 Build로 덮이면서 깜빡인다.

    // 셸이 부르는 유일한 진입점. _selectedSlot은 DeckSaveManager 좌표(없으면 -1), _onClick은 편집 대상 전환 콜백.
    // _tutorialSlot은 튜토리얼 안내가 가리킬 덱 좌표(없으면 -1) — 그 칸 하나만 앵커로 등록한다.
    public void Build(int _selectedSlot, Action<int> _onClick, int _tutorialSlot = -1)
    {
        Clear();

        if (content == null || slotPrefab == null) return;

        // 씬에 남은 목업 하드코딩 칸 제거.
        // Destroy는 프레임 끝에 반영되므로 먼저 SetActive(false)로 꺼야 이번 프레임 가로 배치에 끼지 않는다
        // (LayoutGroup은 비활성 자식을 무시한다).
        for (int t_i = content.childCount - 1; t_i >= 0; t_i--)
        {
            var t_child = content.GetChild(t_i).gameObject;
            t_child.SetActive(false);
            Destroy(t_child);
        }

        // 상한은 SLOT_COUNT로 둔다 — DeckCount로 끊으면 불변식이 깨진 세이브(중간 구멍)에서 뒤쪽 덱이 통째로 사라진다.
        // 표시 번호는 유효 슬롯만 세므로 구멍 난 세이브에서도 "01, 02"가 연속된다.
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
                _onClick,
                null);                                  // 삭제 콜백 없음 — null이면 SetEditMode가 항상 무시해서 삭제 버튼이 절대 뜨지 않는다(매치 화면에 파괴적 경로를 두지 않는다)

            // 튜토리얼 덱 칸에만 등록한다. 로비 목록(DeckListController)이 쓰는 DeckCreateSlot(7)과 키가 달라
            // TutorialAnchorRegistry가 static이어도 로비 하이라이트를 빼앗지 않는다 — 나머지 칸은 그대로 등록하지 않는다.
            if (t_i == _tutorialSlot) t_view.RegisterTutorialAnchor(EOutgameTutorialAnchor.MatchDeckTutorialDeck);

            m_slots.Add(t_view);
            m_slotIndices.Add(t_i);
            t_display++;
        }

        SetSelected(_selectedSlot);
    }

    // 재빌드 없이 하이라이트만 옮긴다 — 재빌드는 스크롤 위치를 잃는다.
    public void SetSelected(int _slotIndex)
    {
        int t_hit = -1;
        for (int t_i = 0; t_i < m_slots.Count; t_i++)
        {
            bool t_on = m_slotIndices[t_i] == _slotIndex;
            if (t_on) t_hit = t_i;

            if (m_slots[t_i] != null) m_slots[t_i].SetSelected(t_on);
        }

        ScrollTo(t_hit);
    }

    public void Clear()
    {
        // 앵커는 명시 해제한다 — Destroy가 프레임 끝에 반영되므로 그사이 죽을 칸이 등록된 채 남는다
        // (레지스트리의 fake-null 자가치유에 기대면 그 구간에 게이트가 사라질 칸을 가리킬 수 있다).
        TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.MatchDeckTutorialDeck);

        // Destroy는 프레임 끝에 반영된다 — 먼저 끄지 않으면 셸이 Clear만 부르고 나가는 경로에서 한 프레임 잔상이 남는다.
        for (int t_i = 0; t_i < m_slots.Count; t_i++)
        {
            if (m_slots[t_i] == null) continue;

            m_slots[t_i].gameObject.SetActive(false);
            Destroy(m_slots[t_i].gameObject);
        }

        m_slots.Clear();
        m_slotIndices.Clear();
    }

    // 선택 칸이 화면 밖일 때를 대비한 보조 동작. 칸 폭이 균일하다는 전제로 순번 비율만 쓴다
    // (정확한 픽셀 계산은 이번 프레임 레이아웃이 확정되기 전이라 신뢰할 수 없다).
    // scroll 미배선이면 스크롤 없이도 목록은 정상 동작한다.
    void ScrollTo(int _viewIndex)
    {
        if (scroll == null) return;
        if (_viewIndex < 0 || m_slots.Count <= 1) return;

        scroll.horizontalNormalizedPosition = Mathf.Clamp01((float)_viewIndex / (m_slots.Count - 1));
    }
}
