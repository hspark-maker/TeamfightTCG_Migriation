using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

// 로비 PlayBtn이 여는 출전 덱 확인/편집 오버레이의 셸(MatchDeckRoot에 부착). 로비 DeckTabController의 매치판.
//
// 전투 시작은 이 화면이 결정한다 — 호스트(LobbyMatchLauncher)가 배틀 씬을 로드하기 전에 RunSelectionAsync를
// await하고, "시작"이 눌려야 통과한다. 배틀 씬의 필드 초기화가 DeckConfig.PlayerDeck을 소비하므로 확정이 씬 로드보다 앞이어야 한다.
//
// 아는 것은 "어느 저장 슬롯이 선택됐는가"와 "두 패널 중 무엇을 보이는가"뿐 — 편성·저장은 전부 DeckEditController에 위임한다.
// 선택을 DeckConfig가 아니라 슬롯 인덱스로 드는 이유: DeckConfig는 직렬화 없는 씬 캐리어라 "어느 슬롯"을 표현하지 못한다.
public class MatchDeckShell : MonoBehaviour
{
    [SerializeField] GameObject matchPanel;   // MatchDeckPanel 인스턴스

    [Header("컨트롤러")]
    [SerializeField] MatchDeckPanelView panelView;

    // 편집 화면은 이 프리팹 안에 없다 — 로비 덱 탭과 같은 한 인스턴스를 풀에서 받아 쓴다.
    // 가로 덱 리스트·뒤로가기 버튼도 그 프리팹 안에 있고, 켜고 끄는 축은 DeckEditData가 실어 보낸다.
    bool m_editing;

    // 현재 선택된 저장 슬롯. 유효한 덱이 하나도 없으면 -1.
    public int SelectedSlot { get; private set; } = -1;

    // 게이트 결과. Pending인 동안 호스트가 전투 시작을 붙잡고 있다.
    enum EGate { Pending, Confirmed, Cancelled }

    EGate m_gate = EGate.Pending;

    // 게이트가 열려 있는가. 화면이 켜졌는지로 판정하지 않는 이유는 전환이 이 화면을 게이트보다 "먼저" 세우기 때문이다
    // (PrepareForHandoff) — 켜짐을 진행 중으로 읽으면 정작 진짜 진입이 중복으로 걸려 그대로 포기 처리된다.
    bool m_selecting;

    // 전환이 이미 세워 둔 화면인가. 다시 열면 등장 안무가 세운 알파·배율이 저작값으로 되돌아간다.
    bool m_prepared;

    // 편집 화면이 떠 있으면 내린다. 저장 판정은 편집기의 뒤로가기(RequestLeave)가 이미 거쳤다고 본다 —
    // 여기서 부르는 경로는 전부 "이미 나가기로 결정된" 뒤다.
    void HideEditorIfOpen()
    {
        if (!m_editing) return;   // 연 적이 없으면 풀에 묻지 않는다(GetUI가 "No Such UI" 로그를 남긴다)

        m_editing = false;
        DeckEditController.HidePooled();
    }

