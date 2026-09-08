using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 덱 편집 화면 하단의 가로 덱 선택 바(DeckEditPanel/DeckStrip에 부착). 맨 앞에 신규 생성 칸을 두고,
// 신규 편집 중이면 그다음에 순번 없는 "생성 중" 칸을, 그 뒤로 저장된 유효 덱을 나열한다.
// 세이브는 읽기만 한다 — 삽입·삭제는 편집기(DeckEditController)가 자기 저장 경로에서만 한다.
//
// 로비 목록(DeckListController)을 재사용하지 않는 이유: 그쪽은 2열 그리드에 삭제 경로와 편집 모드를 함께 들고 있다.
// 이 바는 "지금 어느 덱을 편집 중인가"를 보여주는 것이 본업이고, 삭제 버튼은 신규 편집 중일 때,
// 그리고 만석이라 ⊕ 칸이 삭제 토글로 바뀐 뒤 그것을 눌렀을 때만 열린다(둘 다 이 바가 쥔 표시 축이다).
// 지우는 일 자체는 여기서 하지 않는다 — 세이브는 읽기만 하고, 파괴는 편집기가 넘긴 콜백이 맡는다.
public class DeckStripView : MonoBehaviour
{
    [SerializeField] Transform    content;      // HorizontalLayoutGroup + ContentSizeFitter(Horizontal=PreferredSize)
    [SerializeField] DeckSlotView slotPrefab;   // DeckStripCard.prefab (DeckCard.prefab 배리언트)
    [SerializeField] ScrollRect   scroll;       // 옵션 — 선택 칸으로 스크롤을 맞출 때만 사용

    [Tooltip("스크롤 밖에 고정된 신규 생성 칸. 배선하면 그것을 쓰고, 비우면 목록 맨 앞에 인스턴스로 만든다.\n"
           + "반드시 content 바깥(스크롤 형제)에 두어야 한다 — 안에 두면 목록 재생성이 프리팹의 자식을 지운다.")]
    [SerializeField] DeckSlotView createCell;

    readonly List<DeckSlotView> m_slots = new List<DeckSlotView>();

    // m_slots와 같은 순서로 각 칸의 저장 슬롯 인덱스를 보관한다(신규 생성 칸은 -1).
    // DeckSlotView는 슬롯 인덱스를 외부로 노출하지 않으므로, 선택 이동 시 대조할 좌표를 리스트가 직접 들고 있어야 한다.
    readonly List<int> m_slotIndices = new List<int>();

    // 저장 전인 "생성 중" 칸이 쓰는 좌표 자리. ⊕ 칸의 -1과 반드시 달라야 한다 —
    // 매치 편집 화면은 ⊕를 목록 안에 인스턴스로 만들어 -1로 등록하므로,
    // 같은 값을 나눠 쓰면 선택 표시가 두 칸에 함께 켜진다.
    const int DRAFT_INDEX = -2;

    // 선택 상태는 여기서 들지 않는다 — 진실원은 편집기(DeckEditController)이고,
    // 이 바는 지시받은 좌표를 칸 표시에 반영하기만 한다(같은 상태를 두 곳이 들면 어긋난다).

    // 만석일 때 ⊕ 칸이 여닫는 삭제 버튼 표시 축. 재빌드마다 닫힌 상태로 돌아간다 —
    // 한 장을 지우면 더는 만석이 아니라 ⊕ 칸이 생성 칸으로 되돌아오므로, 상태를 이어 갈 이유가 없다.
    bool m_deleteMode;

    // Build 가 마지막으로 지시받은 편집 중 슬롯 좌표(신규 편집 중이면 -1). 삭제 토글이 켜져도 이 칸만은
    // 삭제 버튼을 열지 않는다 — 편집 중인 덱이 발밑에서 사라지면 편집기의 좌표가 빈 칸을 가리키게 된다.
    int m_editingSlot = -1;

    // 이번 Build 가 세운 ⊕ 칸(저작 칸이든 인스턴스든). 삭제 토글을 켜고 끌 때 문구를 갈아끼우려면 다시 잡아야 한다.
    DeckSlotView m_createView;

    // OnEnable에서 자동 Build 하지 않는다 — "어느 슬롯이 선택됐는지"와 클릭 콜백은 편집기만 아는 정보다.
    // 여기서 임의로 그리면 선택 없는 목록이 한 프레임 떴다가 편집기의 Build로 덮이면서 깜빡인다.

