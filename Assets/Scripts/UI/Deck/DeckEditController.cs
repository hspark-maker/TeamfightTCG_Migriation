using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>덱 편집 한 번의 진입분. 로비 탭(DeckTabController)과 매치 셸(MatchDeckShell)이 같은 화면을
/// 서로 다른 요구로 여는데, 그 차이를 <b>전부 여기 필드로</b> 표현한다 — 프리팹 사본이나 배리언트로 갈리지 않게.</summary>
public sealed class DeckEditData : UIData
{
    /// <summary>편집할 저장 슬롯. <see cref="isNew"/>가 true면 무시한다.</summary>
    public int slotIndex = -1;

    /// <summary>신규 덱 생성. 저장 좌표는 6/6 완성 저장 시점에 정해진다(매치 화면은 쓰지 않는다).</summary>
    public bool isNew;

    /// <summary>편집 종료 시 호스트 복귀. 미주입이면 로비 탭 셸 경로를 탄다.</summary>
    public Action onExit;

    /// <summary>드래그 레이어. 프리팹이 자기 것을 들고 있으면 무시된다 —
    /// 덱 편집을 단독으로 띄우는 테스트 씬처럼 프리팹에 없을 때만 쓰인다.</summary>
    public DeckEditDragController dragController;

    /// <summary>편집을 열 때 이 카드 한 장을 덱에서 빼고 시작한다(없으면 저장된 그대로).
    /// 튜토리얼이 "직접 골라 끼우기"를 가르치는 자리 — 빈 칸이 하나 있어야 가르칠 것이 생긴다.</summary>
    public CardData holdoutCard;

    /// <summary>상단 제목.
    /// (예전에는 배리언트가 노드를 아예 삭제해서 껐다 — 그래서 저작본이 둘로 갈렸다.)</summary>
    public bool showTitle = true;

    /// <summary>덱 전투력 표시. 매치 화면은 끈다(덱 확인 패널이 그 자리를 쥔다).</summary>
    public bool showDeckPower = true;

    /// <summary>전투 시작(매치 화면 전용). <b>주입 여부가 곧 버튼 표시 여부다</b> —
    /// 눌리는데 아무 데도 안 가는 버튼이 생기지 않게 축을 하나로 둔다.
    /// 로비 탭은 미주입 → 버튼이 꺼진다.
    ///
    /// 호출 시점은 편집기의 이탈 판정(RequestLeave)을 통과한 뒤다 — 전투가 소비하는 것은 세이브이므로
    /// 저장하지 않은 편성분을 안고 시작하면 화면에 그린 덱과 실제 출전 덱이 갈린다.</summary>
    public Action onPlay;
}

// 덱 구성 화면(DeckEditPanel에 부착). 편성 상태의 진실원이자 저장 진입점.
//
// 편집은 전부 m_working(6칸, null 허용) 위에서만 일어나고 DeckSaveManager에는 손대지 않는다.
// "취소하면 원상복구"를 별도 스냅샷 없이 성립시키기 위한 구조다 — 세이브를 편집 중에 건드리는 순간
// 취소 경로에서 복원할 원본이 사라진다.
//
// 풀드 UI다. 예전에는 로비 탭 안에 한 벌, 매치 오버레이 안에 한 벌(MatchDeckEditPanel 배리언트) —
// 저작본이 둘로 갈려 레이아웃 오버라이드가 쌓였다. 지금은 UIPoolManager가 세우는 인스턴스 하나뿐이고
// 두 호스트의 차이는 전부 DeckEditData로 들어온다.
public class DeckEditController : PooledUIBase, IPointerClickHandler
{
    [SerializeField] TMP_InputField    nameInput;      // 덱 이름 입력/표시
    [SerializeField] Button            backButton;
    [SerializeField] DeckTabController tabController;

    [Header("편성 UI")]
    [SerializeField] DeckEditSlotView[]     slots;          // 크기 6
    [SerializeField] DeckEditCollectionGrid collectionGrid;
    [Tooltip("로비에서는 LobbyTabController가 Initialize로 넘긴다(탭 프리팹에 오버라이드를 남기지 않으려고).\n"
           + "여기 배선은 덱 편집을 단독으로 띄우는 테스트 씬용 폴백이다.")]
    [SerializeField] DeckEditDragController dragController;
    [SerializeField] TMP_Text               countText;
    [SerializeField] TMP_Text               totalHpText;    // 편성된 카드의 체력 합(미배선이면 표시 생략)
    [SerializeField] DeckSynergyStrip       synergyStrip;

    [Header("버튼")]
    [SerializeField] Button unequipAllButton;
    [SerializeField] Button autoEquipButton;
    [Tooltip("우측 하단 저장 버튼. 바꾼 게 있을 때만 눌린다(미배선이면 나갈 때 확인만으로 저장한다).")]
    [SerializeField] Button saveButton;

    [Header("호스트별로 켜고 끄는 것 (선택)")]
    [Tooltip("상단 제목 노드. 호스트가 DeckEditData.showTitle로 켜고 끈다.")]
    [SerializeField] GameObject titleNode;

    [Tooltip("덱 전투력 표시 노드. 호스트가 DeckEditData.showDeckPower로 켜고 끈다.")]
    [SerializeField] GameObject deckPowerNode;

    [Tooltip("전투 시작 버튼. DeckEditData.onPlay가 주입됐을 때만 켜진다 —\n"
           + "로비 탭에서는 여기서 전투로 갈 곳이 없다. 미배선이면 그 축을 통째로 건너뛴다.")]
    [SerializeField] Button playButton;

    // 목록 칸(DeckSlotView의 이름 표시)이 짧다 — 프리팹 설정 누락에 기대지 않고 코드에서 상한을 박는다.
    const int NAME_MAX_LENGTH = 12;

    // 신규 생성은 저장 좌표가 없다(큐 삽입 위치는 저장이 확정되는 순간에 생긴다) → sentinel이 아니라 모드로 구분한다.
    enum EDeckEditMode { None, Edit, Create }

    // 편집 중인 덱 사본. 길이는 항상 DECK_SIZE 고정이고 빈 칸은 null이다(리스트로 두면 "3번 칸이 비었다"를 표현할 수 없다).
    readonly CardData[] m_working = new CardData[DeckSaveManager.DECK_SIZE];

    EDeckEditMode m_mode = EDeckEditMode.None;

