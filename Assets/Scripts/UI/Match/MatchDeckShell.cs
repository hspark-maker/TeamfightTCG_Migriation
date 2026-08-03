using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

// 전투 씬 진입 직후 덱을 고르고 편집하는 화면의 셸(MatchDeckRoot에 부착). 로비 DeckTabController의 전투판.
//
// 전투 시작은 이 화면이 결정한다 — 호스트(GameInitializer)가 필드를 초기화하기 전에 RunSelectionAsync를
// await하고, "시작"이 눌려야 통과한다. 필드 초기화가 DeckConfig.PlayerDeck을 소비하므로 게이트가 그보다 앞이어야 한다.
//
// 아는 것은 "어느 저장 슬롯이 선택됐는가"와 "두 패널 중 무엇을 보이는가"뿐 — 편성·저장은 전부 DeckEditController에 위임한다.
// 선택을 DeckConfig가 아니라 슬롯 인덱스로 드는 이유: DeckConfig는 직렬화 없는 씬 캐리어라 "어느 슬롯"을 표현하지 못한다.
public class MatchDeckShell : MonoBehaviour
{
    [SerializeField] GameObject matchPanel;   // MatchDeckPanel 인스턴스
    [SerializeField] GameObject editPanel;    // MatchDeckEditPanel 인스턴스

    [Header("컨트롤러")]
    [SerializeField] MatchDeckPanelView       panelView;
    [SerializeField] DeckEditController       editController;
    [SerializeField] MatchDeckStripController strip;

    // 편집 패널 좌하단 뒤로가기. 배리언트에서 원본 BackButton을 삭제했으므로 편집 화면의 유일한 종료 경로다.
    [SerializeField] Button editBackButton;

    [Header("단독 실행 폴백")]
    // 전투 씬에는 부트 프리팹이 없다 — 로비를 거치면 DontDestroyOnLoad로 따라오지만
    // 전투 씬 단독 Play에서는 카탈로그·소유·덱 세이브가 전부 비어 목록이 0개가 된다.
    [SerializeField] CardRegistry      fallbackCardRegistry;
    [SerializeField] DeckImageCatalog  fallbackDeckImages;

    // 현재 선택된 저장 슬롯. 유효한 덱이 하나도 없으면 -1.
    public int SelectedSlot { get; private set; } = -1;

    // 게이트 결과. Pending인 동안 호스트가 전투 시작을 붙잡고 있다.
    enum EGate { Pending, Confirmed, Cancelled }

    EGate m_gate = EGate.Pending;

    // 루트가 비활성인 채로 Open이 불릴 수 있다(SetActive가 Awake를 동기 실행하지만 순서에 기대지 않는다).
    bool m_wired;

    void Awake()
    {
        EnsureWired();
    }

    // 배선은 한 번만. 편집 종료 훅과 뒤로가기 버튼을 여기서 건다 —
    // 프리팹 onClick으로 배선하면 셸이 모르는 종료 경로가 생겨 복귀 후 MySection 갱신을 놓친다.
    void EnsureWired()
    {
        if (m_wired) return;
        m_wired = true;

        if (editController != null) editController.SetExitHandler(OnEditorExit);

        if (editBackButton != null)
        {
            editBackButton.onClick.RemoveAllListeners();
            // 셸이 아니라 컨트롤러로 직행한다 — 저장 판정·미완성 확인 팝업은 OnBackClicked 한 곳에만 있다.
            editBackButton.onClick.AddListener(OnEditBackClicked);
        }
        else
        {
            // 원본 BackButton을 배리언트에서 지웠기 때문에, 이게 없으면 편집 화면에서 나갈 방법이 전혀 없다.
            Debug.LogError("[MatchDeckShell] editBackButton 미배선 — 편집 화면에서 빠져나올 수 없다.");
        }
    }

