using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// 로비 PlayBtn → 출전 덱 확정 → AI 대전 진입.
/// 전투가 소비하는 DeckConfig.PlayerDeck을 채우는 지점은 이 진입점이 여는 덱 화면(MatchDeckShell) 하나뿐이다.
/// 배틀 씬은 확정된 값을 읽기만 한다 — 확정 지점이 씬을 넘어 둘로 갈리지 않게.
public class LobbyMatchLauncher : MonoBehaviour
{
    [SerializeField] LobbyOverlayHost overlayHost;
    [SerializeField] AIDeckConfig   aiDeckConfig;   // BattleScene GameInitializer가 참조하는 것과 동일 에셋

    [Header("매칭 연출")]
    [SerializeField] MatchmakingShell    matchShellPrefab;   // 미배선이면 매칭 없이 구 동작
    [SerializeField] OpponentProfilePool profilePool;

    [Header("유효 덱 없음 안내")]
    [SerializeField] LobbyTabController lobbyTabController;
    [SerializeField] LobbyTabPanel deckPanel;

    [Header("기능 잠금")]
    [Tooltip("전투 진입 버튼(PlayBtn). 이 컴포넌트가 LobbyPlay 해금 여부로 interactable을 소유한다.\n" +
             "미배선이면 잠김이 화면에 안 드러난다 — 진입 차단 자체는 StartAiBattle이 따로 막는다.")]
    [SerializeField] LobbyMatchTabPanel matchPanel;

    [Header("보상 토너먼트")]
    [Tooltip("토너먼트 맵 오버레이. 여닫음은 맵이 스스로 갖고, 여는 계기·전투 진입만 로비 쪽이 쥔다 — 맵이 컨트롤러·런처를 인스펙터로 물면 그 배선이 탭 프리팹 오버라이드로 남는다.")]
    [SerializeField] TournamentMapOverlayView tournamentPanel;

    const string BATTLE_SCENE = "BattleScene";

    // 게이트가 열려 있는 동안 PlayBtn 재클릭을 막는다 — 두 번째 진입이 셸의 선택 상태를 덮고,
    // Confirm 한 번에 두 await가 동시에 깨어 LoadScene이 두 번 돈다.
    bool m_running;

    IMatchmaker      m_matchmaker;
    MatchmakingShell m_matchShell;
    LobbyOverlayHost m_overlayHost;

    /// <summary>
    /// 오버레이 호스트. **인스펙터가 프리팹 에셋을 물고 있으면 쓰지 않는다.**
    ///
    /// 에셋을 물면 화면에 없는 원본을 조작하게 된다 — 매치 덱 화면이 열리지 않고, 에디터에서는 그 조작이
    /// 프리팹 파일에 그대로 기록된다(자식을 지우는 순간 "Destroying assets is not permitted"로 터진다).
    /// 프리팹 에셋은 씬에 속하지 않으므로 gameObject.scene.IsValid()로 구분할 수 있다.
    /// </summary>
    LobbyOverlayHost OverlayHost
    {
        get
        {
            if (m_overlayHost != null) return m_overlayHost;

            if (overlayHost != null && overlayHost.gameObject.scene.IsValid())
                return m_overlayHost = overlayHost;

            if (overlayHost != null)
                Debug.LogError(
                    "[LobbyMatchLauncher] overlayHost에 프리팹 에셋이 물려 있다 — 인스펙터에서 씬 인스턴스로 다시 배선할 것. "
                  + "이번 실행은 계층에서 찾아 진행한다.", this);

            return m_overlayHost = transform.root.GetComponentInChildren<LobbyOverlayHost>(true);
        }
    }

    MatchDeckShell DeckShell => OverlayHost != null ? OverlayHost.MatchDeckShell : null;

    // 페이크 → 실제 Photon 매칭 교체는 이 한 줄이 전부다.
    IMatchmaker Matchmaker => m_matchmaker ??= new FakeMatchmaker(aiDeckConfig, profilePool);

    // 로비 캔버스에 미리 얹지 않고 첫 매칭 때 띄운다 — 로비 프리팹을 저장할 때마다 SafeArea가
    // 런타임 계산값으로 굳어(anchorMax) 관계없는 좌표가 함께 커밋된다. 부모는 덱 화면과 같은 SafeArea다.
    MatchmakingShell MatchShell
    {
        get
        {
            if (m_matchShell == null && matchShellPrefab != null)
                m_matchShell = Instantiate(matchShellPrefab, transform.parent);

            return m_matchShell;
        }
    }