    // 현재 편집 중인 저장 슬롯 인덱스(Edit 모드에서만 유효).
    int  m_slotIndex = -1;
    bool m_dirty;

    // 편집 진입 시점의 이름. 이름 변경 여부 판정 기준이자 빈 입력의 복구값이다.
    string m_savedName;

    // 튜토리얼 게이트 아래 층으로 내려앉기 위한 자기 캔버스(LiftToOverlayLayer가 만든다).
    Canvas m_sortingCanvas;

    // 이번 편집에서 일부러 빼 둔 카드. 유저가 도로 끼우면 null로 돌아간다.
    CardData m_holdout;

    // 슬롯 선택 모드에서 교체될 차례를 기다리는 카드. null이면 평상시다.
    // 편성 진실원(m_working)과 달리 이건 화면 조작 상태라 저장·dirty와 무관하다.
    CardData m_pendingSwapCard;

    /// <summary>어느 칸이든 카드가 편성된 직후 발화(탭·드래그 공통). 튜토리얼이 "지목한 카드를 끼웠는가"를
    /// 이 신호로만 판정한다 — 클릭을 들으면 드래그로 넣은 경우를 놓친다.</summary>
    public static event Action<CardData> OnAnyCardEquipped;

    public bool IsOpen => m_mode != EDeckEditMode.None;

    // 드래그 컨트롤러가 드롭 대상 판정에 쓰는 칸 목록. 미배선(null)이어도 호출측이 터지지 않게 빈 목록을 준다.
    public IReadOnlyList<DeckEditSlotView> Slots => slots ?? Array.Empty<DeckEditSlotView>();

    // 편집 종료 시 어디로 나갈지. 미주입이면 로비 탭 셸(tabController) 경로를 그대로 탄다 —
    // 로비 배선(LobbyCanvas → Tab_Deck 오버라이드)은 이 필드를 모르는 채 동작이 그대로 유지된다.
    Action m_onExit;

    // 종료 처리 주입. DeckTabController가 없는 호스트(매치 셸)가 Awake에서 한 번 건다.
    // OnDisable에서 지우지 않는다 — 이건 편집 상태가 아니라 배선이고, 패널을 껐다 켤 때마다
    // 다시 주입해야 하면 호스트가 이 패널의 라이프사이클을 추적해야 한다.
    public void SetExitHandler(Action _onExit) => m_onExit = _onExit;

    /// <summary>드래그 컨트롤러 주입. <b>프리팹이 자기 것을 들고 있으면 주입을 무시한다</b> —
    /// 풀드 UI는 자기 캔버스(order 400)에 살아서, 로비 캔버스(order 0)의 DragLayer를 주입받으면
    /// 고스트가 패널 뒤로 깔린다. 프리팹 안에 레이어가 없는 경우(단독 테스트 씬)에만 주입이 먹는다.</summary>
    public void SetDragController(DeckEditDragController _controller)
    {
        if (dragController == null && _controller != null) dragController = _controller;
    }

    // 이번 진입 요청. Open은 Show에서 돈다 — BeginEdit이 컬렉션 그리드를 세우므로 활성 상태여야 한다.
    DeckEditData m_request;

    protected override void Awake()
    {
        base.Awake();          // 풀 등록(UIPoolManager.RegisterUI)

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

        if (autoEquipButton != null)
        {
            autoEquipButton.onClick.RemoveAllListeners();
            autoEquipButton.onClick.AddListener(AutoEquip);

            // 잠김 룩은 한 번만 붙인다 — 이후 해금 반영은 붙은 컴포넌트가 스스로 한다.
            FeatureLockView.Attach(autoEquipButton.gameObject, EOutgameFeature.DeckAutoEquip);
        }

        if (saveButton != null)
        {
            saveButton.onClick.RemoveAllListeners();
            saveButton.onClick.AddListener(OnSaveClicked);
        }

        if (playButton != null)
        {
            playButton.onClick.RemoveAllListeners();
            playButton.onClick.AddListener(OnPlayClicked);
        }

        if (nameInput != null)
        {
            nameInput.characterLimit = NAME_MAX_LENGTH;
            nameInput.onEndEdit.RemoveAllListeners();
            nameInput.onEndEdit.AddListener(OnNameEndEdit);
        }

        // 시너지 아이콘 롱프레스 → 그 시너지를 가진 카드만 강조. 어떤 카드가 대상인지는 편성/컬렉션을 아는 여기서 정한다.
        if (synergyStrip != null) synergyStrip.onFocusChanged = ApplySynergyFocus;

        // 열리기 전에는 반드시 꺼져 있다. 켜고 끄는 주인은 풀(Show/Hide)뿐이다.
        //
        // 왜 코드로 강제하나: 씬/프리팹에 저작된 인스턴스가 켜진 채로 남아 있으면
        // (Tab_Deck의 DeckEditPanel 인스턴스가 m_IsActive=1로 저작돼 있다) 전체화면 편집 화면이
        // 로비 위에 깔려 하단 탭바까지 클릭을 먹는다 — 예전엔 호스트의 SetActive(false)가 매번 지웠지만
        // 그 책임이 풀로 옮겨간 지금은 "열 때만 켠다"를 여기서 불변식으로 박아야 한다.
        // 배선은 이 위에서 이미 끝났으므로 지금 꺼도 다음 열기에 그대로 살아 있다.
        if (!this.isShow) gameObject.SetActive(false);
    }

    // _synergy가 null이면 강조 해제. 대상 카드는 살짝 커지고 나머지는 흐려진다.
    // 강조 중에는 컬렉션 목록을 세운다 — 설명창을 띄운 채 화면이 스와이프되면 보라고 강조한 카드가 흘러간다.
    void ApplySynergyFocus(SynergyData _synergy)
    {
        bool t_focusing = _synergy != null;

        // 두 강조는 같은 알파 축을 쓴다 — 겹쳐 두면 시너지 롱프레스를 떼는 순간 슬롯 선택 딤까지 같이 풀려
        // "모드는 켜졌는데 화면은 평상시"가 된다. 나중에 들어온 시너지 강조에 자리를 내준다.
        if (t_focusing) CancelSlotPick();

        if (slots != null)
        {
            for (int t_i = 0; t_i < slots.Length; t_i++)
            {
                if (slots[t_i] == null) continue;
                slots[t_i].SetFocus(t_focusing, SynergyPreview.Has(slots[t_i].Card, _synergy));
            }
        }

        if (collectionGrid != null)
        {
            collectionGrid.SetSynergyFocus(_synergy);
            collectionGrid.SetScrollLocked(t_focusing);
        }

        // 강조를 걷은 자리에 모드 신호를 되돌린다. 손을 떼는 쪽은 위 CancelSlotPick을 타지 않으므로
        // (멀티터치로 시너지 강조 중에 모드로 들어간 경우) 컬렉션 딤만 지워진 반쪽 화면이 남는다.
        if (!t_focusing) ApplySlotPickVisual();
    }

