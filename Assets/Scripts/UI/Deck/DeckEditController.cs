using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// 덱 구성 화면(DeckEditPanel에 부착). 편성 상태의 진실원이자 저장 진입점.
//
// 편집은 전부 m_working(6칸, null 허용) 위에서만 일어나고 DeckSaveManager에는 손대지 않는다.
// "취소하면 원상복구"를 별도 스냅샷 없이 성립시키기 위한 구조다 — 세이브를 편집 중에 건드리는 순간
// 취소 경로에서 복원할 원본이 사라진다.
public class DeckEditController : MonoBehaviour
{
    [SerializeField] TMP_InputField    nameInput;      // 덱 이름 입력/표시
    [SerializeField] Button            backButton;
    [SerializeField] DeckTabController tabController;

    [Header("편성 UI")]
    [SerializeField] DeckEditSlotView[]     slots;          // 크기 6
    [SerializeField] DeckEditCollectionGrid collectionGrid;
    [SerializeField] DeckEditDragController dragController;
    [SerializeField] TMP_Text               countText;

    [Header("버튼")]
    [SerializeField] Button unequipAllButton;
    [SerializeField] Button autoEquipButton;   // 이번 범위 미구현 → 항상 비활성

    // 목록 칸(DeckSlotView의 이름 표시)이 짧다 — 프리팹 설정 누락에 기대지 않고 코드에서 상한을 박는다.
    const int NAME_MAX_LENGTH = 12;

    // 편집 중인 덱 사본. 길이는 항상 DECK_SIZE 고정이고 빈 칸은 null이다(리스트로 두면 "3번 칸이 비었다"를 표현할 수 없다).
    readonly CardData[] m_working = new CardData[DeckSaveManager.DECK_SIZE];

    // 현재 편집 중인 저장 슬롯 인덱스(닫힌 상태는 -1).
    int  m_slotIndex = -1;
    bool m_dirty;

    // 편집 진입 시점의 이름. 이름 변경 여부 판정 기준이자 빈 입력의 복구값이다.
    string m_savedName;

    public int  SlotIndex => m_slotIndex;
    public bool IsOpen    => m_slotIndex >= 0;

    // 드래그 컨트롤러가 드롭 대상 판정에 쓰는 칸 목록. 미배선(null)이어도 호출측이 터지지 않게 빈 목록을 준다.
    public IReadOnlyList<DeckEditSlotView> Slots => slots ?? Array.Empty<DeckEditSlotView>();

    void Awake()
    {
        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(OnBackClicked);
        }

        if (unequipAllButton != null)
        {
            unequipAllButton.onClick.RemoveAllListeners();
            unequipAllButton.onClick.AddListener(ClearAll);
        }

        if (nameInput != null)
        {
            nameInput.characterLimit = NAME_MAX_LENGTH;
            nameInput.onEndEdit.RemoveAllListeners();
            nameInput.onEndEdit.AddListener(OnNameEndEdit);
        }