    /// <summary>편집기가 부르는 유일한 진입점. _selectedSlot은 DeckSaveManager 좌표(없으면 -1),
    /// _createSelected는 신규 생성 편집 중인가(생성 중 칸을 세우고 저장된 칸의 삭제 버튼을 여는 축을 겸한다).
    /// _onSlotDelete가 null이면 삭제 버튼은 어떤 칸에서도 뜨지 않는다.
    /// _fullDeleteToggle 이 켜져 있으면 만석일 때 ⊕ 칸이 비활성 대신 삭제 토글이 된다(호스트가 정한다 — DeckEditData.allowFullDeleteToggle).</summary>
    public void Build(int _selectedSlot, bool _createSelected, Action<int> _onSlotClick, Action _onCreateClick, Action<int> _onSlotDelete, bool _fullDeleteToggle = false)
    {
        Clear();

        // 삭제 토글은 재빌드마다 닫힌다 — 지우고 나면 만석이 풀려 ⊕ 칸이 생성 칸으로 돌아오기 때문이다.
        m_deleteMode  = false;
        m_editingSlot = _createSelected ? -1 : _selectedSlot;

        if (content == null || slotPrefab == null) return;

        // 저작 실수를 여기서 끊는다 — 아래 정리 루프가 content의 자식을 전부 파괴하므로,
        // 생성 칸이 그 안에 있으면 두 번째 진입부터 사라진다(증상이 늦게 나와 추적이 어렵다).
        if (createCell != null && createCell.transform.parent == content)
        {
            Debug.LogError($"[DeckStripView] createCell이 content 안에 저작됐다({name}) — 스크롤 바깥으로 옮겨야 한다. 이번에는 인스턴스 생성으로 대체한다.");
            createCell = null;
        }

        // 씬에 남은 목업 하드코딩 칸 제거.
        // Destroy는 프레임 끝에 반영되므로 먼저 SetActive(false)로 꺼야 이번 프레임 가로 배치에 끼지 않는다
        // (LayoutGroup은 비활성 자식을 무시한다).
        for (int t_i = content.childCount - 1; t_i >= 0; t_i--)
        {
            var t_child = content.GetChild(t_i).gameObject;
            t_child.SetActive(false);
            Destroy(t_child);
        }

        // 신규 생성 칸을 먼저 붙인다 — 이 바에서 ⊕는 맨 왼쪽 고정 자리다.
        BuildCreateCell(_onCreateClick, _fullDeleteToggle && _onSlotDelete != null);

        // 저장 전인 신규 덱도 목록에 세운다 — 어느 덱을 만드는 중인지 이 바에서 읽히게 하기 위함이다.
        // 순번은 붙이지 않는다(저장이 확정되는 순간에 정해진다).
        BuildDraftCell(_createSelected, _selectedSlot);

        // 상한은 SLOT_COUNT로 둔다 — DeckCount로 끊으면 불변식이 깨진 세이브(중간 구멍)에서 뒤쪽 덱이 통째로 사라진다.
        // 표시 번호는 유효 슬롯만 세므로 구멍 난 세이브에서도 번호가 연속된다.
        // 생성 중 칸은 이 셈에서 빠진다 — 저장되기 전에는 순번을 갖지 않는다.
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
                _onSlotDelete);

            // 삭제 버튼은 신규 편집 중일 때만 연다. 콜백이 없으면 SetEditMode가 알아서 무시한다.
            t_view.SetEditMode(_createSelected);