    /// <summary>풀이 넘긴 진입 요청을 받아둔다. 실제 열기는 <see cref="Show"/>다 —
    /// 재사용 인스턴스는 이 시점에 아직 비활성이고, BeginEdit이 세우는 컬렉션 그리드는 활성이라야 레이아웃이 선다.</summary>
    public override void Initialization(UIData _data)
    {
        this.data    = _data;
        this.m_request = _data as DeckEditData;

        if (this.m_request == null)
        {
            Debug.LogError("[DeckEditController] DeckEditData가 아니면 어느 덱을 열지 알 수 없다.", this);
            return;
        }

        SetExitHandler(this.m_request.onExit);
        SetDragController(this.m_request.dragController);
    }

    public override void Show()
    {
        if (this.m_request == null) return;

        gameObject.SetActive(true);   // OnEnable이 여기서 돌아 소유 변경·해금 구독이 선다
        this.isShow = true;

        LiftToOverlayLayer();
        ApplyHostChrome();

        if (this.m_request.isNew) OpenNew();
        else                      Open(this.m_request.slotIndex);
    }

    // 호스트마다 켜고 끄는 장식. 노드를 지우지 않고 끄기만 한다 —
    // 예전에는 매치 배리언트가 Title·DeckPower·BackButton을 **삭제**해서 저작본이 둘로 갈렸다.
    void ApplyHostChrome()
    {
        if (this.titleNode     != null) this.titleNode.SetActive(this.m_request.showTitle);
        if (this.deckPowerNode != null) this.deckPowerNode.SetActive(this.m_request.showDeckPower);

        // 전투 시작은 갈 곳이 있을 때만 보인다 — 주입 여부가 곧 표시 여부다.
        if (this.playButton != null) this.playButton.gameObject.SetActive(this.m_request.onPlay != null);
    }

    /// <summary>닫기는 편집 상태를 버리고 루트를 내린다. <b>저장 판정은 여기서 하지 않는다</b> —
    /// 그건 RequestLeave 한 곳뿐이고, 호스트는 허가가 떨어진 뒤에 이걸 부른다.
    /// (곧바로 부르면 확인 없이 편성분이 사라진다.)</summary>
    public override void Hide()
    {
        this.isShow = false;

        Close();

        // 그리드 Clear의 Destroy는 프레임 끝이라 등록이 한 프레임 더 살아 있다 — 여기서 명시로 걷는다.
        if (this.collectionGrid != null) this.collectionGrid.ApplyTutorialAnchor(null);
        this.m_holdout = null;

        this.data = null;
        this.m_request = null;
        this.m_onExit = null;

        gameObject.SetActive(false);   // OnDisable이 드래그 고스트까지 걷는 최종 방어선
    }

    /// <summary>덱 편집을 여는 유일한 창구. 두 호스트가 같은 한 인스턴스를 쓴다.
    /// 풀이 없으면(부트 미초기화) null — 호스트는 진입 자체를 포기해야 한다(빈 화면으로 갇히지 않게).</summary>
    public static DeckEditController OpenPooled(DeckEditData _data)
    {
        if (UIPoolManager.Instance == null)
        {
            Debug.LogError("[DeckEditController] UIPoolManager가 없어 덱 편집을 열 수 없다 — Boot 초기화를 확인할 것.");

            return null;
        }

        return UIPoolManager.Instance.AddOrUpdateUI<DeckEditController>(_data);
    }

    /// <summary>풀이 세워 둔 편집 화면. <b>편집 중임을 아는 호스트만</b> 부를 것 —
    /// 세운 적 없으면 풀이 "No Such UI" 로그를 남긴다(상태 질의용 창구가 아니다).</summary>
    public static DeckEditController Pooled()
        => UIPoolManager.instance != null ? UIPoolManager.instance.GetUI<DeckEditController>() : null;

    /// <summary>풀이 세워 둔 편집 화면을 닫는다. 저장 판정은 호스트가 <see cref="RequestLeave"/>로 이미 받았다고 본다.</summary>
    public static void HidePooled() => Pooled()?.Hide();

    // 기존 덱 편집 진입. _slotIndex는 DeckSaveManager 슬롯 좌표.
    public void Open(int _slotIndex)
    {
        // DeckSaveManager는 슬롯 배열을 직접 인덱싱한다 — 범위 밖 좌표가 새면 예외가 난다.
        if (_slotIndex < 0 || _slotIndex >= DeckSaveManager.SLOT_COUNT)
        {
            Debug.LogError($"[DeckEditController] 잘못된 슬롯 인덱스 {_slotIndex} — 편집을 열지 않는다.");
            return;
        }

        m_mode      = EDeckEditMode.Edit;
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

        ApplyHoldout();

        // 표시용 폴백("덱 N")까지 포함한 값이라야, 입력을 안 건드렸을 때 rename 판정이 false로 유지된다.
        BeginEdit(DeckSaveManager.GetDisplayName(_slotIndex));
    }

    // backButton이 없는 화면(매치 편집 패널)의 종료 창구.
    public void RequestExit() => RequestLeave(ExitEditor);

    // 6/6일 때만 저장한다. 미완성이면 아무것도 하지 않는다(= 폐기).
    // 저장 규칙은 SaveNewDeck/SaveEditedDeck을 그대로 쓴다 — 규칙이 두 벌이 되는 순간 매치와 로비가 갈라진다.
    // 반환 false는 "저장을 시도했는데 실패"(신규 삽입 실패)만을 뜻한다. 저장할 게 없었으면 true다.
    public bool SaveIfComplete()
    {
        if (!IsOpen) return true;
        if (CountFilled() != DeckSaveManager.DECK_SIZE) return true;

        if (m_mode == EDeckEditMode.Create) return SaveNewDeck();

        SaveEditedDeck();

        return true;
    }