    // 튜토리얼 전투는 상대가 시나리오 고정이라 매칭을 태우지 않는다 —
    // 마지막 튜토 전투가 끝나며 TutorialConfig가 꺼지고, 그 다음 판부터 이 문이 열린다.
    bool UseMatchmaking => !TutorialConfig.IsActive && matchShellPrefab != null;

    void OnEnable()
    {
        if (matchPanel != null)
        {
            matchPanel.PlayRequested += StartAiBattle;
            matchPanel.TournamentRequested += OpenTournamentMap;
        }

        if (tournamentPanel != null) tournamentPanel.NodeSelected += StartTournamentBattle;

        TournamentReturnFlow.ReturnRequested += HandleTournamentReturn;
        TournamentReturnFlow.GiftRevealRequested += HandleGiftReveal;

        OutgameFeatureLock.OnChanged += ApplyPlayLock;
        ApplyPlayLock();
    }

    void OnDisable()
    {
        if (matchPanel != null)
        {
            matchPanel.PlayRequested -= StartAiBattle;
            matchPanel.TournamentRequested -= OpenTournamentMap;
        }

        if (tournamentPanel != null) tournamentPanel.NodeSelected -= StartTournamentBattle;

        TournamentReturnFlow.ReturnRequested -= HandleTournamentReturn;
        TournamentReturnFlow.GiftRevealRequested -= HandleGiftReveal;

        OutgameFeatureLock.OnChanged -= ApplyPlayLock;
    }

    // 로비가 서는 즉시 떠났던 화면을 되돌린다. 골드 흡입을 기다리지 않는다 — 기다리면 로비가 한참 드러난다.
    // 한 프레임 미루는 것은 레이아웃 때문이다: rect가 0인 프레임에 맵을 열면 스크롤 계산이 깨져 정점이 바닥에 뭉친다.
    System.Collections.IEnumerator Start()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();