    // 전투 시작 게이트. 호스트(GameInitializer)가 필드 초기화 "전에" 이걸 await 하고,
    // true를 받으면 DeckConfig.PlayerDeck이 확정된 상태로 전투를 이어간다.
    // false = 유저가 전투를 포기했다(또는 씬이 내려갔다) → 호스트가 복귀를 처리한다.
    public async UniTask<bool> RunSelectionAsync(CancellationToken _ct)
    {
        EnsureBoot();

        m_gate = EGate.Pending;
        Open();

        // 씬 파괴로 취소되면 Confirm/Cancel 어느 쪽도 오지 않는다 — 예외 대신 취소 여부를 값으로 받는다.
        bool t_canceled = await UniTask.WaitUntil(() => m_gate != EGate.Pending, cancellationToken: _ct)
                                       .SuppressCancellationThrow();

        if (t_canceled) return false;

        Close();

        return m_gate == EGate.Confirmed;
    }

    // 전투 시작 버튼. 선택된 덱을 씬 전환 캐리어에 실은 뒤에만 게이트를 연다 —
    // 실패(유효 덱 없음)하면 화면을 유지한다. 버튼 interactable로도 막히지만 그건 표시일 뿐이다.
    public void Confirm()
    {
        if (!TryConfirmSelection())
        {
            Debug.LogWarning("[MatchDeckShell] 유효한 덱이 선택되지 않았다 — 전투를 시작하지 않는다.");

            return;
        }

        m_gate = EGate.Confirmed;
    }

    // 전투 포기. 실제로 어디로 돌아갈지는 호스트가 정한다(셸은 씬을 모른다).
    public void Cancel()
    {
        m_gate = EGate.Cancelled;
    }

    // 전투 씬 단독 Play 보강. 로비를 거쳐 들어오면 부트가 이미 살아 있으므로 아무것도 하지 않는다.
    // 도감 화면의 EnsureBoot와 같은 결이되, 덱 목록이 필요하므로 덱 세이브 로드까지 포함한다.
    void EnsureBoot()
    {
        if (CardCatalog.IsReady) return;
        if (fallbackCardRegistry == null)
        {
            Debug.LogWarning("[MatchDeckShell] 카탈로그 미초기화 + 폴백 레지스트리 미배선 — 덱 목록이 비어 보인다.");

            return;
        }

        DataSaveManager.Load();
        CardCatalog.SetSource(fallbackCardRegistry.All);
        OwnershipManager.Init();

        // 세이브의 카드 키를 CardData로 재수화하려면 마스터 목록을 먼저 넘겨야 한다(BootInstaller와 같은 순서).
        DeckSaveManager.SetCardRegistry(fallbackCardRegistry.All);
        DeckSaveManager.LoadFromSave();

        DeckImages.SetSource(fallbackDeckImages);
    }

    // 덱 화면 진입. 게이트를 쓰지 않고 직접 열 때(디버그·후속 진입점)의 창구다.
    // _slotIndex가 음수면 이전 선택 → 첫 유효 슬롯 순으로 알아서 고른다.
    public void Open(int _slotIndex = -1)
    {
        gameObject.SetActive(true);
        EnsureWired();

        SelectedSlot = ResolveSlot(_slotIndex);

        // 상대 덱은 여기서 뽑지 않는다 — 호스트(GameInitializer.ConfirmEnemyDeck)가 이 화면을 열기 전에
        // 확정해 DeckConfig에 실어둔다. 이 화면이 다시 뽑으면 확정 지점이 두 곳이 되고,
        // 전투가 소비하는 값과 화면에 그린 값이 갈린다. 뷰는 캐리어를 읽기만 한다.
        ShowMatchPanel();
    }

    public void Close()
    {
        // 편집 중 닫히는 경로는 없지만(편집은 뒤로가기로만 나간다), 패널이 켜진 채 루트가 꺼지면
        // DeckEditController.OnDisable이 편집 상태를 무저장 폐기한다 — 그게 이 화면의 사양이다.
        gameObject.SetActive(false);
    }

    // 매치 패널 EditButton. 편집 대상은 지금 선택된 덱이다.
    public void OpenEditor()
    {
        EnsureWired();

        SelectedSlot = ResolveSlot(SelectedSlot);

        // 매치 화면은 신규 덱 생성을 지원하지 않는다(가로 리스트에 + 칸이 없다) → 편집할 원본이 없으면 열지 않는다.
        if (SelectedSlot < 0)
        {
            Debug.LogWarning("[MatchDeckShell] 저장된 유효 덱이 없다 — 편집 화면을 열지 않는다(신규 생성은 로비 덱 탭).");

            return;
        }

        if (matchPanel != null) matchPanel.SetActive(false);
        if (editPanel  != null) editPanel.SetActive(true);

        // 리스트를 먼저 세운 뒤 편집기를 연다 — 편집기가 여는 순간의 선택 표시가 이미 맞아 있어야
        // "잠깐 다른 칸이 선택돼 보이는" 한 프레임이 생기지 않는다.
        if (strip          != null) strip.Build(SelectedSlot, OnStripSlotClicked);
        if (editController != null) editController.Open(SelectedSlot);
    }