    // 신규 덱 편집 진입. 저장 좌표는 6/6 완성 저장 시점에 TryInsertFront가 정한다.
    public void OpenNew()
    {
        m_mode      = EDeckEditMode.Create;
        m_slotIndex = -1;

        Array.Clear(m_working, 0, m_working.Length);
        m_dirty = false;

        BeginEdit(DeckSaveManager.SuggestNewDeckName());
    }

    public void Close()
    {
        m_mode      = EDeckEditMode.None;
        m_slotIndex = -1;
        m_dirty     = false;
        m_savedName = null;
        Array.Clear(m_working, 0, m_working.Length);

        m_pendingSwapCard = null;

        if (dragController != null) dragController.Cancel();
        if (collectionGrid != null) collectionGrid.Clear();
        if (synergyStrip   != null) synergyStrip.Clear();
        if (nameInput      != null) nameInput.DeactivateInputField();   // 소프트키보드가 패널 밖까지 살아남지 않게
    }

    void OnEnable()
    {
        OwnershipManager.OnOwnershipChanged += OnOwnershipChanged;

        // 편집 화면이 열린 채 자동 편성이 해금될 수 있다 — 유저 조작으로만 도는 RefreshAll로는 그 순간을 못 잡아
        // 버튼이 잠긴 채 굳는다(잠김 룩은 풀리는데 버튼은 안 풀리는 어긋남까지 생긴다).
        OutgameFeatureLock.OnChanged += OnFeatureLockChanged;
    }

    void OnFeatureLockChanged()
    {
        if (IsOpen) RefreshAll();
    }

    // 편집 중 소유가 바뀌면(디버그 전체 해금 등) 컬렉션을 다시 그린다.
    // 그리드는 스스로 Build 하지 않는다 — "장착중 딤"에 필요한 편성 상태를 아는 쪽이 여기뿐이라 재빌드도 여기서 건다.
    void OnOwnershipChanged()
    {
        if (!IsOpen || collectionGrid == null) return;

        // 드래그 중이어도 안전하다 — 드래그는 타일이 아니라 CardData를 들고 있다(DeckEditDragController.Begin).
        collectionGrid.Build(OnTileDragRequest, OnTileClicked);
        RefreshAll();
        ScrollToHoldout();
    }

    // 패널이 어떤 경로로 꺼지든(탭 전환·씬 전환·부모 비활성) 드래그 고스트가 남지 않게 하는 최종 방어선.
    // Close()는 DeckTabController를 거치는 경로에서만 불린다.
    // 편집 상태(m_mode)도 같이 내려야 한다 — 안 그러면 패널이 꺼졌는데 IsOpen이 true로 남아
    // 다음 진입 전까지 편집 중인 것처럼 보고된다.
    void OnDisable()
    {
        OwnershipManager.OnOwnershipChanged -= OnOwnershipChanged;
        OutgameFeatureLock.OnChanged        -= OnFeatureLockChanged;

        m_mode      = EDeckEditMode.None;
        m_slotIndex = -1;
        m_dirty     = false;
        m_savedName = null;
        Array.Clear(m_working, 0, m_working.Length);
        m_pendingSwapCard = null;

        if (dragController != null) dragController.Cancel();
        if (synergyStrip   != null) synergyStrip.Clear();
        if (nameInput      != null) nameInput.DeactivateInputField();
    }

    // 튜토리얼이 지목한 카드를 이번 편집에서만 빼 둔다 — 세이브는 건드리지 않는다(m_working 위에서만 일어난다).
    // 칸을 앞으로 당기지 않고 그 자리를 비운다: 탭 배치(FindFirstEmpty)가 원래 자리를 되찾아야 편성 순서가 보존된다.
    void ApplyHoldout()
    {
        m_holdout = m_request != null ? m_request.holdoutCard : null;
        if (m_holdout == null) return;

        for (int t_i = 0; t_i < m_working.Length; t_i++)
        {
            if (m_working[t_i] != m_holdout) continue;

            m_working[t_i] = null;
            return;
        }

        // 빼 둘 카드가 덱에 없으면 빈 칸이 안 생긴다 — "이 카드를 끼워라"라는 안내가 끼울 자리를 못 찾는다.
        Debug.LogWarning($"[DeckEditController] 튜토리얼이 지목한 카드({m_holdout.name})가 이 덱에 없어 빈 칸을 만들지 못했다.");
        m_holdout = null;
    }

    // 이 화면은 튜토리얼 안내가 가리키는 무대라 게이트 아래 층으로 내려앉는다(절차는 UiSortingOrder가 쥔다).
    void LiftToOverlayLayer()
    {
        this.m_sortingCanvas = UiSortingOrder.LiftNested(gameObject, UiSortingOrder.PooledOverlay);

        LiftDragLayer();
    }

    // 드래그 고스트는 이 패널의 자식(DragLayer)이라 승격에 함께 딸려 내려간다 — 그대로 두면 게이트 딤 밑에서 끌린다.
    void LiftDragLayer()
    {
        if (this.dragController == null) return;

        UiSortingOrder.LiftNested(this.dragController.gameObject, UiSortingOrder.DragGhost);
    }

    // 두 진입점의 공통 후반부. m_mode·m_slotIndex·m_working은 호출 전에 확정돼 있어야 한다.
    void BeginEdit(string _initialName)
    {
        m_savedName = _initialName;
        if (nameInput != null) nameInput.SetTextWithoutNotify(m_savedName);   // 세팅이 onEndEdit로 되튀지 않게

        if (collectionGrid != null) collectionGrid.Build(OnTileDragRequest, OnTileClicked);

        if (dragController != null) dragController.Setup(() => Slots, AssignSlot);
        // 배선이 프리팹 인스턴스 오버라이드로만 존재한다(DragLayer가 이 프리팹 밖에 있다) — Revert 한 번에 조용히 사라진다.
        // 여기서 알리지 않으면 "롱프레스해도 아무 일 없음"으로만 드러난다. 패널을 열 때 한 번만 찍힌다.
        else Debug.LogError($"[DeckEditController] dragController 미배선({name}) — 드래그 이동이 동작하지 않는다(클릭 배치만 가능).");

        RefreshAll();
        ScrollToHoldout();
    }