        // 자동 편성은 이번 범위 밖 — 버튼을 지우면 씬 배선이 깨지므로 자리는 두고 비활성으로 고정한다.
        if (autoEquipButton != null) autoEquipButton.interactable = false;
    }

    // 편집 진입. _slotIndex는 DeckSaveManager 슬롯 좌표(신규 생성도 빈 슬롯 인덱스를 받는다).
    public void Open(int _slotIndex)
    {
        // DeckSaveManager는 슬롯 배열을 직접 인덱싱한다 — 만석일 때 FindFirstEmptySlot이 주는 -1이 새면 예외가 난다.
        if (_slotIndex < 0 || _slotIndex >= DeckSaveManager.SLOT_COUNT)
        {
            Debug.LogError($"[DeckEditController] 잘못된 슬롯 인덱스 {_slotIndex} — 편집을 열지 않는다.");
            return;
        }

        m_slotIndex = _slotIndex;

        // 세이브의 List<CardData>는 유효 슬롯이면 6개지만 불완전 슬롯이면 더 짧을 수 있다 → 앞에서부터 채운다.
        Array.Clear(m_working, 0, m_working.Length);
        var t_saved = DeckSaveManager.Load(_slotIndex);
        if (t_saved != null)
        {
            int t_count = Mathf.Min(t_saved.Count, m_working.Length);
            for (int t_i = 0; t_i < t_count; t_i++)
                m_working[t_i] = t_saved[t_i];
        }

        m_dirty = false;   // 로드 직후 = 디스크와 동일 → 그냥 나가면 파일 쓰기 없음

        // GetName은 이름이 비어 있으면 "덱 N" 폴백을 준다 → 신규 덱도 그대로 기본 이름이 된다.
        m_savedName = DeckSaveManager.GetName(_slotIndex);
        if (nameInput != null) nameInput.SetTextWithoutNotify(m_savedName);   // 세팅이 onEndEdit로 되튀지 않게

        if (collectionGrid != null) collectionGrid.Build(OnTileDragRequest);
        if (dragController != null) dragController.Setup(() => Slots, AssignSlot);

        RefreshAll();
    }

    public void Close()
    {
        m_slotIndex = -1;
        m_dirty     = false;
        m_savedName = null;
        Array.Clear(m_working, 0, m_working.Length);

        if (dragController != null) dragController.Cancel();
        if (collectionGrid != null) collectionGrid.Clear();
        if (nameInput      != null) nameInput.DeactivateInputField();   // 소프트키보드가 패널 밖까지 살아남지 않게
    }

    void OnEnable()
    {
        OwnershipManager.OnOwnershipChanged += OnOwnershipChanged;
    }

    // 편집 중 소유가 바뀌면(디버그 전체 해금 등) 컬렉션을 다시 그린다.
    // 그리드는 스스로 Build 하지 않는다 — "장착중 딤"에 필요한 편성 상태를 아는 쪽이 여기뿐이라 재빌드도 여기서 건다.
    void OnOwnershipChanged()
    {
        if (!IsOpen || collectionGrid == null) return;

        // 드래그 중이어도 안전하다 — 드래그는 타일이 아니라 CardData를 들고 있다(DeckEditDragController.Begin).
        collectionGrid.Build(OnTileDragRequest);
        RefreshAll();
    }

    // 패널이 어떤 경로로 꺼지든(탭 전환·씬 전환·부모 비활성) 드래그 고스트가 남지 않게 하는 최종 방어선.
    // Close()는 DeckTabController를 거치는 경로에서만 불린다.
    // 편집 상태(m_slotIndex)도 같이 내려야 한다 — 안 그러면 패널이 꺼졌는데 IsOpen이 true로 남아
    // DeckTabController.IsEditing이 거짓을 보고한다.
    void OnDisable()
    {
        OwnershipManager.OnOwnershipChanged -= OnOwnershipChanged;

        m_slotIndex = -1;
        m_dirty     = false;
        m_savedName = null;
        Array.Clear(m_working, 0, m_working.Length);

        if (dragController != null) dragController.Cancel();
        if (nameInput      != null) nameInput.DeactivateInputField();
    }

    // 이름 입력 확정. 여기서는 표시만 정리하고 dirty를 세우지 않는다 —
    // 저장 여부는 나갈 때 실제 입력값과 m_savedName을 비교해 판정한다(발화 순서에 기대지 않는다).
    void OnNameEndEdit(string _value)
    {
        // OnDisable로 편집이 내려간 뒤 포커스 해제로 늦게 불릴 수 있다.
        if (!IsOpen || nameInput == null) return;

        string t_name = (_value ?? string.Empty).Trim();

        // 빈 이름은 저장하지 않는다 — 편집 진입 시점 이름으로 되돌린다.
        nameInput.SetTextWithoutNotify(string.IsNullOrEmpty(t_name) ? m_savedName : t_name);
    }

    // 지금 화면에 입력된 이름(트림). 비어 있으면 진입 시점 이름을 그대로 쓴다.
    string ResolveName()
    {
        string t_name = nameInput != null ? (nameInput.text ?? string.Empty).Trim() : string.Empty;

        return string.IsNullOrEmpty(t_name) ? m_savedName : t_name;
    }

    // 컬렉션 칸에서 드래그가 시작될 때. 스크롤뷰 소유권을 넘겨줘야 드래그와 스크롤이 서로를 잡아먹지 않는다.
    void OnTileDragRequest(DeckEditCardTile _tile, PointerEventData _data)
    {
        if (_tile == null || dragController == null) return;

        dragController.Begin(_tile.Card, _data, collectionGrid != null ? collectionGrid.Scroll : null);
    }

    // 편성 칸에 카드를 놓는다. 같은 카드가 이미 다른 칸에 있으면 복사가 아니라 이동이다(덱 내 중복 금지).
    public void AssignSlot(int _slotIndex, CardData _card)
    {
        if (_slotIndex < 0 || _slotIndex >= m_working.Length) return;
        if (_card == null) return;

        // 제자리 드롭. 아래 이동 처리보다 먼저 걸러야 한다 — 뒤에 두면 원래 칸을 비우고 나가버린다.
        // 겸사겸사 dirty 오염(변화 없는데 저장 유발)도 막는다.
        if (m_working[_slotIndex] == _card) return;

        for (int t_i = 0; t_i < m_working.Length; t_i++)
            if (t_i != _slotIndex && m_working[t_i] == _card) m_working[t_i] = null;

        m_working[_slotIndex] = _card;
        m_dirty = true;
        RefreshAll();
    }

    // 편성 칸 클릭 = 해제.
    public void ClearSlot(int _slotIndex)
    {
        if (_slotIndex < 0 || _slotIndex >= m_working.Length) return;
        if (m_working[_slotIndex] == null) return;   // 빈 칸 클릭으로 dirty가 서면 나갈 때 불필요한 파일 쓰기가 생긴다

        m_working[_slotIndex] = null;
        m_dirty = true;
        RefreshAll();
    }

    public void ClearAll()
    {
        if (CountFilled() == 0) return;   // 이미 비어 있으면 dirty를 세우지 않는다

        Array.Clear(m_working, 0, m_working.Length);
        m_dirty = true;
        RefreshAll();
    }

    // 편성 칸·컬렉션 착용표시·카운터를 m_working 하나로부터 전량 재생성한다(부분 갱신은 불일치의 근원).
    void RefreshAll()
    {
        if (slots != null)
        {
            // 씬에서 칸을 덜 배선했거나 더 붙였을 수 있다 — 짧은 쪽 기준으로 돈다.
            int t_count = Mathf.Min(slots.Length, m_working.Length);
            for (int t_i = 0; t_i < t_count; t_i++)
                if (slots[t_i] != null) slots[t_i].Bind(t_i, m_working[t_i], ClearSlot);
        }

        if (collectionGrid != null) collectionGrid.RefreshInDeck(m_working);

        int t_filled = CountFilled();
        if (countText        != null) countText.text = $"{t_filled} / {DeckSaveManager.DECK_SIZE}";
        if (unequipAllButton != null) unequipAllButton.interactable = t_filled > 0;
    }

    int CountFilled()
    {
        int t_n = 0;
        for (int t_i = 0; t_i < m_working.Length; t_i++)
            if (m_working[t_i] != null) t_n++;

        return t_n;
    }

    void OnBackClicked()
    {
        // 드래그 도중 뒤로가기가 눌릴 수 있다(고스트가 버튼을 덮지 않는 배치). 고스트를 먼저 정리한다.
        if (dragController != null && dragController.IsDragging) dragController.Cancel();

        if (CountFilled() == DeckSaveManager.DECK_SIZE)
        {
            // SaveToFile()은 메모리 6슬롯을 통째로 flush해 로드 안 된 다른 덱을 빈 값으로 덮어쓴다
            // (DeckSaveManager.cs:74-76 주석). 그래서 이 슬롯만 반영하는 SaveSlotToFile을 쓴다.
            // m_working에는 null이 섞일 수 있지만 내부 Save()가 Where(d => d != null)로 거르고,
            // 애초에 6/6일 때만 이 분기에 들어오므로 안전하다.
            //
            // 이름은 SetName으로 메모리에 올려두면 SaveSlotToFile이 slotName까지 같이 직렬화한다.
            // SetName을 저장 경로 안에서만 부르는 게 중요하다 — 밖에서 부르면 미완성 폐기 경로에서도
            // 메모리 이름이 바뀐 채로 남는다.
            // 이름이 그대로면 SetName을 부르지 않는다 — m_savedName은 GetName의 표시용 폴백("덱 1")일 수 있고,
            // 그걸 되쓰면 "이름 미지정(빈 문자열)" 상태가 실데이터로 굳어버린다.
            string t_name    = ResolveName();
            bool   t_renamed = t_name != m_savedName;

            if (t_renamed) DeckSaveManager.SetName(m_slotIndex, t_name);
            if (m_dirty || t_renamed)
            {
                // 덱 대표 이미지는 첫 저장 때 한 번만 발급하고 이후 카드 구성이 바뀌어도 유지한다.
                // 발급을 파일 쓰기 분기 안에 두는 게 중요하다 — 밖에서 세우면 저장하지 않는 경로에서
                // 메모리에만 키가 남아 디스크와 어긋난다.
                if (string.IsNullOrEmpty(DeckSaveManager.GetImageKey(m_slotIndex)))
                    DeckSaveManager.SetImageKey(m_slotIndex, DeckImages.PickRandomKey());

                DeckSaveManager.SaveSlotToFile(m_slotIndex, m_working);
            }
            ExitToList();
            return;
        }

        // 미완성 상태로는 저장하지 않는다. DeckSaveManager.Save()도 부르면 안 된다 —
        // 메모리 슬롯이 6장 미만으로 덮여 IsSlotValid가 false가 되고, 목록에서 기존 덱이 통째로 사라진다.
        SimpleYNPopup t_popup = UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = "덱이 완성되지 않았습니다.\n변경사항을 버리고 나갈까요?",
            yesText   = "나가기",
            yesAction = ExitToList,
            noText    = "계속 편집",
            noAction  = null,
        });

        // 팝업이 못 뜨면(UIPoolManager 미배치·프리팹 미등록) 확인을 못 받은 채 화면에 갇힐 수 있다 → 그냥 내보낸다.
        if (t_popup == null)
        {
            Debug.LogError("[DeckEditController] 확인 팝업 생성 실패 — 저장 없이 목록으로 복귀한다.");
            ExitToList();
        }
    }

    void ExitToList()
    {
        if (tabController != null) tabController.CloseEditor();
    }
}