        TournamentReturnFlow.Restore();
    }

    public void StartAiBattle()
    {
        if (m_running) return;

        // 버튼을 죽여 두는 것만으로는 부족하다 — 잠김 표시는 표현 레이어 몫이고, 진입을 실제로 막는 주체는 여기다.
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.LobbyPlay)) return;

        DeckConfig.SetMultiplayer(false);

        // 덱 화면을 거치지 않는 튜토리얼 챕터. 저장된 덱이 아직 없으므로 유효 덱 검사보다 반드시 앞이다.
        if (TutorialConfig.IsActive && !TutorialConfig.ShowDeckGate)
        {
            EnterBattle();
            return;
        }

        // 셸이 세이브 슬롯 좌표로 동작하므로 판정도 세이브 기준이다(DeckConfig는 아직 비어 있어도 된다).
        if (!DeckSaveManager.HasAnyValidSlot())
        {
            ShowNoDeckPopup();
            return;
        }

        RunEntryAsync().Forget();
    }

    /// <summary>보상 토너먼트 정점 도전. 상대·덱·AI 레벨이 저작 고정이라 매칭을 태우지 않는다.
    /// TournamentRun.Begin은 모든 가드를 통과한 뒤에 온다 — 중간에 return하며 세워 두면 그게 곧 로비 누수다.</summary>
    public void StartTournamentBattle(int _nodeIndex)
    {
        if (m_running) return;

        if (!TournamentProgress.CanEnter(_nodeIndex)) return;
        if (!TournamentProgress.TryGetNode(_nodeIndex, out TournamentNodeDef t_node)) return;

        // 저작 덱이 비면 상대 없이 전투가 뜬다(DeckConfig.SetEnemyDeck은 null도 못 받는다) — 진입 단계에서 막는다.
        if (t_node.enemyDeck == null || t_node.enemyDeck.Count == 0)
        {
            Debug.LogWarning($"[LobbyMatchLauncher] 토너먼트 정점 '{t_node.nodeId}'에 상대 덱이 없어 진입을 막는다 — 저작 검증 필요.");
            return;
        }

        DeckConfig.SetMultiplayer(false);

        if (!DeckSaveManager.HasAnyValidSlot())
        {
            ShowNoDeckPopup();
            return;
        }

        if (!TournamentRun.Begin(t_node.nodeId, t_node.AiCardLevelOrBase)) return;

        var t_preset = new MatchOpponent(
            MatchProfile.OfTournamentNode(t_node.displayName, t_node.avatar), t_node.enemyDeck);

        RunEntryAsync(t_preset).Forget();
    }

    // 로비에서 전투로 넘어가는 유일한 문. 세 진입 경로가 여기로 모인다 — 전환 연출을 갈아끼울 때 손댈 자리가 하나여야 한다.
    //
    // m_running을 되돌리지 않는 이유: 커튼이 도는 동안 로비는 그대로 살아 있다. 하드컷 시절엔 그 창이 한 프레임이라
    // 무시할 만했지만, 이제는 그 사이 PlayBtn 재클릭이 덱 화면을 커튼 밑에서 다시 연다(RunEntryAsync의 finally가
    // 이 지점보다 먼저 m_running을 내린다). 로비는 곧 파괴되므로 다시 세운 채 두면 된다.
    void EnterBattle()
    {
        m_running = true;
        CurtainView.LoadScene(BATTLE_SCENE);
    }

    // 진입 체인이 "전투 시작"으로 닫히면 그때 씬을 로드한다. 포기면 각 화면이 스스로 닫고 로비가 그대로 남는다.
    async UniTaskVoid RunEntryAsync(MatchOpponent? _preset = null)
    {
        var t_ct = this.GetCancellationTokenOnDestroy();

        // 전투로 닫히지 않은 모든 끝(포기·취소·예외)에서 토너먼트 플래그를 끊는다. 그 경로엔 씬 전환이 없어
        // TurnRunner.Cleanup이 영영 돌지 않는다 — 남겨 두면 다음 일반 전투의 AI 레벨이 정점 레벨로 굳고
        // 랭크 정산이 통째로 스킵된다. m_running과 같은 finally에 두는 이유도 같다(체인이 던져도 새지 않게).
        bool t_confirmed = false;
        m_running = true;
        try
        {
            t_confirmed = await RunEntryChainAsync(t_ct, _preset);
        }
        finally
        {
            m_running = false;
            if (!t_confirmed) TournamentRun.End();
        }

        // 씬이 내려가며 취소된 경우 — 파괴 중인 오브젝트를 건드리지 않는다.
        if (t_ct.IsCancellationRequested) return;

        if (t_confirmed) EnterBattle();
    }

    // 매칭 연출 → 상대 확정 → 출전 덱 확정. 어느 단계든 포기하면 false로 빠져 로비가 그대로 남는다.
    async UniTask<bool> RunEntryChainAsync(CancellationToken _ct, MatchOpponent? _preset = null)
    {
        // 고정 상대(토너먼트 정점)는 뽑을 것이 없다 — 매칭 단계를 통째로 건너뛴다.
        MatchOpponent? t_opponent = _preset;
        if (!_preset.HasValue && UseMatchmaking)
        {
            t_opponent = await MatchShell.RunMatchAsync(Matchmaker, _ct);
            if (t_opponent == null) return false;   // 취소 = 로비로 되돌아간다
        }

        ConfirmOpponent(t_opponent, _preset.HasValue);

        if (DeckShell == null)
        {
            Debug.LogWarning("[LobbyMatchLauncher] 덱 화면 미배선 — 첫 유효 덱으로 전투에 진입한다.");

            // 넘어갈 화면이 없으니 매칭 화면은 여기서 스스로 내려간다(전환이 내려 줄 기회가 없다).
            m_matchShell?.Close();

            return TryApplyFirstValidDeck();
        }

        // 매칭을 거치지 않은 경로(튜토리얼·토너먼트)는 옮겨 앉힐 이전 화면이 없다 — 덱 화면이 곧장 뜬다.
        if (_preset.HasValue || t_opponent == null) return await DeckShell.RunSelectionAsync(_ct);

        return await RunSelectionWithHandoffAsync(_ct);
    }

    // 매칭 화면 → 덱 화면. 덱 화면을 매칭 화면 "밑에" 먼저 세운 뒤, 매칭의 세 부품(내 카드·상대 카드·VS)이
    // 새 화면의 제자리로 옮겨 앉는다. 커튼으로 덮지 않는 이유는 두 화면의 축이 이미 같기 때문이다 —
    // 가리면 같은 무대라는 사실이 오히려 지워진다(자세한 규약은 MatchHandoffFx 참고).
    async UniTask<bool> RunSelectionWithHandoffAsync(CancellationToken _ct)
    {
        MatchHandoffTargets t_targets = DeckShell.PrepareForHandoff();

        // 선택 게이트는 전환이 도는 동안 시작해 첫 대기에서 멈춘다 — 전환이 끝난 프레임엔 이미 서 있어야 한다.
        UniTask<bool> t_selection = DeckShell.RunSelectionAsync(_ct);

        await m_matchShell.PlayHandoffAsync(t_targets, _ct);

        return await t_selection;
    }

    // 상대를 전투 전에 확정한다 — 덱 화면의 EnemySection과 실제 전투가 같은 값을 보게 하는 유일한 지점.
    // 튜토리얼은 전투가 TutorialConfig.EnemyDeck으로 초기화되므로(GameInitializer) 여기서 랜덤을 뽑으면
    // 화면에 그린 6장이 실제 상대와 달라진다 — "상대 덱을 미리 확인한다"는 안내가 거짓이 된다.
    void ConfirmOpponent(MatchOpponent? _matched, bool _preset = false)
    {
        // 고정 상대는 저작값이 곧 진실이다 — 어떤 폴백도 태우지 않는다(태우면 맵에 그린 정점과 실제 상대가 갈린다).
        if (_preset && _matched.HasValue)
        {
            MatchOpponentHandoff.Set(_matched.Value);
            DeckConfig.SetEnemyDeck(_matched.Value.Deck);
            return;
        }

        if (TutorialConfig.IsActive && TutorialConfig.EnemyDeck != null)
        {
            MatchOpponentHandoff.Clear();
            DeckConfig.SetEnemyDeck(TutorialConfig.EnemyDeck);
            return;
        }

        if (_matched.HasValue) MatchOpponentHandoff.Set(_matched.Value);
        else                   MatchOpponentHandoff.Clear();

        // 덱 없이 프로필만 온 상대(실제 매칭)는 덱만 폴백을 탄다 — 표시는 매칭한 상대를 그대로 유지한다.
        if (_matched.HasValue && _matched.Value.IsValid)
        {
            DeckConfig.SetEnemyDeck(_matched.Value.Deck);
            return;
        }

        DeckConfig.SetEnemyDeck(aiDeckConfig != null
            ? aiDeckConfig.GetDeckForTier(RankManager.TierIndex)
            : new List<CardData>());
    }

    void ApplyPlayLock()
    {
        matchPanel?.SetPlayInteractable(OutgameFeatureLock.IsUnlocked(EOutgameFeature.LobbyPlay));
    }

    void ShowNoDeckPopup()
    {
        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = "유효한 덱이 없습니다.\n덱을 먼저 구성해 주세요.",
            yesText   = "덱 편성",
            noText    = "닫기",
            yesAction = GoToDeckTab,
        });
    }

    void GoToDeckTab()
    {
        tournamentPanel?.Close();   // 맵이 떠 있는 채로 덱 탭에 가면 오버레이가 덱 화면을 가린다
        if (deckPanel != null) lobbyTabController?.Select(deckPanel);
    }

    void OpenTournamentMap()
    {
        tournamentPanel?.Open();
    }

    // 정점 전투 복귀 — 떠났던 화면(배틀 탭 + 맵)을 되돌린다. 승패 무관하게 맵으로 온다.
    // 선물 등장은 여기서 하지 않는다(골드 흡입 뒤에 따로 온다) — 맵은 이미 미수령 상태를 그리고 있다.
    void HandleTournamentReturn(string _nodeId, bool _won)
    {
        // 탭 트리거는 끈다 — 탭 진입 튜토리얼이 방금 세운 맵을 덮으면 복귀가 무의미해진다.
        if (matchPanel != null) lobbyTabController?.Select(matchPanel, false);
        if (tournamentPanel == null) return;

        // 등장이 올 자리를 열기 전에 비워 둔다 — 순서를 뒤집으면 선물이 이미 서 있다가 다시 튀어나온다.
        if (_won) tournamentPanel.ArmGiftReveal(_nodeId);

        tournamentPanel.Open();
    }

    // 골드 흡입이 끝난 뒤의 선물 등장. PlayGiftReveal이 예약도 함께 푼다 — 맵을 떠났어도 반드시 불러야
    // 선물이 감춰진 채 남지 않는다.
    void HandleGiftReveal(string _nodeId) => tournamentPanel?.PlayGiftReveal(_nodeId);

    // 셸 미배선 폴백 전용. 저장된 슬롯 중 첫 유효 덱을 DeckConfig에 적용하고, 없으면 false.
    static bool TryApplyFirstValidDeck()
    {
        for (int t_i = 0; t_i < DeckSaveManager.SLOT_COUNT; t_i++)
        {
            if (!DeckSaveManager.IsSlotValid(t_i)) continue;

            DeckConfig.Set(DeckSaveManager.Load(t_i));
            return true;
        }
        return false;
    }
}
