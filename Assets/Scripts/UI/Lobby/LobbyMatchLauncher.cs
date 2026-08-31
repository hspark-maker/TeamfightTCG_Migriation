using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

/// 로비 PlayBtn → 출전 덱 확정 → 매칭(실 상대 20초 탐색, 없으면 AI) → 전투 진입.
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

    [Tooltip("정점 도전의 대치 인트로. 미배선이면 예전처럼 덱 화면이 곧장 뜬다 — 연출 때문에 전투가 막히지 않는다.")]
    [SerializeField] VersusIntroShell versusShellPrefab;

    const string BATTLE_SCENE = "BattleScene";

    // 게이트가 열려 있는 동안 PlayBtn 재클릭을 막는다 — 두 번째 진입이 셸의 선택 상태를 덮고,
    // Confirm 한 번에 두 await가 동시에 깨어 LoadScene이 두 번 돈다.
    bool m_running;

    IMatchmaker      m_matchmaker;
    MatchmakingShell m_matchShell;
    VersusIntroShell m_versusShell;
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

    // 실 상대를 먼저 찾고, 못 만나면 안쪽 AI 매칭으로 내려간다. 멀티/싱글 판정은 이 결과가 소유한다 —
    // 여기서 갈리는 것이 DeckConfig.IsMultiplayer 이고, 씬 로드·랭크 정산·보상 경로가 전부 그 값을 따른다.
    IMatchmaker Matchmaker => m_matchmaker ??=
        new PhotonRankedMatchmaker(new FakeMatchmaker(aiDeckConfig, profilePool));

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

    // 매칭 셸과 같은 이유로 첫 도전 때 띄운다. 부모도 같다 — 나중에 생성돼 마지막 형제가 되므로
    // 맵 오버레이 위에 선다(이 화면은 맵을 덮어야 한다).
    VersusIntroShell VersusShell
    {
        get
        {
            if (m_versusShell == null && versusShellPrefab != null)
                m_versusShell = Instantiate(versusShellPrefab, transform.parent);

            return m_versusShell;
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

    /// <summary>PlayBtn 진입점. 이름은 인스펙터 배선 호환을 위해 유지한다 —
    /// 실제로는 매칭 결과에 따라 실 멀티로도 간다(<see cref="PhotonRankedMatchmaker"/>).</summary>
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
        if (t_node.enemyDeckIds == null || t_node.enemyDeckIds.Count == 0)
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
            MatchProfile.OfTournamentNode(t_node.displayName, t_node.avatar), t_node.EnemyDeckIds);

        RunEntryAsync(t_preset).Forget();
    }

    // 로비에서 전투로 넘어가는 유일한 문. 세 진입 경로가 여기로 모인다 — 전환 연출을 갈아끼울 때 손댈 자리가 하나여야 한다.
    //
    // m_running을 되돌리지 않는 이유: 커튼이 도는 동안 로비는 그대로 살아 있다. 하드컷 시절엔 그 창이 한 프레임이라
    // 무시할 만했지만, 이제는 그 사이 PlayBtn 재클릭이 덱 화면을 커튼 밑에서 다시 연다(RunEntryAsync의 finally가
    // 이 지점보다 먼저 m_running을 내린다). 로비는 곧 파괴되므로 다시 세운 채 두면 된다.
    void EnterBattle()
    {
        EnterBattleAsync().Forget();
    }

    async UniTaskVoid EnterBattleAsync()
    {
        if (m_running) return;
        m_running = true;

        EBattleContentGateResult t_result = await BattleContentSync.CheckBeforeBattleAsync(
            DeckConfig.IsMultiplayer, this.GetCancellationTokenOnDestroy());
        if (this == null) return;

        if (t_result == EBattleContentGateResult.Current ||
            t_result == EBattleContentGateResult.OfflineAllowed)
        {
            if (t_result == EBattleContentGateResult.OfflineAllowed)
                Debug.LogWarning("[BattleContent] Server comparison unavailable. Single-player battle continues with the current snapshot.");

            // 대인전은 매칭 단계(PreBattleMatchSync)가 이미 서버 검증을 끝냈다 — 여기서 또 태우지 않는다.
            if (DeckConfig.IsMultiplayer)
            {
                LoadBattleSceneOverNetwork();
                return;
            }

            bool t_deckValidated = await RunSoloValidationAsync();

            // 파괴 검사가 먼저다 — 씬이 내려가는 중에 팝업을 세우면 이미 죽은 오브젝트를 만진다.
            if (this == null) return;
            if (!t_deckValidated)
            {
                ShowEntryBlocked("덱을 확인하지 못했습니다.\n네트워크 연결을 확인한 뒤 다시 시도해 주세요.");
                return;
            }

            CurtainView.LoadScene(BATTLE_SCENE);
            return;
        }

        ShowEntryBlocked(t_result == EBattleContentGateResult.UpdatedRestartRequired
            ? "새 전투 데이터를 받았습니다.\n게임을 다시 시작한 뒤 전투를 시작해 주세요."
            : "전투 데이터를 확인할 수 없습니다.\n네트워크 연결을 확인한 뒤 다시 시도해 주세요.");
    }

    /// <summary>AI 대전도 서버가 덱을 검증한 뒤에만 씬으로 넘어간다 — 대인전과 같은 규율이다.
    ///
    /// <para>튜토리얼은 제외한다. 상대도 덱도 시나리오 저작값이라 세이브에 없는 카드가 들어가고,
    /// 서버 대조는 그것을 정상적으로 card_not_owned 로 거절한다 — 통과할 수 없는 검사다.</para></summary>
    async UniTask<bool> RunSoloValidationAsync()
    {
        if (TutorialConfig.IsActive) return true;

        ESoloMatchSyncResult t_result = await SoloMatchSync.RunAsync(this.GetCancellationTokenOnDestroy());

        // 씬이 내려가는 중이면 화면을 세우지 않는다 — 호출부가 this == null 로 걸러 준다.
        return t_result == ESoloMatchSyncResult.Success;
    }

    // 진입을 접고 로비를 되돌린다. 진입 게이트가 여럿이라 되돌리는 자리는 하나여야 한다 —
    // m_running 을 안 내리면 PlayBtn 이 영영 안 먹고, TournamentRun 을 안 끊으면
    // 다음 일반 전투의 AI 레벨이 정점 레벨로 굳는다.
    void ShowEntryBlocked(string _message)
    {
        TournamentRun.End();
        m_matchShell?.Close();
        m_running = false;
        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = _message,
            yesText = "확인",
        });
    }

    // 멀티는 두 클라가 같은 씬으로 함께 넘어가야 한다 — 마스터가 러너로 태우고 나머지는 따라 들어간다.
    // 커튼(CurtainView)을 쓰지 않는 이유: 그건 이쪽 화면만 덮는 로컬 전환이라, 러너가 씬을 바꾸는
    // 시점과 어긋나면 한쪽만 로비에 남는다.
    void LoadBattleSceneOverNetwork()
    {
        NetworkRunner t_runner = NetworkSession.Instance?.Runner;
        if (t_runner == null)
        {
            // 여기까지 왔는데 러너가 없으면 매칭이 세운 멀티 플래그가 거짓이다 — 싱글로 되돌려 전투는 살린다.
            Debug.LogError("[LobbyMatchLauncher] 멀티 진입인데 러너가 없다 — 싱글 경로로 전투를 연다.");
            DeckConfig.ResetMode();
            CurtainView.LoadScene(BATTLE_SCENE);
            return;
        }

        SceneTransitionVideo.Instance?.PlayOverlay();

        // 마스터가 아니면 부르지 않는다. 러너가 씬을 바꾸면 이쪽도 함께 넘어간다.
        if (!t_runner.IsSharedModeMasterClient) return;

        int t_buildIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{BATTLE_SCENE}.unity");
        if (t_buildIndex < 0) t_buildIndex = SceneUtility.GetBuildIndexByScenePath(BATTLE_SCENE);
        t_runner.LoadScene(SceneRef.FromIndex(t_buildIndex));
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

    // 일반전은 출전 덱 확정 → 매칭 연출 → 상대 확정 순서다. 어느 단계든 포기하면 false로 빠져 로비가 그대로 남는다.
    // 고정 상대(토너먼트)와 튜토리얼은 상대가 이미 정해져 있으므로 기존처럼 상대를 먼저 확정한다.
    async UniTask<bool> RunEntryChainAsync(CancellationToken _ct, MatchOpponent? _preset = null)
    {
        if (!_preset.HasValue && UseMatchmaking)
        {
            // 덱 화면에는 아직 정해지지 않은 상대를 빈 칸으로 보인다. 직전 전투의 캐리어가 남아 있으면
            // 새 상대처럼 보이므로 선택 화면을 열기 전에 명시적으로 비운다.
            MatchOpponentHandoff.Clear();
            DeckConfig.ClearEnemyDeck();

            bool t_selected;
            if (DeckShell == null)
            {
                Debug.LogWarning("[LobbyMatchLauncher] 덱 화면 미배선 — 첫 유효 덱으로 전투에 진입한다.");
                t_selected = TryApplyFirstValidDeck();
            }
            else
            {
                t_selected = await DeckShell.RunSelectionAsync(_ct);
            }

            if (!t_selected || _ct.IsCancellationRequested) return false;

            // 매칭 화면을 먼저 세운 뒤 덱 화면을 내린다 — 두 오버레이 사이로 로비가 한 프레임 비치지 않게 한다.
            MatchmakingShell t_matchShell = MatchShell;
            if (t_matchShell == null)
            {
                DeckShell?.Close();
                return false;
            }

            UniTask<MatchOpponent?> t_match = t_matchShell.RunMatchAsync(Matchmaker, _ct);
            DeckShell?.Close();

            // 아래 고정 상대 경로의 t_opponent와 이름을 나눈다 — 같은 이름은 메서드 선언 공간이 겹쳐 컴파일되지 않는다.
            MatchOpponent? t_matched = await t_match;
            if (t_matched == null) return false;   // 취소 = 로비로 되돌아간다

            ConfirmOpponent(t_matched);

            // 성공한 매칭 화면은 곧 시작될 씬 전환이 덮는다. 여기서 닫으면 콘텐츠 확인 동안 로비가 드러난다.
            return true;
        }

        // 고정 상대(토너먼트 정점)와 튜토리얼은 매칭을 타지 않는다.
        MatchOpponent? t_opponent = _preset;
        ConfirmOpponent(t_opponent, _preset.HasValue);

        if (DeckShell == null)
        {
            Debug.LogWarning("[LobbyMatchLauncher] 덱 화면 미배선 — 첫 유효 덱으로 전투에 진입한다.");

            // 넘어갈 화면이 없으니 매칭 화면은 여기서 스스로 내려간다(전환이 내려 줄 기회가 없다).
            m_matchShell?.Close();

            return TryApplyFirstValidDeck();
        }

        // 고정 상대는 매칭 대신 대치 인트로를 앞세운다 — 정점을 누른 것과 덱을 짜는 것 사이가
        // 비어 있으면 상대가 누구인지 화면이 한 번도 말하지 않는다.
        //
        // 셸을 여기서 붙잡아 넘긴다 — VersusShell은 비어 있으면 새로 만드는 프로퍼티라,
        // 전환 도중 셸이 파괴되면 저작 상태의 새 셸에서 갈라짐만 도는 경로가 생긴다.
        if (_preset.HasValue)
        {
            VersusIntroShell t_versus = VersusShell;

            if (t_versus != null) return await RunSelectionWithVersusAsync(t_versus, _preset.Value, _ct);
        }

        // 앞세울 화면이 없는 경로(튜토리얼·셸 미배선)는 옮겨 앉힐 이전 화면도 없다 — 덱 화면이 곧장 뜬다.
        return await DeckShell.RunSelectionAsync(_ct);
    }

    // 대치 인트로 → 덱 화면. 매칭 경로(RunSelectionWithHandoffAsync)와 같은 규약이되 앞자리 화면만 다르다.
    //
    // 덱 화면을 대치가 "끝난 뒤에" 세우는 이유: 매칭은 상대를 기다리는 동안 세울 시간이 있지만
    // 여기는 상대가 이미 정해져 있어 대기가 없다. 미리 세워 두면 그 레이아웃 비용이 진입 안무 첫 프레임에 얹힌다.
    async UniTask<bool> RunSelectionWithVersusAsync(VersusIntroShell _versus, MatchOpponent _opponent,
                                                    CancellationToken _ct)
    {
        await _versus.PlayVersusAsync(_opponent, _ct);

        // 씬이 내려가는 중이다 — 파괴될 화면을 세우지 않는다.
        if (_ct.IsCancellationRequested) return false;

        // 여기서부터 화면을 내릴 책임은 갈라짐에 있다. 덱 화면을 세우다 던지면 넘겨받을 것이 없으므로,
        // 대치 화면이 로비를 덮은 채(터치까지 먹는다) 남지 않게 이 구간만 감싼다.
        try
        {
            MatchHandoffTargets t_targets = DeckShell.PrepareForHandoff();

            // 선택 게이트는 전환이 도는 동안 시작해 첫 대기에서 멈춘다 — 전환이 끝난 프레임엔 이미 서 있어야 한다.
            UniTask<bool> t_selection = DeckShell.RunSelectionAsync(_ct);

            await _versus.PlayHandoffAsync(t_targets, _ct);

            return await t_selection;
        }
        catch
        {
            _versus.Close();

            throw;
        }
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
            : new List<int>());
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
        // 버튼을 죽여 두는 것만으로는 부족하다 — 잠김 표시는 표현 레이어 몫이고, 진입을 실제로 막는 주체는 여기다.
        // 정점 전투 복귀(HandleTournamentReturn)는 이 문을 거치지 않는다 — 거치게 하면 랭크가 복귀를 삼킨다.
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.Tournament)) return;

        tournamentPanel?.Open();
        // 복귀 재오픈(HandleTournamentReturn)은 이 자리를 거치지 않는다 — 안내가 전투 복귀 연출 위에 겹치지 않는 이유다.
        TriggeredTutorialRunner.Fire(EOutgameTutorialTrigger.TournamentMapFirstOpen);
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
