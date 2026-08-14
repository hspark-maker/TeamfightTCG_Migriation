using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
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

    [Header("편집 모드")]
    [SerializeField] Button   editToggleButton;        // 옵션 헤더 편집 토글 — 미배선이면 편집 모드 진입 경로가 없을 뿐 목록은 정상 동작
    [SerializeField] TMP_Text editToggleLabel;         // 옵션 토글 버튼 라벨
    [SerializeField] string   editLabel = "편집";
    [SerializeField] string   doneLabel = "완료";

    readonly List<DeckSlotView> m_slots = new List<DeckSlotView>();

    // 편집 모드(각 덱 칸의 - 버튼 노출). 칸이 아니라 목록이 들고 있는다 —
    // 칸은 재빌드마다 새로 만들어지므로 상태를 칸에 두면 삭제 직후 모드가 날아간다.
    bool m_editMode;

    // 마지막 Build가 실제로 그린 덱 칸 수. 편집 토글 활성 판정에 쓴다(countText와 같은 값).
    int m_deckSlotCount;

    void Awake()
    {
        // 배선은 프리팹에서 한 번뿐이므로 등록도 한 번뿐 — OnEnable에 두면 탭 재진입마다 중복 등록된다.
        if (editToggleButton != null)
        {
            editToggleButton.onClick.AddListener(ToggleEditMode);

            // 잠김 룩도 한 번만 붙인다 — 이후 해금 반영은 붙은 컴포넌트가 스스로 한다.
            FeatureLockView.Attach(editToggleButton.gameObject, EOutgameFeature.DeckEditToggle);
        }
    }

    void OnEnable()
    {
        // 탭을 나갔다 오면 편집 모드는 항상 해제한다(DeckTabController가 "항상 목록부터"를 보장하는 것과 같은 이유).
        m_editMode = false;
        Build();

        // 목록이 켜진 채 튜토리얼이 진행되면 "신규 생성" 칸의 잠금이 그때 풀린다 — 재빌드가 유일한 반영 경로다.
        OutgameFeatureLock.OnChanged += Build;
    }

    void OnDisable()
    {
        OutgameFeatureLock.OnChanged -= Build;
    }

    // 외부에서 강제 갱신할 때의 공개 창구(목록이 켜진 채 덱이 바뀌는 경로가 생기면 사용).
    public void Refresh()
    {
        Build();
    }

    void Build()
    {
        ClearSlots();

        // 배선이 비면 칸이 하나도 없는 상태 — 개수를 0으로 되돌려야 편집 토글이 켜진 채 남지 않는다.
        m_deckSlotCount = 0;
        if (content == null || slotPrefab == null)
        {
            ApplyEditMode();
            return;
        }

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
        t_create.BindCreate(!DeckSaveManager.IsFull && OutgameFeatureLock.IsUnlocked(EOutgameFeature.DeckCreate), OnCreateClicked);
        // BindCreate 뒤에 붙인다 — 그 안에서 꺼지는 자식들까지 흑백 대상으로 잡을 이유가 없다.
        FeatureLockView.Attach(t_create.gameObject, EOutgameFeature.DeckCreate);
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
                OnSlotClicked,
                OnSlotDeleteClicked);
            m_slots.Add(t_view);
            t_display++;
        }

        // 개수는 DeckCount가 아니라 실제로 그린 칸 수로 센다 — 불변식이 깨져도 화면과 숫자가 어긋나지 않게.
        m_deckSlotCount = t_display - 1;
        if (countText != null) countText.text = $"{m_deckSlotCount} / {DeckSaveManager.SLOT_COUNT}";

        // 재빌드로 칸이 통째로 새로 생겼으므로 편집 모드를 다시 입힌다(삭제 직후 모드 유지의 근거).
        ApplyEditMode();
    }

    void ToggleEditMode()
    {
        // 토글 버튼이 목록 패널 바깥(탭 공용 헤더)에 배선되면 패널이 꺼져 있어도 리스너는 살아 있다 — 꺼진 목록을 만지지 않게 막는다.
        if (!isActiveAndEnabled) return;

        m_editMode = !m_editMode;
        ApplyEditMode();
    }

    // 편집 모드를 화면에 반영하는 단일 지점. 반영 직전에 모드를 실제 칸 수로 한 번 보정한다
    // (토글·재빌드 양쪽 경로가 여기로 모이므로 "덱 0개면 편집 모드 없음"을 한 곳에서만 지킬 수 있다).
    void ApplyEditMode()
    {
        // 덱이 0개면 편집할 대상이 없다 — 마지막 덱을 지운 순간 여기서 모드가 자동으로 풀린다.
        if (m_deckSlotCount <= 0) m_editMode = false;

        for (int t_i = 0; t_i < m_slots.Count; t_i++)
            if (m_slots[t_i] != null) m_slots[t_i].SetEditMode(m_editMode);

        if (editToggleLabel  != null) editToggleLabel.text        = m_editMode ? doneLabel : editLabel;
        if (editToggleButton != null) editToggleButton.interactable = m_deckSlotCount > 0
                                                                   && OutgameFeatureLock.IsUnlocked(EOutgameFeature.DeckEditToggle);
    }

    // - 버튼. 삭제는 되돌릴 수 없으므로 확인 팝업을 거치지 않는 경로를 만들지 않는다.
    void OnSlotDeleteClicked(int _slotIndex)
    {
        if (!DeckSaveManager.IsSlotValid(_slotIndex)) return;

        // 이름은 팝업이 뜨기 전에 캡처한다 — 삭제 후에는 그 좌표가 다른 덱을 가리킨다(압축 당김).
        string t_name = DeckSaveManager.GetDisplayName(_slotIndex);

        if (UIPoolManager.Instance == null)
        {
            // 다른 화면은 팝업이 없으면 그냥 진행하는 폴백을 쓰지만, 삭제는 복구가 불가능하므로 취소한다.
            Debug.LogWarning("[DeckListController] UIPoolManager 없음 — 덱 삭제를 취소한다.");
            return;
        }

        UIPoolManager.Instance.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = $"'{t_name}' 덱을 삭제할까요?",
            yesText   = "삭제",
            noText    = "취소",
            yesAction = () =>
            {
                // 팝업이 떠 있는 사이 세이브가 바뀌면(다른 경로의 삭제·삽입으로 압축이 당겨지면)
                // 같은 좌표가 다른 덱을 가리킨다. TryDeleteAt은 유효성만 보므로 여기서 이름으로 동일성을 재확인한다.
                if (!DeckSaveManager.IsSlotValid(_slotIndex) || DeckSaveManager.GetDisplayName(_slotIndex) != t_name)
                {
                    Debug.LogWarning($"[DeckListController] 확인 중 덱 목록이 바뀜 — 삭제 취소 slot={_slotIndex}.");
                    Build();
                    return;
                }

                // 실패 사유(미로드·레지스트리 미주입 등)는 DeckSaveManager가 이미 로그한다.
                if (!DeckSaveManager.TryDeleteAt(_slotIndex))
                    Debug.LogWarning($"[DeckListController] 덱 삭제 실패 slot={_slotIndex}.");

                // 삭제 후 뒤 덱이 앞으로 당겨져 슬롯 좌표·표시 번호가 전부 밀린다 → 부분 갱신이 성립하지 않는다.
                Build();
            },
        });
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