            m_slots.Add(t_view);
            m_slotIndices.Add(t_i);
            t_display++;
        }

        SetSelected(_selectedSlot, _createSelected);
    }

    /// <summary>재빌드 없이 하이라이트만 옮긴다 — 재빌드는 스크롤 위치를 잃는다.</summary>
    public void SetSelected(int _slotIndex, bool _createSelected)
    {
        // 신규 편집 중이면 슬롯 좌표는 보지 않는다 — 편집 중 칸이 선택 표시의 유일한 주인이다.
        int t_target = _createSelected ? -1 : _slotIndex;

        // 저작 자식은 m_slots 밖이라 아래 루프가 닿지 않는다.
        // ⊕ 칸은 선택 표시를 갖지 않는다 — 그 자리의 주인이 "편집 중" 칸으로 넘어갔다.
        if (createCell != null) createCell.SetSelected(false);

        int t_hit = -1;
        for (int t_i = 0; t_i < m_slots.Count; t_i++)
        {
            // 편집 중 칸만 모드로 판정한다. ⊕ 칸(-1)은 좌표 대조에서 자연히 빠진다 —
            // 둘 다 음수라 부호 하나로 뭉뚱그리면 두 칸이 함께 켜진다.
            bool t_on = m_slotIndices[t_i] == DRAFT_INDEX ? _createSelected : m_slotIndices[t_i] == t_target;
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

    // 신규 생성 칸. 아직 잠겨 있으면 자리는 지키되 눌리지 않는다(DeckListController.Build와 같은 규칙).
    // 만석이면 생성 대신 삭제 토글이 된다 — 6장이 꽉 찬 채로는 이 화면에서 덱을 줄일 길이 없었기 때문이다
    // (삭제 버튼은 신규 편집 중에만 열리는데, 만석이면 신규 편집에 들어갈 수 없다).
    // _canDelete 가 false 인 호스트(매치 셸 — 출전 좌표를 따로 들어 압축 당김을 못 따라간다)는 예전처럼 "가득 참" 비활성으로 남는다.
    void BuildCreateCell(Action _onCreateClick, bool _canDelete)
    {
        m_createView = null;

        bool t_unlocked  = OutgameFeatureLock.IsUnlocked(EOutgameFeature.DeckCreate);
        bool t_canCreate = !DeckSaveManager.IsFull && t_unlocked;
        bool t_asDelete  =  DeckSaveManager.IsFull && t_unlocked && _canDelete;

        // 저작 자식은 목록에 등록하지 않는다 — Clear()가 m_slots를 통째로 Destroy하므로
        // 넣는 순간 프리팹의 자식이 파괴되고, 풀드 인스턴스라 두 번째 진입부터 칸이 영영 사라진다.
        if (createCell != null)
        {
            createCell.gameObject.SetActive(_onCreateClick != null);
            if (_onCreateClick == null) return;

            BindCreateOrDelete(createCell, t_asDelete, t_canCreate, _onCreateClick);
            FeatureLockView.Attach(createCell.gameObject, EOutgameFeature.DeckCreate);
            return;
        }

        if (_onCreateClick == null) return;   // 신규 생성을 지원하지 않는 호스트

        var t_create = Instantiate(slotPrefab, content);
        BindCreateOrDelete(t_create, t_asDelete, t_canCreate, _onCreateClick);

        // Bind 뒤에 붙인다 — 그 안에서 꺼지는 자식들까지 흑백 대상으로 잡을 이유가 없다.
        FeatureLockView.Attach(t_create.gameObject, EOutgameFeature.DeckCreate);

        m_slots.Add(t_create);
        m_slotIndices.Add(-1);
    }

    void BindCreateOrDelete(DeckSlotView _view, bool _asDelete, bool _canCreate, Action _onCreateClick)
    {
        m_createView = _view;

        if (_asDelete) _view.BindDeleteToggle(m_deleteMode, ToggleDeleteMode);
        else           _view.BindCreate(_canCreate, _onCreateClick);
    }

    // 만석 상태의 ⊕ 칸 클릭. 목록을 재빌드하지 않고 저장된 칸의 삭제 버튼만 여닫는다 —
    // 재빌드는 스크롤 위치를 잃고, 편집기의 선택 좌표도 바뀌지 않았다.
    void ToggleDeleteMode()
    {
        m_deleteMode = !m_deleteMode;

        for (int t_i = 0; t_i < m_slots.Count; t_i++)
        {
            if (m_slots[t_i] == null) continue;

            // 편집 중인 덱은 제외한다. ⊕·생성 중 칸은 삭제 콜백이 없어 SetEditMode 가 알아서 무시한다.
            bool t_open = m_deleteMode && m_slotIndices[t_i] != m_editingSlot;
            m_slots[t_i].SetEditMode(t_open);
        }

        if (m_createView != null) m_createView.BindDeleteToggle(m_deleteMode, ToggleDeleteMode);
    }

    // 저장 전인 신규 덱 칸. 목록에 서 있다는 사실 자체가 "지금 신규 편집 중"이라 클릭 경로를 두지 않는다.
    // ⊕ 칸 바로 뒤에 붙여야 m_slots 순서와 형제 순서가 두 호스트(로비·매치)에서 모두 일치한다.
    void BuildDraftCell(bool _createSelected, int _selectedSlot)
    {
        if (!_createSelected) return;

        // 편집기 불변식(Create 모드면 슬롯 좌표가 없다)이 깨진 채로 들어오면 편집 중 칸과 실제 덱이
        // 동시에 선택돼 보인다. 증상만으로는 원인을 찾기 어려우니 여기서 알린다(표시는 SetSelected가 정리한다).
        if (_selectedSlot >= 0)
            Debug.LogError($"[DeckStripView] 신규 편집 중인데 슬롯 좌표({_selectedSlot})가 함께 왔다 — 편집 중 칸을 우선한다.");

        var t_draft = Instantiate(slotPrefab, content);
        t_draft.BindDraft();

        m_slots.Add(t_draft);
        m_slotIndices.Add(DRAFT_INDEX);
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