    // 지목된 타일이 목록 밖에 있으면 게이트가 승격했을 때 클리핑이 끊겨 화면에 샌다 — 그리기 직후 안으로 들여놓는다.
    void ScrollToHoldout()
    {
        if (m_holdout != null && collectionGrid != null) collectionGrid.EnsureVisible(m_holdout);
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

        // 이름 변경도 IsDirty의 한 축이다 — 여기서 갱신하지 않으면 이름만 고쳤을 때 저장 버튼이 죽은 채 남는다.
        RefreshSaveButton();
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

        CancelSlotPick();   // 끌기 시작하면 놓을 칸은 손끝이 정한다 — 고르라는 신호가 남아 있을 이유가 없다

        // 고스트 크기는 그리드가 정한다 — 매치 패널은 GridRatioFitter가 cellSize를 런타임에 계산한다.
        dragController.Begin(_tile.Card,
                             _data,
                             collectionGrid != null ? collectionGrid.Scroll    : null,
                             collectionGrid != null ? collectionGrid.CellSize  : default);
    }

    // 컬렉션 칸 클릭 = 앞쪽 빈 칸에 자동 배치(드래그의 지름길). 빈 칸이 없으면 슬롯 선택 모드로 넘어간다 —
    // 예전에는 여기서 조용히 물러나, 6칸을 채우고 나면 롱프레스 드래그 말고는 교체할 방법이 없었다.
    // 이미 편성된 카드는 타일에서 걸러 여기 오지 않는다.
    // 배치는 AssignSlot에 위임한다 — 덱 내 중복 제거·dirty·재갱신을 드래그 드롭과 같은 경로로 태우기 위함이다.
    void OnTileClicked(DeckEditCardTile _tile)
    {
        // 편집이 닫힌 뒤 같은 프레임에 늦게 디스패치될 수 있다(그리드 Clear의 Destroy는 프레임 끝에 반영).
        // 가드가 없으면 닫힌 편집기의 0번 칸에 카드가 꽂히고 m_dirty까지 선다.
        if (!IsOpen) return;
        if (_tile == null || _tile.Card == null) return;

        // 드래그 도중 들어온 클릭은 무시한다. 입력 모듈 쪽 차단(eligibleForClick)이 뚫려도 고스트가 붙은 채
        // 카드가 칸에 꽂히는 상태는 만들지 않는다.
        if (dragController != null && dragController.IsDragging) return;

        // 고른 카드를 한 번 더 누르면 취소. 다른 카드를 누르면 아래에서 대상만 갈아탄다.
        if (m_pendingSwapCard == _tile.Card) { CancelSlotPick(); return; }

        int t_empty = FindFirstEmpty();

        // 6칸이 다 찼다 — 어느 칸을 내줄지는 유저가 고른다.
        if (t_empty < 0) { EnterSlotPick(_tile.Card); return; }

        // 빈 칸이 있으면 고를 것이 없다. 모드 중에 칸이 비는 경로(슬롯 해제·전체 해제)가 있어 여기서도 내려둔다.
        CancelSlotPick();
        AssignSlot(t_empty, _tile.Card);
    }

    // 교체를 기다리는 카드를 세운다. 편성(m_working)은 유저가 칸을 고를 때까지 그대로다 —
    // 이 모드가 바꾸는 것은 "다음 클릭의 뜻"과 화면 신호뿐이다.
    void EnterSlotPick(CardData _card)
    {
        if (_card == null) return;

        m_pendingSwapCard = _card;
        ApplySlotPickVisual();
    }

    // 슬롯 선택 모드 해제. 편성은 건드리지 않는다.
    void CancelSlotPick()
    {
        if (m_pendingSwapCard == null) return;   // 평상시 클릭마다 그리드 전체를 되칠할 이유가 없다

        m_pendingSwapCard = null;
        ApplySlotPickVisual();
    }

    // 모드 신호를 화면에 칠한다: 고르지 않은 컬렉션 카드를 눌러 딤을 깔고, 그 위로 편성 6칸과 고른 카드를
    // 함께 띄운다 — 눌러야 할 것(6칸)과 들고 있는 것(고른 카드)이 같은 층에 남는다.
    // 테두리(highlightFrame)는 쓰지 않는다 — 그건 드래그 오버 전용 신호다.
    // 슬롯 재바인딩(Bind)과 컬렉션 착용딤(RefreshInDeck)이 이 표시를 원상복구하므로 반드시 그 뒤에 불러야 한다.
    void ApplySlotPickVisual()
    {
        bool t_picking = m_pendingSwapCard != null;

        if (slots != null)
        {
            // 6칸은 전부 골라야 할 대상이라 가려낼 것이 없다(_match는 항상 true).
            for (int t_i = 0; t_i < slots.Length; t_i++)
                if (slots[t_i] != null) slots[t_i].SetFocus(t_picking, true);
        }

        if (collectionGrid != null) collectionGrid.SetPickedCard(m_pendingSwapCard);
    }

    /// <summary>패널 여백 클릭 = 슬롯 선택 취소. 슬롯·컬렉션 타일·버튼은 자기 자리에서 클릭을 소비하므로
    /// 여기까지 올라오지 않는다 — 취소 전용 풀스크린 캐처를 따로 세울 필요가 없다.</summary>
    public void OnPointerClick(PointerEventData _data)
    {
        // 입력 모듈은 우클릭·휠클릭에도 클릭 핸들러를 태운다(DeckEditCardTile.OnPointerClick과 같은 이유).
        if (_data != null && _data.button != PointerEventData.InputButton.Left) return;

        CancelSlotPick();
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

        // 빼 뒀던 카드가 제자리로 돌아오면 디스크와 같아진다 — dirty를 남기면 튜토리얼 한복판에 저장 확인 팝업이 낀다.
        // 다른 칸에 끼웠다면 편성 순서만 달라진 것이라 그 차이는 버린다(전투 덱은 시나리오가 정한다).
        if (_card == m_holdout)
        {
            m_holdout = null;
            if (CountFilled() == DeckSaveManager.DECK_SIZE) m_dirty = false;
        }

        RefreshAll();

        OnAnyCardEquipped?.Invoke(_card);
    }

    // 편성 칸 클릭. 뜻은 모드가 정한다 — 평상시엔 해제, 슬롯 선택 모드에선 고른 카드와 교체.
    // 교체 전용 경로를 따로 두지 않는다: AssignSlot의 덧씌우기가 곧 교체다(드래그 드롭도 같은 길을 탄다).
    void OnSlotClicked(int _slotIndex)
    {
        if (m_pendingSwapCard == null) { ClearSlot(_slotIndex); return; }

        var t_card = m_pendingSwapCard;

        // AssignSlot이 부르는 RefreshAll보다 먼저 내려야 교체 결과 위에 강조가 남지 않는다.
        CancelSlotPick();
        AssignSlot(_slotIndex, t_card);
    }

