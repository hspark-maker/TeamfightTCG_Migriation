using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

/// 로비 PlayBtn → 매칭(실 상대 20초 탐색, 없으면 AI) → 전투 진입. 일반전은 덱 확인 화면을 거치지 않는다.
///
/// 전투가 소비하는 DeckConfig.PlayerDeck은 씬 로드 전에 여기서 확정된다 — 일반전은 대표 덱
/// (DeckSaveManager.SelectedSlot)을 싣고, 덱 화면이 남아 있는 두 경로(모험 정점·튜토리얼 덱 게이트)는
/// 그 화면(MatchDeckShell)이 싣는다. 배틀 씬은 확정된 값을 읽기만 한다.
public class LobbyMatchLauncher : MonoBehaviour
{
    [SerializeField] LobbyOverlayHost overlayHost;
    [SerializeField] AIDeckConfig   aiDeckConfig;   // BattleScene GameInitializer가 참조하는 것과 동일 에셋

    [Header("매칭 연출")]
    [SerializeField] MatchmakingShell    matchShellPrefab;   // 미배선이면 매칭도 대치 인트로도 없이 구 동작
    [SerializeField] OpponentProfilePool profilePool;

    [Header("유효 덱 없음 안내")]
    [SerializeField] LobbyTabController lobbyTabController;
    [SerializeField] LobbyTabPanel deckPanel;

    [Header("기능 잠금")]
    [Tooltip("전투 진입 버튼(PlayBtn). 이 컴포넌트가 LobbyPlay 해금 여부로 interactable을 소유한다.\n" +
             "미배선이면 잠김이 화면에 안 드러난다 — 진입 차단 자체는 StartAiBattle이 따로 막는다.")]
    [SerializeField] LobbyMatchTabPanel matchPanel;

    [Header("모험")]
    [Tooltip("모험 맵 오버레이. 여닫음은 맵이 스스로 갖고, 여는 계기·전투 진입만 로비 쪽이 쥔다 — 맵이 컨트롤러·런처를 인스펙터로 물면 그 배선이 탭 프리팹 오버라이드로 남는다.")]
    [SerializeField] TournamentMapOverlayView tournamentPanel;

    const string BATTLE_SCENE = "BattleScene";

    // 게이트가 열려 있는 동안 PlayBtn 재클릭을 막는다 — 두 번째 진입이 셸의 선택 상태를 덮고,
    // Confirm 한 번에 두 await가 동시에 깨어 LoadScene이 두 번 돈다.
    bool m_running;

    public bool IsRunning => m_running;

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

    // 실 상대를 먼저 찾고, 못 만나면 안쪽 AI 매칭으로 내려간다. 멀티/싱글 판정은 이 결과가 소유한다 —
    // 여기서 갈리는 것이 DeckConfig.IsMultiplayer 이고, 씬 로드·랭크 정산·보상 경로가 전부 그 값을 따른다.
    IMatchmaker Matchmaker => m_matchmaker ??=
        new PhotonRankedMatchmaker(new ServerMatchmaker(profilePool));