    // 선택된 덱을 씬 전환 캐리어에 싣는다. Confirm이 게이트를 열기 직전에 부르는 유일한 지점이다.
    public bool TryConfirmSelection()
    {
        if (!IsValidSlot(SelectedSlot)) return false;

        DeckConfig.Set(DeckSaveManager.GetSlot(SelectedSlot));

        return true;
    }

    // 가로 덱 리스트 클릭. 편집 화면에 머문 채 대상 슬롯만 갈아탄다.
    // 저장 여부(6/6이면 조용히 저장, 미만이면 폐기)는 SwitchTo가 판정한다 — 셸은 관여하지 않는다.
    void OnStripSlotClicked(int _slotIndex)
    {
        if (_slotIndex == SelectedSlot) return;

        // 전환이 거부되면(저장 실패 등) 선택 상태를 옮기지 않는다 —
        // 셸의 SelectedSlot과 컨트롤러의 편집 대상이 어긋나면 복귀 후 엉뚱한 덱이 그려진다.
        if (editController != null && !editController.SwitchTo(_slotIndex)) return;

        SelectedSlot = _slotIndex;

        if (strip != null) strip.SetSelected(_slotIndex);
    }

    void OnEditBackClicked()
    {
        // 저장·미완성 확인은 컨트롤러가 하고, 실제 복귀는 아래 OnEditorExit로 되돌아온다.
        if (editController != null) editController.RequestExit();
    }

    // DeckEditController에 주입한 종료 훅. 로비의 DeckTabController.CloseEditor 자리다.
    void OnEditorExit()
    {
        if (editController != null) editController.Close();
        if (strip          != null) strip.Clear();

        // 편집분이 저장됐을 수 있으므로 세이브에서 다시 읽어 그린다(편집기가 준 값을 받아쓰지 않는다).
        ShowMatchPanel();
    }

    // 편집 패널을 반드시 함께 내린다 — 진입(Open)이 편집 도중 닫힌 상태를 물려받으면 두 패널이 겹친 채 뜬다.
    void ShowMatchPanel()
    {
        if (editPanel  != null) editPanel.SetActive(false);
        if (matchPanel != null) matchPanel.SetActive(true);
        if (panelView  != null) panelView.Render(SelectedSlot);
    }

    // 표시할 슬롯 결정. 요청값 → 직전 선택 유지 → 첫 유효 슬롯 → 없음(-1).
    // 직전 선택을 한 단계 끼워 넣는 이유: 편집에서 돌아왔을 때 사용자가 마지막에 보던 덱이 그대로 남아야 한다.
    int ResolveSlot(int _requested)
    {
        if (IsValidSlot(_requested))   return _requested;
        if (IsValidSlot(SelectedSlot)) return SelectedSlot;

        for (int t_i = 0; t_i < DeckSaveManager.SLOT_COUNT; t_i++)
            if (DeckSaveManager.IsSlotValid(t_i)) return t_i;

        return -1;
    }

    // DeckSaveManager.IsSlotValid는 범위 가드 없이 슬롯 배열을 직접 인덱싱한다 —
    // 이 셸은 "선택 없음"을 -1로 표현하므로 범위 검사를 반드시 앞에 둬야 한다.
    static bool IsValidSlot(int _slotIndex)
        => _slotIndex >= 0 && _slotIndex < DeckSaveManager.SLOT_COUNT && DeckSaveManager.IsSlotValid(_slotIndex);

    // 진입 배선(PlayBtn 등)이 아직 없어서 Play 중 인스펙터에서 직접 여는 검증 창구다.
    [ContextMenu("Open (디버그)")]
    void DebugOpen()
    {
        Open();
    }
}