    // 편성 칸 해제. 유일한 호출자는 OnSlotClicked다(슬롯은 코드에서만 Bind로 배선된다).
    void ClearSlot(int _slotIndex)
    {
        if (_slotIndex < 0 || _slotIndex >= m_working.Length) return;
        if (m_working[_slotIndex] == null) return;   // 빈 칸 클릭으로 dirty가 서면 나갈 때 불필요한 파일 쓰기가 생긴다

        m_working[_slotIndex] = null;
        m_dirty = true;
        RefreshAll();
    }

    public void ClearAll()
    {
        // 편성이 통째로 바뀌면 "6칸이 다 차서 고르는 중"이라는 모드의 전제가 사라진다.
        // 남겨두면 빈 칸이 생긴 덱에서 다음 슬롯 탭이 교체가 아니라 이동이 되어 덱에 구멍이 난다.
        CancelSlotPick();

        if (CountFilled() == 0) return;   // 이미 비어 있으면 dirty를 세우지 않는다

        Array.Clear(m_working, 0, m_working.Length);
        m_dirty = true;
        RefreshAll();
    }

    // 자동 편성. 빈 칸만 앞에서부터 메운다(이미 편성한 카드는 건드리지 않는다).
    // AssignSlot을 반복 호출하지 않고 m_working을 직접 채운다 — 덱 내 중복 금지는 아래 중복 스킵이 대신 보장한다.
    // 채우는 순서는 체력 내림차순이다(마스터 등록 순서가 아니다) — "자동 편성"이 곧 "맨 앞 6장"이 되지 않게.
    public void AutoEquip()
    {
        CancelSlotPick();   // ClearAll과 같은 이유 — 편성을 갈아엎고 나면 고르는 중이던 전제가 남지 않는다

        bool t_changed = false;

        // 튜토리얼이 지정한 덱이 최우선. 저작 의도가 소유 판정보다 앞서므로 미소유여도 경고만 남기고 채운다.
        if (OutgameTutorialRunner.TryGetForcedDeck(out var t_forced) && t_forced != null)
        {
            for (int t_i = 0; t_i < t_forced.Count; t_i++)
            {
                var t_card = t_forced[t_i];
                if (t_card == null) continue;
                if (ContainsInWorking(t_card)) continue;

                if (!OwnershipManager.IsOwned(t_card))
                    Debug.LogWarning($"[DeckEditController] 튜토리얼 지정 카드 '{t_card.name}'가 미소유 상태다 — 그대로 편성한다.");

                if (!TryFillFirstEmpty(t_card)) break;   // 6칸이 다 찼다
                t_changed = true;
            }
        }

        // 나머지는 소유 카드 중 체력 높은 순으로 메운다. 카탈로그가 없으면 채울 원본이 없으므로 조용히 넘어간다.
        // 이미 6칸이 찼으면 후보 수집·정렬 자체가 낭비다(버튼은 비활성이지만 이 메서드는 public).
        if (CardCatalog.IsReady && CountFilled() < DeckSaveManager.DECK_SIZE)
        {
            var t_cards      = CardCatalog.All;
            var t_candidates = new List<(CardData card, int order)>(t_cards.Count);

            for (int t_i = 0; t_i < t_cards.Count; t_i++)
            {
                var t_card = t_cards[t_i];
                if (t_card == null) continue;                     // CardRegistry의 ID 보존용 빈 칸
                if (!OwnershipManager.IsOwned(t_card)) continue;
                if (ContainsInWorking(t_card)) continue;

                t_candidates.Add((t_card, t_i));
            }

            // 동점을 카탈로그 인덱스로 깬다. List.Sort는 불안정 정렬이라 tiebreak가 없으면
            // 같은 체력 카드들의 편성 결과가 호출마다 달라진다(유저 눈에는 버튼이 랜덤으로 보인다).
            t_candidates.Sort((_a, _b) =>
            {
                int t_cmp = DeckPower.Of(_b.card).CompareTo(DeckPower.Of(_a.card));

                return t_cmp != 0 ? t_cmp : _a.order.CompareTo(_b.order);
            });

            for (int t_i = 0; t_i < t_candidates.Count; t_i++)
            {
                if (!TryFillFirstEmpty(t_candidates[t_i].card)) break;
                t_changed = true;
            }
        }

        if (!t_changed) return;   // 실제 변화가 없으면 dirty를 세우지 않는다(빈 칸 클릭 가드와 같은 이유)

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
                if (slots[t_i] != null) slots[t_i].Bind(t_i, m_working[t_i], OnSlotClicked);
        }

        if (collectionGrid != null)
        {
            collectionGrid.RefreshInDeck(m_working);
            collectionGrid.ApplyTutorialAnchor(m_holdout);   // 끼우고 나면 m_holdout이 null이라 저절로 걷힌다
        }

        int t_filled = CountFilled();
        if (countText        != null) countText.text   = $"{t_filled} / {DeckSaveManager.DECK_SIZE}";
        if (totalHpText      != null) totalHpText.text = DeckPower.Of(m_working).ToString();
        if (synergyStrip     != null) synergyStrip.Refresh(m_working);
        if (unequipAllButton != null) unequipAllButton.interactable = t_filled > 0;
        if (autoEquipButton  != null) autoEquipButton.interactable  = t_filled < DeckSaveManager.DECK_SIZE    // 가득 차면 채울 칸이 없다
                                                                   && OutgameFeatureLock.IsUnlocked(EOutgameFeature.DeckAutoEquip);
        RefreshSaveButton();

