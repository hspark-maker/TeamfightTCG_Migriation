using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 덱 편집 화면 하단의 가로 덱 선택 바(DeckEditPanel/DeckStrip에 부착). 저장된 유효 덱을 나열하고 맨 뒤에 신규 생성 칸을 붙인다.
// 세이브는 읽기만 한다 — 삽입·삭제는 편집기(DeckEditController)가 자기 저장 경로에서만 한다.
//
// 로비 목록(DeckListController)을 재사용하지 않는 이유: 그쪽은 2열 그리드에 삭제 경로와 편집 모드를 함께 들고 있다.
// 이 바는 "지금 어느 덱을 편집 중인가"만 보여주는 선택 표시 전용이라 파괴적 경로를 두지 않는다.
public class DeckStripView : MonoBehaviour
{
    [SerializeField] Transform    content;      // HorizontalLayoutGroup + ContentSizeFitter(Horizontal=PreferredSize)
    [SerializeField] DeckSlotView slotPrefab;   // MatchDeckStripCard.prefab (DeckCard.prefab 배리언트)
    [SerializeField] ScrollRect   scroll;       // 옵션 — 선택 칸으로 스크롤을 맞출 때만 사용

    readonly List<DeckSlotView> m_slots = new List<DeckSlotView>();

    // m_slots와 같은 순서로 각 칸의 저장 슬롯 인덱스를 보관한다(신규 생성 칸은 -1).
    // DeckSlotView는 슬롯 인덱스를 외부로 노출하지 않으므로, 선택 이동 시 대조할 좌표를 리스트가 직접 들고 있어야 한다.
    readonly List<int> m_slotIndices = new List<int>();

    // 선택 상태는 여기서 들지 않는다 — 진실원은 편집기(DeckEditController)이고,
    // 이 바는 지시받은 좌표를 칸 표시에 반영하기만 한다(같은 상태를 두 곳이 들면 어긋난다).

    // OnEnable에서 자동 Build 하지 않는다 — "어느 슬롯이 선택됐는지"와 클릭 콜백은 편집기만 아는 정보다.
    // 여기서 임의로 그리면 선택 없는 목록이 한 프레임 떴다가 편집기의 Build로 덮이면서 깜빡인다.

    /// <summary>편집기가 부르는 유일한 진입점. _selectedSlot은 DeckSaveManager 좌표(없으면 -1),
    /// _createSelected는 신규 생성 편집 중이라 ⊕ 칸이 선택 표시를 가져야 하는가.</summary>
    public void Build(int _selectedSlot, bool _createSelected, Action<int> _onSlotClick, Action _onCreateClick)
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
                _onSlotClick,
                null);                                  // 삭제 콜백 없음 — null이면 SetEditMode가 항상 무시해서 삭제 버튼이 절대 뜨지 않는다

            m_slots.Add(t_view);
            m_slotIndices.Add(t_i);
            t_display++;
        }

        BuildCreateCell(_onCreateClick);

        SetSelected(_selectedSlot, _createSelected);
    }

    /// <summary>재빌드 없이 하이라이트만 옮긴다 — 재빌드는 스크롤 위치를 잃는다.</summary>
    public void SetSelected(int _slotIndex, bool _createSelected)
    {
        int t_hit = -1;
        for (int t_i = 0; t_i < m_slots.Count; t_i++)
        {
            // 신규 생성 칸(-1)은 좌표가 아니라 모드로 판정한다 — _slotIndex가 -1인 상태는 "선택 없음"도 겸한다.
            bool t_on = m_slotIndices[t_i] < 0 ? _createSelected : m_slotIndices[t_i] == _slotIndex;
            if (t_on) t_hit = t_i;

            if (m_slots[t_i] != null) m_slots[t_i].SetSelected(t_on);
        }

        ScrollTo(t_hit);
    }

    public void Clear()
    {
        // Destroy는 프레임 끝에 반영된다 — 먼저 끄지 않으면 Clear만 부르고 나가는 경로에서 한 프레임 잔상이 남는다.
        for (int t_i = 0; t_i < m_slots.Count; t_i++)
        {
            if (m_slots[t_i] == null) continue;

            m_slots[t_i].gameObject.SetActive(false);
            Destroy(m_slots[t_i].gameObject);
        }

        m_slots.Clear();
        m_slotIndices.Clear();
    }

    // 맨 뒤 신규 생성 칸. 만석이거나 아직 잠겨 있으면 자리는 지키되 눌리지 않는다(DeckListController.Build와 같은 규칙).
    void BuildCreateCell(Action _onCreateClick)
    {
        if (_onCreateClick == null) return;   // 신규 생성을 지원하지 않는 호스트

        var t_create = Instantiate(slotPrefab, content);
        t_create.BindCreate(!DeckSaveManager.IsFull && OutgameFeatureLock.IsUnlocked(EOutgameFeature.DeckCreate), _onCreateClick);

        // BindCreate 뒤에 붙인다 — 그 안에서 꺼지는 자식들까지 흑백 대상으로 잡을 이유가 없다.
        FeatureLockView.Attach(t_create.gameObject, EOutgameFeature.DeckCreate);

        m_slots.Add(t_create);
        m_slotIndices.Add(-1);
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