    // 전투 시작 게이트. 호스트(LobbyMatchLauncher)가 씬을 로드하기 "전에" 이걸 await 하고,
    // true를 받으면 DeckConfig.PlayerDeck이 확정된 상태로 배틀 씬으로 넘어간다.
    // false = 유저가 전투를 포기했다(또는 씬이 내려갔다) → 호스트가 복귀를 처리한다.
    public async UniTask<bool> RunSelectionAsync(CancellationToken _ct)
    {
        // 이미 진행 중인데 다시 부르면 선택 상태를 덮어쓰고, Confirm 한 번에 두 await가 동시에 깨어난다.
        if (m_selecting)
        {
            Debug.LogWarning("[MatchDeckShell] 선택이 이미 진행 중이다 — 중복 진입을 무시한다.");

            return false;
        }

        m_selecting = true;
        m_gate      = EGate.Pending;

        // 전환이 이미 세워 둔 화면이면 다시 열지 않는다 — 등장 안무가 감춰 둔 칸이 저작값으로 되살아난다.
        if (!m_prepared) Open();
        m_prepared = false;

        // 씬 파괴로 취소되면 Confirm/Cancel 어느 쪽도 오지 않는다 — 예외 대신 취소 여부를 값으로 받는다.
        bool t_canceled = await UniTask.WaitUntil(() => m_gate != EGate.Pending, cancellationToken: _ct)
                                       .SuppressCancellationThrow();

        m_selecting = false;

        // 씬이 내려가는 중이다 — 파괴될 오브젝트를 건드리지 않는다.
        if (t_canceled) return false;

        bool t_confirmed = m_gate == EGate.Confirmed;

        // 내리는 것은 포기일 때뿐이다. 이 화면은 로비 위 오버레이라 내리는 즉시 로비가 드러나는데,
        // 전투 시작은 호스트가 곧바로 씬을 로드한다 — 여기서 내리면 그 사이가 로비로 번쩍인다.
        // 화면을 덮어 가는 것은 CurtainView이고, 커튼이 닫히는 동안(하드컷의 한 프레임이 아니다)
        // 이 화면이 깔려 있어야 한다 — 커튼은 이 화면의 색으로 만들어져 있어 그래야 접히는 것처럼 보인다.
        if (!t_confirmed) Close();

        return t_confirmed;
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

    /// <summary>
    /// 매칭 화면 밑에 이 화면을 미리 세운다. 게이트(RunSelectionAsync)보다 앞서는 유일한 진입이다 —
    /// 전환이 옮겨 앉힐 자리를 읽으려면 레이아웃이 이미 계산돼 있어야 한다.
    /// </summary>
    public MatchHandoffTargets PrepareForHandoff()
    {
        Open();
        m_prepared = true;

        return panelView != null ? panelView.BuildHandoffTargets() : default;
    }

    // 덱 화면 진입. 게이트를 쓰지 않고 직접 열 때(디버그·후속 진입점)의 창구다.
    // _slotIndex가 음수면 이전 선택 → 첫 유효 슬롯 순으로 알아서 고른다.
    public void Open(int _slotIndex = -1)
    {
        // 루트를 켜기 전에 편집 화면을 내린다 — 편집 화면은 풀 캔버스에 살아서 이 루트를 꺼도 같이 꺼지지 않는다.
        HideEditorIfOpen();

        gameObject.SetActive(true);

        SelectedSlot = ResolveSlot(_slotIndex);

        // 상대 덱은 여기서 뽑지 않는다 — 호스트(LobbyMatchLauncher.ConfirmOpponent)가 이 화면을 열기 전에
        // 확정해 DeckConfig에 실어둔다. 이 화면이 다시 뽑으면 확정 지점이 두 곳이 되고,
        // 전투가 소비하는 값과 화면에 그린 값이 갈린다. 뷰는 캐리어를 읽기만 한다.
        ShowMatchPanel();
    }

    public void Close()
    {
        // 편집 중 닫히는 경로는 없지만(편집은 뒤로가기로만 나간다), 편집 화면은 풀 캔버스에 살아서
        // 이 루트를 꺼도 같이 꺼지지 않는다 — 남으면 로비 위에 편집 화면만 떠 있는 상태가 된다.
        // 내리면 DeckEditController.OnDisable이 편집 상태를 무저장 폐기한다 — 그게 이 화면의 사양이다.
        HideEditorIfOpen();

        gameObject.SetActive(false);
    }

    // 매치 패널 EditButton. 편집 대상은 지금 선택된 덱이다.
    public void OpenEditor()
    {
        SelectedSlot = ResolveSlot(SelectedSlot);

        // 매치 화면은 신규 덱 생성을 지원하지 않는다(가로 리스트에 + 칸이 없다) → 편집할 원본이 없으면 열지 않는다.
        if (SelectedSlot < 0)
        {
            Debug.LogWarning("[MatchDeckShell] 저장된 유효 덱이 없다 — 편집 화면을 열지 않는다(신규 생성은 로비 덱 탭).");

            return;
        }

        // 가로 리스트와 뒤로가기는 편집 화면이 자기 안에서 세운다 — 셸은 "무엇을 켤지"만 실어 보낸다.
        // 리스트 선택 표시는 편집기가 여는 순간 이미 맞아 있다(같은 데이터로 한 번에 세우므로
        // "잠깐 다른 칸이 선택돼 보이는" 한 프레임이 없다).
        DeckEditController t_editor = DeckEditController.OpenPooled(new DeckEditData
        {
            slotIndex        = SelectedSlot,
            onExit           = OnEditorExit,
            showDeckStrip    = true,
            showTitle        = false,   // 그 자리를 가로 덱 리스트가 쓴다
            showDeckPower    = false,   // 리스트 칸에 이미 나와 있다
            tutorialDeckSlot = TutorialDeckSlot(),
            onSlotSwitched   = _slot => SelectedSlot = _slot,
            onPlay           = OnEditorPlay,   // 편집 화면에서도 바로 전투로 갈 수 있어야 한다
        });

        // 못 세우면 매치 패널에 머문다 — 빈 화면으로 갇히면 전투 시작 게이트가 통째로 막힌다.
        if (t_editor == null) return;

        m_editing = true;
        if (matchPanel != null) matchPanel.SetActive(false);
    }

    // 선택된 덱을 씬 전환 캐리어에 싣는다. Confirm이 게이트를 열기 직전에 부르는 유일한 지점이다.
    public bool TryConfirmSelection()
    {
        if (!IsValidSlot(SelectedSlot)) return false;

        DeckConfig.Set(DeckSaveManager.GetSlot(SelectedSlot));

        return true;
    }

    // 편집 화면의 전투 시작. 저장·미완성 확인은 편집기의 RequestLeave가 이미 거쳤다 —
    // 여기 도착했다는 것은 세이브가 확정됐다는 뜻이고, Confirm은 그 세이브를 읽는다.
    //
    // 편집 화면을 반드시 먼저 내린다: 풀 캔버스(DontDestroyOnLoad)에 살아서 배틀 씬으로 넘어가도
    // 그대로 떠 있는다. 매치 패널을 되살리는 것은 커튼 때문이다 — 커튼이 이 화면의 색으로 접히므로
    // 씬이 로드되는 동안 그 밑에 깔려 있어야 한다.
    void OnEditorPlay()
    {
        HideEditorIfOpen();
        ShowMatchPanel();

        // 유효 덱이 아니면 Confirm이 게이트를 열지 않는다 — 그때는 매치 패널에 머문다.
        Confirm();
    }

    // DeckEditData에 실어 보낸 종료 훅. 로비의 DeckTabController.CloseEditor 자리다.
    // 저장·미완성 확인은 편집 화면의 뒤로가기가 이미 거쳤다(RequestLeave 한 곳뿐이다).
    void OnEditorExit()
    {
        HideEditorIfOpen();

        // 편집분이 저장됐을 수 있으므로 세이브에서 다시 읽어 그린다(편집기가 준 값을 받아쓰지 않는다).
        ShowMatchPanel();
    }

    // 편집 화면을 반드시 함께 내린다 — 풀 캔버스에 살아서 이 루트를 꺼도 같이 꺼지지 않는다.
    void ShowMatchPanel()
    {
        HideEditorIfOpen();
        if (matchPanel != null) matchPanel.SetActive(true);

        if (panelView == null) return;

        panelView.Render(SelectedSlot);

        // 전환을 타고 들어온 직전 표시가 칸을 감춘 채 끝났을 수 있다 — 전환을 타지 않는 경로는 반드시 여기서 되돌린다.
        panelView.ResetIntro();
    }

    // 표시할 슬롯 결정. 요청값 → 직전 선택 유지 → (튜토리얼 덱을 뺀) 첫 유효 슬롯 → 없음(-1).
    // 직전 선택을 한 단계 끼워 넣는 이유: 편집에서 돌아왔을 때 사용자가 마지막에 보던 덱이 그대로 남아야 한다.
    int ResolveSlot(int _requested)
    {
        if (IsValidSlot(_requested))   return _requested;
        if (IsValidSlot(SelectedSlot)) return SelectedSlot;

        // 튜토리얼 덱은 방금 지급돼 목록 맨 앞(=첫 유효 슬롯)에 있다. 그대로 고르면 이미 선택된 채로 떠서
        // "목록에서 골라 쓴다"를 가르칠 수 없다 → 다른 덱을 초기 선택으로 두고 유저가 옮겨오게 한다.
        int t_tutorial = TutorialDeckSlot();

        for (int t_i = 0; t_i < DeckSaveManager.SLOT_COUNT; t_i++)
            if (t_i != t_tutorial && DeckSaveManager.IsSlotValid(t_i)) return t_i;

        // 튜토리얼 덱이 유일한 덱이면 그거라도 고른다 — 선택 없음(-1)은 전투 시작 버튼 자체를 잠근다.
        return t_tutorial;
    }

    // 이번 튜토리얼 전투가 쓸 덱의 저장 슬롯. 튜토리얼이 아니거나 목록에 없으면 -1.
    // 전투 덱 정본은 TutorialConfig(시나리오)이고 DeckGrant 스텝이 같은 구성을 세이브에 넣어둔다 —
    // 좌표를 여기서 되찾는 이유는 세이브 삽입이 항상 맨 앞이라 스텝이 좌표를 알려줄 수 없기 때문.
    static int TutorialDeckSlot()
    {
        if (!TutorialConfig.IsActive) return -1;

        return DeckSaveManager.TryFindSlot(TutorialConfig.PlayerDeck, out int t_index) ? t_index : -1;
    }

    // DeckSaveManager.IsSlotValid는 범위 가드 없이 슬롯 배열을 직접 인덱싱한다 —
    // 이 셸은 "선택 없음"을 -1로 표현하므로 범위 검사를 반드시 앞에 둬야 한다.
    static bool IsValidSlot(int _slotIndex)
        => _slotIndex >= 0 && _slotIndex < DeckSaveManager.SLOT_COUNT && DeckSaveManager.IsSlotValid(_slotIndex);

    // PlayBtn을 거치지 않고 Play 중 인스펙터에서 화면만 열어보는 검증 창구다.
    [ContextMenu("Open (디버그)")]
    void DebugOpen()
    {
        Open();
    }
}