        // 위 재바인딩이 하이라이트·알파를 전부 원상복구한 뒤라야 모드 신호가 살아남는다.
        // 평상시에 되칠하지 않는 이유: 해제는 CancelSlotPick이 이미 했고, 여기서 한 번 더 지우면
        // 시너지 강조 도중 재갱신이 끼었을 때(해금·소유 변동) 그 강조까지 같이 걷힌다.
        if (m_pendingSwapCard != null) ApplySlotPickVisual();
    }

    /// <summary>저장 버튼은 <b>바꾼 게 있을 때만</b> 눌린다. 미완성이어도 눌리게 두는 이유는,
    /// 왜 저장이 안 되는지 알려주는 자리가 필요해서다 — 회색으로 죽여 두면 유저는 이유를 알 수 없다.</summary>
    void RefreshSaveButton()
    {
        if (saveButton == null) return;

        saveButton.interactable = IsOpen && IsDirty;
    }

    int CountFilled()
    {
        int t_n = 0;
        for (int t_i = 0; t_i < m_working.Length; t_i++)
            if (m_working[t_i] != null) t_n++;

        return t_n;
    }

    // 앞쪽 빈 칸의 인덱스. 없으면 -1.
    int FindFirstEmpty()
    {
        for (int t_i = 0; t_i < m_working.Length; t_i++)
            if (m_working[t_i] == null) return t_i;

        return -1;
    }

    // 앞쪽 빈 칸 하나에 카드를 놓는다. 빈 칸이 없으면 false(호출측이 순회를 끊는 신호).
    // 자동 편성 전용 — dirty·재갱신은 호출측(AutoEquip)이 순회를 끝낸 뒤 한 번에 처리한다.
    bool TryFillFirstEmpty(CardData _card)
    {
        int t_empty = FindFirstEmpty();
        if (t_empty < 0) return false;

        m_working[t_empty] = _card;
        return true;
    }

    bool ContainsInWorking(CardData _card)
    {
        for (int t_i = 0; t_i < m_working.Length; t_i++)
            if (m_working[t_i] == _card) return true;

        return false;
    }

    void OnBackClicked() => RequestLeave(ExitEditor);

    // 전투 시작. 나가기와 같은 판정을 거친다 — 전투가 소비하는 것은 세이브라, 저장하지 않은 편성분을
    // 안고 시작하면 화면에 그린 덱과 실제 출전 덱이 갈린다(RequestLeave가 저장·미완성 확인을 맡는다).
    //
    // 호출 시점의 요청을 잡아 둔다: 확인 팝업 응답을 기다리는 동안 호스트가 다른 요청으로 이 화면을
    // 다시 열면 m_request가 갈리고, 그때 허가가 떨어지면 엉뚱한 호스트의 전투가 시작된다.
    void OnPlayClicked()
    {
        Action t_onPlay = this.m_request?.onPlay;
        if (t_onPlay == null) return;

        RequestLeave(t_onPlay);
    }

    // 편집 화면을 떠나도 되는지 판정하는 단일 창구. 뒤로가기든 탭 버튼이든 전부 여기로 모은다 —
    // 경로마다 판정이 갈리면 "어떤 버튼으로 나갔는지"에 따라 편성분이 사라지고 말고가 달라진다.
    //
    // 나가도 되는 순간에 _onGranted를 부른다(즉시 또는 확인 팝업의 "나가기" 이후).
    // 나가면 안 되면 아무것도 부르지 않는다 — 호출측은 자기 전환을 그대로 포기하면 된다.
    public void RequestLeave(Action _onGranted)
    {
        // 편집이 열려 있지 않으면 저장할 것도 확인받을 것도 없다 —
        // 가드가 없으면 빈 m_working이 "미완성"으로 읽혀 엉뚱한 확인 팝업이 뜬다.
        if (!IsOpen)
        {
            _onGranted?.Invoke();

            return;
        }

        // 드래그 도중 눌릴 수 있다(고스트가 버튼·탭바를 덮지 않는 배치). 고스트를 먼저 정리한다.
        if (dragController != null && dragController.IsDragging) dragController.Cancel();

        // 바꾼 게 없으면 저장할 것도 확인받을 것도 없다. 여기서 걸러야 "들어왔다 그냥 나가기"에
        // 팝업이 뜨지 않는다 — 매번 물으면 확인 창이 소음이 되고, 유저는 내용을 안 읽고 누르게 된다.
        if (!IsDirty)
        {
            _onGranted?.Invoke();

            return;
        }

        SimpleYNPopupData t_data = CountFilled() == DeckSaveManager.DECK_SIZE
            ? new SimpleYNPopupData
              {
                  titleText = "변경사항을 저장할까요?",
                  yesText   = "저장",
                  // 삽입에 실패하면 나가지 않는다 — 그냥 내보내면 편성한 6장이 조용히 증발한다.
                  // 화면을 유지해 재시도 여지를 남긴다(실패 사유는 DeckSaveManager가 로그로 남긴다).
                  yesAction = () => { if (SaveIfComplete()) _onGranted?.Invoke(); },
                  noText    = "저장 안 함",
                  noAction  = () => _onGranted?.Invoke(),
              }
            // 미완성 상태로는 저장하지 않는다. DeckSaveManager.Save()도 부르면 안 된다 —
            // 메모리 슬롯이 6장 미만으로 덮여 IsSlotValid가 false가 되고, 목록에서 기존 덱이 통째로 사라진다.
            // 그래서 여기서는 "저장" 선택지 자체를 주지 않고, 왜 저장이 안 되는지만 알린다.
            : new SimpleYNPopupData
              {
                  titleText = $"카드 {DeckSaveManager.DECK_SIZE}장을 모두 채워야 저장할 수 있습니다.\n변경사항을 버리고 나갈까요?",
                  yesText   = "나가기",
                  yesAction = () => _onGranted?.Invoke(),
                  noText    = "계속 편집",
                  noAction  = null,
              };

        SimpleYNPopup t_popup = UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(t_data);

        // 팝업이 못 뜨면(UIPoolManager 미배치·프리팹 미등록) 확인을 못 받은 채 화면에 갇힐 수 있다 → 그냥 내보낸다.
        if (t_popup == null)
        {
            Debug.LogError("[DeckEditController] 확인 팝업 생성 실패 — 저장 없이 편집을 닫는다.");
            _onGranted?.Invoke();
        }
    }

    /// <summary>디스크와 달라진 게 있는가. 카드 편성(m_dirty)뿐 아니라 <b>이름 변경도 변경사항</b>이다 —
    /// 이름만 고치고 나간 유저에게 아무것도 안 묻고 버리면 그대로 사라진다.
    ///
    /// 신규(Create)는 이름을 비교하지 않는다. m_savedName이 실제 저장값이 아니라 제안 이름이라
    /// 비교가 성립하지 않고, 카드를 한 장이라도 놓으면 어차피 m_dirty가 선다.</summary>
    bool IsDirty => m_dirty
                 || (m_mode == EDeckEditMode.Edit && ResolveName() != m_savedName);

    // 저장 버튼. 편집 화면에 머문 채 지금까지의 편성을 확정한다(나가기와 달리 화면을 닫지 않는다).
    void OnSaveClicked()
    {
        if (!IsOpen) return;

        CancelSlotPick();   // 저장은 편성을 확정하는 사건이다 — 끝난 화면이 계속 칸을 고르라고 말하지 않게

        // 미완성은 저장하지 않는다 — 6장 미만으로 세이브를 덮으면 IsSlotValid가 false가 되어
        // 목록에서 그 덱이 통째로 사라진다(RequestLeave의 미완성 분기와 같은 이유).
        // 버튼을 죽여두지 않고 여기서 알리는 쪽을 택했다: 왜 저장이 안 되는지 말할 자리가 필요하다.
        if (CountFilled() != DeckSaveManager.DECK_SIZE)
        {
            // SimpleYNPopup은 버튼이 항상 둘이다(단일 확인 모드가 없다) → 남는 한 자리를 빈 채로 두지 않고
            // 바로 채워 주는 선택지로 쓴다. 자동 편성이 잠겨 있으면 그 자리는 그냥 닫기가 된다.
            bool t_canAutoEquip = OutgameFeatureLock.IsUnlocked(EOutgameFeature.DeckAutoEquip);

            UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
            {
                titleText = $"카드 {DeckSaveManager.DECK_SIZE}장을 모두 채워야 저장할 수 있습니다.",
                yesText   = t_canAutoEquip ? "자동 편성" : "확인",
                yesAction = t_canAutoEquip ? AutoEquip : (Action)null,
                noText    = "닫기",
                noAction  = null,
            });

            return;
        }

        // 실패(신규 삽입 불가)면 아무것도 바꾸지 않는다 — dirty를 내리면 유저는 저장된 줄 알고 나간다.
        if (!SaveIfComplete()) return;

        MarkSaved();
    }

    /// <summary>저장이 끝난 뒤의 상태 정리. 여기서 dirty를 내려야 저장 버튼이 다시 죽는다.
    /// m_savedName도 같이 굳힌다 — 이름 비교가 IsDirty의 한 축이라, 안 굳히면 저장 직후에도 계속 dirty다.</summary>
    void MarkSaved()
    {
        m_dirty     = false;
        m_savedName = ResolveName();

        RefreshAll();
    }

    // 신규 덱은 rename·dirty 판정이 없다 — 6/6이 채워졌으면 항상 저장 대상이다.
    // 이름도 항상 실문자열로 굳힌다. 빈 이름으로 두면 이후 덱이 앞뒤로 밀릴 때 표시 폴백("덱 N")이 따라 변한다.
    bool SaveNewDeck()
    {
        // 이미지 키는 삽입 "전에" 뽑는다 — IsKeyInUse가 삽입 전 상태를 봐야 중복 회피가 정확하고,
        // 삽입 후 SetImageKey는 SaveAll이 끝난 뒤라 메모리에만 남는다.
        if (DeckSaveManager.TryInsertFront(m_working, ResolveName(), DeckImages.PickRandomKey(), out int t_index))
        {
            // 저장 버튼으로 신규를 확정하면 화면에 그대로 머문다 — 그 상태로 또 누르면 같은 덱이 한 벌 더 꽂힌다.
            // 저장된 순간부터는 "그 슬롯을 편집 중"이어야 한다(나가기 경로에서는 곧 닫혀서 드러나지 않던 문제).
            m_mode      = EDeckEditMode.Edit;
            m_slotIndex = t_index;

            return true;
        }

        // 만석·미로드 등 실패 사유는 DeckSaveManager가 로그로 남긴다. 여기서는 나가지 않는다는 사실만 알린다.
        Debug.LogError("[DeckEditController] 신규 덱 저장 실패 — 편집 화면을 유지한다.");

        return false;
    }

    // 기존 덱 저장. 위치(m_slotIndex)는 그대로 유지한다(편집으로는 목록 맨 앞으로 승격하지 않는다).
    void SaveEditedDeck()
    {
        // SaveAll()은 메모리 6슬롯을 통째로 flush해 로드 안 된 다른 덱을 빈 값으로 덮어쓴다
        // (DeckSaveManager.SaveSlot 주석). 그래서 이 슬롯만 반영하는 SaveSlot을 쓴다.
        // m_working에는 null이 섞일 수 있지만 내부 Save()가 Where(d => d != null)로 거르고,
        // 애초에 6/6일 때만 이 분기에 들어오므로 안전하다.
        //
        // 이름은 SetName으로 메모리에 올려두면 SaveSlot이 이름까지 같이 직렬화한다.
        // SetName을 저장 경로 안에서만 부르는 게 중요하다 — 밖에서 부르면 미완성 폐기 경로에서도
        // 메모리 이름이 바뀐 채로 남는다.
        // 이름이 그대로면 SetName을 부르지 않는다 — m_savedName은 GetDisplayName의 표시용 폴백("덱 1")일 수 있고,
        // 그걸 되쓰면 "이름 미지정(빈 문자열)" 상태가 실데이터로 굳어버린다.
        string t_name    = ResolveName();
        bool   t_renamed = t_name != m_savedName;

        if (t_renamed) DeckSaveManager.SetName(m_slotIndex, t_name);
        if (!m_dirty && !t_renamed) return;

        // 덱 대표 이미지는 첫 저장 때 한 번만 발급하고 이후 카드 구성이 바뀌어도 유지한다.
        // 발급을 저장 분기 안에 두는 게 중요하다 — 밖에서 세우면 저장하지 않는 경로에서
        // 메모리에만 키가 남아 세이브와 어긋난다.
        if (string.IsNullOrEmpty(DeckSaveManager.GetImageKey(m_slotIndex)))
            DeckSaveManager.SetImageKey(m_slotIndex, DeckImages.PickRandomKey());

        DeckSaveManager.SaveSlot(m_slotIndex, m_working);
    }

    // 편집 종료. 실제 복귀 지점은 셸이 정한다 — 여기서는 "나간다"만 알면 된다.
    // 주입 훅이 우선이고 없으면 로비 탭 셸(기본 탭 복귀)이다. 호스트 의존이 수렴하는 유일한 지점이다.
    void ExitEditor()
    {
        if (m_onExit != null)
        {
            m_onExit();

            return;
        }

        if (tabController != null) tabController.CloseEditor();
    }
}
