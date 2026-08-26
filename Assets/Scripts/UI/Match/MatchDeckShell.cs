using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

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

    bool m_editing;

    // 전투 시작 응답 한 박이 도는 중인가. 커튼이 걸린 뒤에도 되돌리지 않는다 — 이 화면 밑에서 씬이 갈리기 때문이다.
    // 되돌리는 곳은 진입(Open) 한 곳뿐이라, 전투로 닫히지 않은 화면이 다시 열릴 때만 풀린다.
    bool m_launching;

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
    //
    // 게이트를 바로 열지 않고 응답 한 박을 먼저 태운다. 호스트는 게이트가 열리는 즉시 커튼을 걸므로
    // (LobbyMatchLauncher.EnterBattle) 여기서 바로 열면 클릭과 커튼 사이가 0프레임이다 —
    // 매칭에서 이 화면으로 넘어오는 길은 그렇게 이어 놓고 나가는 길만 하드컷이면 흐름이 끝에서 끊긴다.
    public void Confirm()
    {
        // 한 박이 도는 동안 다시 눌리면 안무가 두 벌 겹치고 게이트가 두 번 열린다.
        if (m_launching) return;

        if (!TryConfirmSelection())
        {
            Debug.LogWarning("[MatchDeckShell] 유효한 덱이 선택되지 않았다 — 전투를 시작하지 않는다.");

            return;
        }

        m_launching = true;

        // 안내가 시킨 순서를 거치지 않고 이 화면을 떠나는 길이 있다(편집 화면의 전투 버튼) —
        // 좌표를 두고 가면 앵커가 사라진 화면으로 돌아와 영영 대기한다. 전투 스텝에 이미 서 있으면 무시된다.
        OutgameTutorialRunner.NotifyDeckGateBattleLaunched();

        // 뷰가 없으면 태울 안무도 없다 — 연출 때문에 전투가 시작되지 않는 길을 만들지 않는다.
        if (panelView == null)
        {
            m_gate = EGate.Confirmed;

            return;
        }

        panelView.PlayLaunch(() => m_gate = EGate.Confirmed);
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
        // 루트를 켜기 전에 편집 패널을 내린다 — 비활성 부모 아래에선 OnEnable이 돌지 않으므로,
        // 편집 패널의 튜토리얼 앵커(로비 덱 편집과 키를 공유한다)가 켜졌다 꺼지며 로비 쪽 등록을 지우는 일이 없다.
        HideEditorIfOpen();
        gameObject.SetActive(true);

        // 전투로 닫히지 않은 화면이 다시 열린다 — 지난번 응답 한 박의 가드를 물려받으면 전투 시작이 영영 안 눌린다.
        m_launching = false;

        SelectedSlot = ResolveSlot(_slotIndex);

        // 상대 덱은 여기서 뽑지 않는다 — 호스트(LobbyMatchLauncher.ConfirmOpponent)가 이 화면을 열기 전에
        // 확정해 DeckConfig에 실어둔다. 이 화면이 다시 뽑으면 확정 지점이 두 곳이 되고,
        // 전투가 소비하는 값과 화면에 그린 값이 갈린다. 뷰는 캐리어를 읽기만 한다.
        ShowMatchPanel();
    }

    public void Close()
    {
        // 편집 중 닫히는 경로는 없지만(편집은 뒤로가기로만 나간다), 패널이 켜진 채 루트가 꺼지면
        // DeckEditController.OnDisable이 편집 상태를 무저장 폐기한다 — 그게 이 화면의 사양이다.
        HideEditorIfOpen();
        gameObject.SetActive(false);
    }

    // 매치 패널 EditButton. 편집 대상은 지금 선택된 덱이다.
    // 튜토리얼이 카드 한 장을 직접 끼우게 하는 구간이면 그 카드를 빼 둔 채 연다(빈 칸이 있어야 가르칠 것이 생긴다).
    public void OpenEditor()
    {
        SelectedSlot = ResolveSlot(SelectedSlot);

        // 매치 화면은 신규 덱 생성을 지원하지 않는다(가로 리스트에 + 칸이 없다) → 편집할 원본이 없으면 열지 않는다.
        if (SelectedSlot < 0)
        {
            Debug.LogWarning("[MatchDeckShell] 저장된 유효 덱이 없다 — 편집 화면을 열지 않는다(신규 생성은 로비 덱 탭).");

            return;
        }

        DeckEditController t_editor = DeckEditController.OpenPooled(new DeckEditData
        {
            slotIndex = SelectedSlot,
            onExit = OnEditorExit,
            showDeckPower = false,
            holdoutCard = OutgameTutorialRunner.TryGetPendingEquipCard(out var t_equip) ? t_equip : 0,
            onPlay = OnEditorPlay,
        });
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

    void HideEditorIfOpen()
    {
        if (!m_editing) return;

        m_editing = false;
        DeckEditController.HidePooled();
    }

    void OnDisable()
    {
        HideEditorIfOpen();
    }

    void OnEditorPlay()
    {
        HideEditorIfOpen();
        ShowMatchPanel();
        Confirm();
    }

    // DeckEditController에 주입한 종료 훅. 로비의 DeckTabController.CloseEditor 자리다.
    void OnEditorExit()
    {
        HideEditorIfOpen();
        ShowMatchPanel();
    }

    // 편집 패널을 반드시 함께 내린다 — 진입(Open)이 편집 도중 닫힌 상태를 물려받으면 두 패널이 겹친 채 뜬다.
    void ShowMatchPanel()
    {
        HideEditorIfOpen();
        if (matchPanel != null) matchPanel.SetActive(true);

        if (panelView == null) return;

        // 편집이 슬롯을 비우거나 6장 미만으로 만들고 나올 수 있다(onSlotSwitched는 받은 값을 검사하지 않는다).
        // 그대로 그리면 전투 시작 버튼이 잠긴 채 남고, 그 버튼을 가리키는 튜토리얼 안내는 풀릴 길 없이 대기한다.
        SelectedSlot = ResolveSlot(SelectedSlot);

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

        // 튜토리얼 중이면 이번 전투가 쓸 덱이 곧 기본 선택이다. 예전에는 일부러 다른 덱을 골라 두고
        // 유저가 가로 덱 리스트에서 옮겨오게 했지만, 그 리스트가 사라져 옮겨올 수단 자체가 없다 —
        // 엉뚱한 덱을 연 채로 두면 안내가 "이 카드를 끼워라"라고 가리키는 덱과 편집 중인 덱이 갈린다.
        int t_tutorial = TutorialDeckSlot();
        if (IsValidSlot(t_tutorial)) return t_tutorial;

        for (int t_i = 0; t_i < DeckSaveManager.SLOT_COUNT; t_i++)
            if (DeckSaveManager.IsSlotValid(t_i)) return t_i;

        return -1;
    }

    // 이번 튜토리얼 전투가 쓸 덱의 저장 슬롯. 튜토리얼이 아니거나 고를 덱이 하나도 없으면 -1.
    // 전투 덱 정본은 TutorialConfig(시나리오)이고 DeckGrant 스텝이 같은 구성을 세이브에 넣어둔다 —
    // 좌표를 여기서 되찾는 이유는 세이브 삽입이 항상 맨 앞이라 스텝이 좌표를 알려줄 수 없기 때문.
    static int TutorialDeckSlot()
    {
        if (!TutorialConfig.IsActive) return -1;

        var t_cards = TutorialConfig.PlayerDeck;
        if (DeckSaveManager.TryFindSlot(t_cards, out int t_index)) return t_index;

        // 앞선 DeckGrant 스텝이 건너뛰어져 그 덱이 세이브에 없다 — 첫 유효 슬롯으로 떨어뜨려 화면을 세운다
        // (지목 실패 시 화면이 대신 고르는 AlbumTabController.FindAnchorThemeIndex와 같은 관용구).
        for (int t_i = 0; t_i < DeckSaveManager.SLOT_COUNT; t_i++)
            if (DeckSaveManager.IsSlotValid(t_i)) return t_i;

        return -1;
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