    // 로비 캔버스에 미리 얹지 않고 첫 매칭 때 띄운다 — 로비 프리팹을 저장할 때마다 SafeArea가
    // 런타임 계산값으로 굳어(anchorMax) 관계없는 좌표가 함께 커밋된다. 부모는 덱 화면과 같은 SafeArea다.
    MatchmakingShell MatchShell
    {
        get
        {
            if (m_matchShell == null && matchShellPrefab != null)
            {
                // 부모가 곧 셸의 자리다. 런처를 캔버스 밖(=루트)에 두면 셸이 캔버스 없이 생성돼
                // 아무것도 렌더되지 않는데, 매칭 로직은 그대로 돌아서 "매칭은 되는데 화면만 안 뜨는"
                // 무증상 결함이 된다 — 조용히 넘기지 않고 여기서 끊는다.
                if (transform.parent == null)
                {
                    Debug.LogError(
                        "[LobbyMatchLauncher] 런처가 씬 루트에 있어 매칭 셸을 세울 자리가 없다 — "
                      + "캔버스(SafeArea) 자식으로 배선할 것.", this);
                    return null;
                }
                m_matchShell = Instantiate(matchShellPrefab, transform.parent);
            }

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
        TournamentReturnFlow.RewardClaimRequested += HandleRewardClaim;

        OutgameFeatureLock.OnChanged += ApplyPlayLock;
        ApplyPlayLock();
    }

    // 셸은 이 런처가 만든 것이므로 이 런처와 함께 죽어야 한다. 부모가 이 씬 안에 있으면 어차피 같이
    // 사라지지만, 부모를 잘못 잡아 상시 캔버스에 붙은 경우에는 씬을 넘어 살아남아 다음 화면을 덮는다.
    // 성공 경로는 씬 전환이 셸을 치운다고 믿고 Close()를 부르지 않으므로, 그 믿음을 여기서 보증한다.
    void OnDestroy()
    {
        if (m_matchShell != null) Destroy(m_matchShell.gameObject);
        m_matchShell = null;
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
        TournamentReturnFlow.RewardClaimRequested -= HandleRewardClaim;

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

    /// <summary>모험 정점 도전. 상대·덱·AI 레벨이 저작 고정이라 매칭을 태우지 않는다.
    /// TournamentRun.Begin은 모든 가드를 통과한 뒤에 온다 — 중간에 return하며 세워 두면 그게 곧 로비 누수다.</summary>
    public void StartTournamentBattle(int _nodeIndex)
    {
        if (m_running) return;

        if (!TournamentProgress.CanEnter(_nodeIndex)) return;
        if (!TournamentProgress.TryGetNode(_nodeIndex, out TournamentNodeDef t_node)) return;

        // 저작 덱이 비면 상대 없이 전투가 뜬다(DeckConfig.SetEnemyDeck은 null도 못 받는다) — 진입 단계에서 막는다.
        if (t_node.enemyDeckIds == null || t_node.enemyDeckIds.Count == 0)
        {
            Debug.LogWarning($"[LobbyMatchLauncher] 모험 정점 '{t_node.nodeId}'에 상대 덱이 없어 진입을 막는다 — 저작 검증 필요.");
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

        // 전역 초기화 상태(MarkUpdateRequired)는 건드리지 않는다 — 로비에는 업데이트 화면이 없고,
        // IsTerminated 를 켜면 PayoutInbox 같은 세션 배관만 조용히 멈춘다. 안내는 이 팝업이 한다.
        if (t_result == EBattleContentGateResult.UpdateRequired)
        {
            ShowEntryBlocked("현재 앱이 지원하지 않는 새 콘텐츠가 배포되었습니다.\n앱을 업데이트해 주세요.");
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

        // 모험(모험) 정점도 제외한다. 상대·덱·AI 레벨이 저작 고정이라 이 경로는 findAiMatch를 태우지 않고
        // (RunEntryChainAsync가 매칭 블록을 건너뛴다), 그래서 서버 매치 신원 자체가 없다 —
        // SoloMatchSync는 그것을 "findAiMatch가 발급한 매치 신원이 없다"로 거절하므로 정점이 영영 시작되지 않는다.
        //
        // 여기서 통과시켜도 보상 자격은 클라가 못 만든다: 정점 격파는 reportTournamentWin이,
        // 지급은 claimReward가 서버에서 선행 사슬·랭크 잠금을 다시 재고 결정한다(matchId를 쓰지 않는 경로).
        // 남는 구멍은 정점 전투에 한해 출전 덱 소유·성장 대조가 빠진다는 것 — 그건 findAiMatch에
        // 모험 모드를 여는 서버 작업이 필요하다.
        if (TournamentRun.IsActive) return true;

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
        // 봉인만 되고 전투로 가지 못한 매치다. 남겨 두면 다음 진입이 그 시드·보드 순서를 소비한다.
        SoloMatchHandoff.Clear();
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

        // 두 클라가 각자 연다. 마스터만 열고 Fusion 이 상대를 끌어오던 구조는 늦은 쪽의 로비 절차를
        // 강제 종료시켰고, 마스터가 끊기면 상대가 영영 못 들어왔다 — BattleSceneEntry 설명 참조.
        BattleSceneEntry.Load(BATTLE_SCENE);
    }

    // 진입 체인이 "전투 시작"으로 닫히면 그때 씬을 로드한다. 포기면 각 화면이 스스로 닫고 로비가 그대로 남는다.
    async UniTaskVoid RunEntryAsync(MatchOpponent? _preset = null)
    {
        var t_ct = this.GetCancellationTokenOnDestroy();

        // 전투로 닫히지 않은 모든 끝(포기·취소·예외)에서 모험 플래그를 끊는다. 그 경로엔 씬 전환이 없어
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
    // 고정 상대(모험)와 튜토리얼은 상대가 이미 정해져 있으므로 기존처럼 상대를 먼저 확정한다.
    async UniTask<bool> RunEntryChainAsync(CancellationToken _ct, MatchOpponent? _preset = null)
    {
        // 서버 매치 신원은 분기 **밖에서** 비운다. 고정 상대(모험) 경로는 아래 블록을 건너뛰므로
        // 여기서 지우지 않으면 직전 AI 매칭이 남긴 시드·보드 순서가 정점 전투의 양 덱을 갈아끼운다.
        SoloMatchHandoff.Clear();

        if (!_preset.HasValue && UseMatchmaking)
        {
            // 직전 전투의 캐리어가 남아 있으면 새 상대처럼 읽힌다 — 매칭을 열기 전에 명시적으로 비운다.
            MatchOpponentHandoff.Clear();
            DeckConfig.ClearEnemyDeck();

            // 출전 덱은 유저가 로비 덱 탭에서 정해 둔 대표 덱이다. 덱 확인 화면을 거치지 않으므로
            // 여기가 일반전에서 DeckConfig.PlayerDeck을 채우는 유일한 지점이다.
            if (!TryApplySelectedDeck()) return false;

            MatchmakingShell t_matchShell = MatchShell;
            if (t_matchShell == null) return false;

            // 아래 고정 상대 경로의 t_opponent와 이름을 나눈다 — 같은 이름은 메서드 선언 공간이 겹쳐 컴파일되지 않는다.
            MatchOpponent? t_matched = await t_matchShell.RunMatchAsync(Matchmaker, _ct);
            if (t_matched == null) return false;   // 취소 = 로비로 되돌아간다

            ConfirmOpponent(t_matched);

            // 성공한 매칭 화면은 곧 시작될 씬 전환이 덮는다. 여기서 닫으면 콘텐츠 확인 동안 로비가 드러난다.
            return true;
        }

        // 고정 상대(모험 정점)와 튜토리얼은 매칭을 타지 않는다.
        MatchOpponent? t_opponent = _preset;
        ConfirmOpponent(t_opponent, _preset.HasValue);

        if (DeckShell == null)
        {
            Debug.LogWarning("[LobbyMatchLauncher] 덱 화면 미배선 — 대표 덱으로 전투에 진입한다.");

            // 넘어갈 화면이 없으니 매칭 화면은 여기서 스스로 내려간다(전환이 내려 줄 기회가 없다).
            m_matchShell?.Close();

            return TryApplySelectedDeck();
        }

        // 고정 상대는 매칭 대신 대치 인트로를 앞세운다 — 정점을 누른 것과 덱을 짜는 것 사이가
        // 비어 있으면 상대가 누구인지 화면이 한 번도 말하지 않는다.
        //
        // 셸을 여기서 붙잡아 넘긴다 — MatchShell은 비어 있으면 새로 만드는 프로퍼티라,
        // 전환 도중 셸이 파괴되면 저작 상태의 새 셸에서 갈라짐만 도는 경로가 생긴다.
        // 셸이 미배선(matchShellPrefab 없음)이면 null이라 아래 곧장 뜨는 경로로 내려간다.
        if (_preset.HasValue)
        {
            MatchmakingShell t_versus = MatchShell;

            if (t_versus != null) return await RunSelectionWithVersusAsync(t_versus, _preset.Value, _ct);
        }

        // 앞세울 화면이 없는 경로(튜토리얼·셸 미배선)는 옮겨 앉힐 이전 화면도 없다 — 덱 화면이 곧장 뜬다.
        return await DeckShell.RunSelectionAsync(_ct);
    }

    // 대치 인트로 → 갈라짐 → 덱 화면. 덱 화면을 앞세우는 경로는 지금 이것 하나뿐이다 —
    // 랭크전(RunEntryChainAsync의 매칭 갈래)은 로비 대표 덱으로 곧장 전투에 들어가 덱 화면을 거치지 않는다.
    //
    // 덱 화면을 대치가 "끝난 뒤에" 세우는 이유: 상대가 이미 정해져 있어 미리 세울 시간을 벌어 줄 대기가 없다.
    // 진입 안무 앞에 세우면 그 레이아웃 비용이 첫 프레임에 그대로 얹힌다.
    async UniTask<bool> RunSelectionWithVersusAsync(MatchmakingShell _versus, MatchOpponent _opponent,
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
            DeckConfig.SetEnemyDeck(_matched.Value.Deck, _matched.Value.CardLevel);
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
            DeckConfig.SetEnemyDeck(_matched.Value.Deck, _matched.Value.CardLevel);
            return;
        }

        int t_cardLevel = 0;
        List<int> t_deck = aiDeckConfig != null
            ? aiDeckConfig.GetDeckForTier(RankManager.TierIndex, out t_cardLevel)
            : new List<int>();
        DeckConfig.SetEnemyDeck(t_deck, t_cardLevel);
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
    // 보상 팝업은 여기서 열지 않는다(서버 낙인이 선 뒤에 따로 온다) — 맵은 이미 미수령 상태를 그리고 있다.
    void HandleTournamentReturn(string _nodeId, bool _won)
    {
        // 탭 트리거는 끈다 — 탭 진입 튜토리얼이 방금 세운 맵을 덮으면 복귀가 무의미해진다.
        if (matchPanel != null) lobbyTabController?.Select(matchPanel, false);

        tournamentPanel?.Open();
    }

    // 낙인이 선 직후의 보상 수령. 승리 복귀에서만 온다.
    void HandleRewardClaim(string _nodeId) => tournamentPanel?.OpenReturnReward(_nodeId);

    // 유저가 로비 덱 탭에서 정해 둔 대표 덱을 씬 전환 캐리어에 싣는다. 유효 덱이 하나도 없으면 false —
    // 진입 앞단(StartAiBattle)이 이미 걸러 내므로 여기까지 오는 일은 세이브가 도중에 비었을 때뿐이다.
    static bool TryApplySelectedDeck()
    {
        int t_slot = DeckSaveManager.SelectedSlot;
        if (t_slot < 0)
        {
            Debug.LogWarning("[LobbyMatchLauncher] 출전할 유효 덱이 없다 — 전투를 시작하지 않는다.");

            return false;
        }

        DeckConfig.Set(DeckSaveManager.GetSlot(t_slot));

        return true;
    }
}
