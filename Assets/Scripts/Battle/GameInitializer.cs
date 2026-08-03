using Cysharp.Threading.Tasks;
using UnityEngine;

public class GameInitializer : MonoBehaviour
{
    [SerializeField] BattleField playerField;
    [SerializeField] BattleField enemyField;
    [SerializeField] AIDeckConfig aiDeckConfig;
    [SerializeField] BattleFieldView playerFieldView;
    [SerializeField] BattleFieldView enemyFieldView;
    [SerializeField] BattleIntro battleIntro;
    [SerializeField] AudioClip battleBGM;
    [SerializeField] TutorialOverlayUI tutorialOverlayPrefab;   // 튜토리얼 오버레이 프리팹(비우면 코드 빌드 폴백)
    [SerializeField] BattleVfxLibrary battleVfxLibrary;         // 규칙 기반 연출 배선 단일 지점(비우면 해당 연출만 생략)
    // 덱 확인/편집 게이트(MatchDeckShell)는 여기 없다 — 로비(LobbyMatchLauncher)가 씬 로드 전에 돌린다.
    // 이 씬에 다시 두면 확정 지점이 씬을 넘어 둘로 갈린다. 배틀 씬은 확정된 DeckConfig를 읽기만 한다.

    void Awake()
    {
        // 전투 씬 단독 실행에서도 배선되게 여기서 주입(DataLibrary 비의존).
        // 연출이 늘어도 이 필드는 하나로 고정 — 새 연출은 라이브러리 에셋의 목록에만 추가한다.
        BattleVfx.SetLibrary(this.battleVfxLibrary);
    }

    async UniTaskVoid Start()
    {
        await StartBattleAsync();
    }

    async UniTask StartBattleAsync()
    {
        // 씬 로드 직후부터 턴 정보(배경+레이블) 숨김 — 확대·코인 결과 확정 전까지 안 보이게.
        GetComponent<TurnRunner>()?.HideTurnInfo();

        this.battleIntro.Await();
        // 씬 전환 영상이 재생 중이면 끝날 때까지 대기 (오프닝 배치(Placed) 소리 차단)
        await UniTask.WaitUntil(() => SceneTransitionVideo.Instance == null
                                   || !SceneTransitionVideo.Instance.IsPlaying);

        // 모드 판정이 먼저다 — 아래 두 단계(상대 덱 확정·덱 게이트)가 IsMultiplayer로 갈린다.
        ReconcileMultiplayerFlag();

        // 상대 덱 폴백. 로비를 거쳐 왔으면 이미 확정돼 있어 무동작이다(아래 HasEnemyDeck 가드).
        ConfirmEnemyDeck();

        if (DeckConfig.IsMultiplayer)
        {
            // false = 러너에서 내 ownerIndex를 상한 안에 못 얻음 → 전투를 시작할 수 없다.
            if (!await InitializeMultiplayerFields())
            {
                AbortInit(_timedOut: true);   // ownerIndex 확보 실패는 항상 내 쪽 문제다
                return;
            }
        }
        else
        {
            InitializeSinglePlayerFields();
        }

        InitializeViews();

        // 튜토리얼: 순차 안내 오버레이 부트스트랩(연출 전용, 규칙 무접촉).
        if (TutorialConfig.IsActive) TutorialOverlayUI.Ensure(this.tutorialOverlayPrefab);

        if (DeckConfig.IsMultiplayer && MultiplayerTurnRunner.Instance != null)
        {
            // false = 초기화 중 상대 이탈 → 전투 시작 없이 부전승 처리 후 조기 종료
            bool t_synced = await MultiplayerTurnRunner.Instance.SyncInitialDecks();
            if (!t_synced)
            {
                AbortInit(MultiplayerTurnRunner.Instance.InitTimedOut);
                return;
            }
        }

        SoundManager.Instance?.PlayBGM(this.battleBGM);

        // 인트로 순서: 카메라 확대 → 코인 토스 → 선공 턴 전환 연출 → 카드 배치(딜) → 턴 루프.
        var t_runner = GetComponent<TurnRunner>();

        // 카메라 확대를 먼저 완료한 뒤 코인/배너/딜(콜백)을 TurnRunner가 순차 실행.
        if (this.battleIntro != null) await this.battleIntro.PlayCameraIntro();
        System.Func<UniTask> t_deal = this.battleIntro != null ? () => this.battleIntro.Play() : null;
        if (t_runner != null) await t_runner.PlayIntroAndStart(t_deal);
    }

    /// <summary>상대(AI) 덱 폴백. 정상 경로에서 상대 덱을 뽑는 지점은 로비(LobbyMatchLauncher.ConfirmEnemyDeck)다 —
    /// 덱 화면이 그린 6장과 실제 상대가 같아야 하므로 확정은 화면을 띄우기 전에 끝나야 한다.
    /// 여기는 로비를 거치지 않는 진입점(MainMenu·TutorialSetupUI·AutoBattle 스텝·씬 단독 실행)만 태운다.</summary>
    void ConfirmEnemyDeck()
    {
        // 멀티는 상대 덱이 SyncInitialDecks로 훨씬 뒤에 도착한다 — 지금 확정할 수 있는 값이 없다.
        if (DeckConfig.IsMultiplayer) return;

        // 튜토리얼은 양 덱이 시나리오 고정이다(아래 필드 초기화가 TutorialConfig에서 직접 주입).
        if (TutorialConfig.IsActive) return;

        // 로비가 이미 확정해 넘겼으면 그대로 쓴다 — 여기서 다시 뽑으면 덱 화면에서 공개한 패와 어긋난다.
        if (DeckConfig.HasEnemyDeck) return;

        if (this.aiDeckConfig == null)
        {
            Debug.LogWarning("[GameInitializer] aiDeckConfig 미배선 — 상대 덱 없이 전투가 시작된다.");
            return;
        }

        // GetRandomDeck은 UnityEngine.Random을 쓴다 — MatchRandom(셔플 시드)을 소비하지 않으므로
        // 시드 설정(InitializeSinglePlayerFields)보다 앞에서 뽑아도 결정론에 영향이 없다.
        DeckConfig.SetEnemyDeck(this.aiDeckConfig.GetRandomDeck());
    }

    /// <summary>모드 플래그를 **런타임 사실**과 대조한다.
    ///
    /// DeckConfig.IsMultiplayer는 로비 패널의 OnPlayerJoined 콜백에서 켜지는데, 전투 씬으로 끌고 가는
    /// 주체는 마스터의 Runner.LoadScene이다 — 즉 플래그의 authority와 씬 로드의 authority가 다르다.
    /// 콜백을 놓친(패널이 비활성화돼 구독이 끊긴) 클라이언트는 IsMultiplayer=false인 채 전투에 들어와
    /// 싱글 턴 객체 + 로컬 시드로 진행한다 = commit-reveal 우회, 양쪽이 아예 다른 게임을 한다.
    ///
    /// 여기서는 러너가 살아 있으면 멀티로 승격하고 에러 로그를 남긴다. 로그가 실제로 찍히는지부터
    /// 계측한 뒤, 확인되면 플래그 세팅 자체를 네트워크 계층으로 옮긴다(그때 UI 세팅 제거).</summary>
    static void ReconcileMultiplayerFlag()
    {
        if (DeckConfig.IsMultiplayer) return;
        if (TutorialConfig.IsActive) return;   // 튜토리얼은 정의상 싱글(TutorialConfig.Begin이 명시적으로 끈다).

        var t_runner = NetworkSession.Instance?.Runner;
        if (t_runner == null || !t_runner.IsRunning) return;

        // 러너가 살아 있는 것만으로는 부족하다 — Disconnect()가 await 없이(Forget) 호출되는 경로가
        // 여럿이라(BattleCleanup.LoadScene, 로비 취소) 이전 판의 러너가 싱글 전투에 남아 있을 수 있다.
        // 상대가 실제로 접속해 있을 때만 승격한다. 잘못 승격하면 SyncInitialDecks가 타임아웃 없이 영원히 기다린다.
        int t_players = 0;
        foreach (Fusion.PlayerRef _ in t_runner.ActivePlayers) t_players++;
        if (t_players < 2)
        {
            Debug.LogWarning($"[Mode] 스테일 러너 감지(접속 {t_players}명). 싱글로 진행한다.");
            return;
        }

        Debug.LogError("[Mode] 러너에 상대가 있는데 IsMultiplayer=false로 전투 씬 진입. "
                     + "멀티로 승격한다 — 로비 콜백(SetMultiplayer)을 놓친 경로가 있다.");
        DeckConfig.SetMultiplayer(true);
    }

    /// <summary>초기화를 시작도 못 하고 빠지는 공통 출구.
    /// 인트로 줌을 못 타고 나가므로 Await가 걸어둔 카메라 잠금을 여기서 풀지 않으면
    /// 이후 화면 비율 대응(BattleCameraFit)이 영영 멈춘다.</summary>
    void AbortInit(bool _timedOut)
    {
        BattleCameraFit.ClearExternalControl();

        var t_runner = GetComponent<TurnRunner>();
        if (_timedOut) t_runner?.HandleInitFailed();              // 무보상 종료 — 내 쪽 문제일 수 있다
        else           t_runner?.HandleOpponentLeftDuringInit();  // 상대 이탈 = 부전승
    }

    /// <summary>반환 false = 상한 안에 내 ownerIndex를 못 얻음.
    /// 상한이 없으면 러너가 죽었거나 스테일인 경우 여기서 영원히 멈춘다(전투가 시작조차 안 됨).</summary>
    async UniTask<bool> InitializeMultiplayerFields()
    {
        // WhenAny로 진 쪽 WaitUntil은 저절로 멈추지 않는다. 이 predicate는 부작용이 있어서
        // (TrySetOwnerIndexFromRunner가 TurnState.LocalOwnerIndex를 쓴다) 살려두면 다음 싱글 전투에서
        // 0으로 세팅한 값을 유령이 1로 덮어쓴다 — 반드시 취소한다.
        using var t_cts = new System.Threading.CancellationTokenSource();

        async UniTask WaitOwnerIndex()
        {
            await UniTask.WaitUntil(() => MultiplayerTurnRunner.Instance != null
                                       && (MultiplayerTurnRunner.Instance.MyOwnerIndex >= 0
                                           || MultiplayerTurnRunner.Instance.TrySetOwnerIndexFromRunner()),
                                    cancellationToken: t_cts.Token)
                .SuppressCancellationThrow();
        }

        int t_timedOut = await UniTask.WhenAny(
            WaitOwnerIndex(),
            UniTask.Delay(System.TimeSpan.FromSeconds(MultiplayerTurnRunner.InitSyncTimeoutSec),
                          ignoreTimeScale: true));
        t_cts.Cancel();

        if (t_timedOut == 1)
        {
            Debug.LogError($"[MultiInit] ownerIndex 확보가 {MultiplayerTurnRunner.InitSyncTimeoutSec}초를 넘겼다. 초기화 중단.");
            return false;
        }

        int t_myIndex = MultiplayerTurnRunner.Instance.MyOwnerIndex;
        TurnState.LocalOwnerIndex = t_myIndex;
        // 멀티 셔플은 Local 고정: 시드 합의(SyncInitialDecks의 commit-reveal)가 이 호출보다 뒤라
        // MatchRandom을 쓸 수 없고, 쓸 필요도 없다 — 셔플 결과는 GetShuffledIds로 상대에게 그대로 전송된다.
        this.playerField.Initialize(DeckConfig.PlayerDeck, t_myIndex, ShufflePolicy.Local);
        return true;
    }

    void InitializeSinglePlayerFields()
    {
        // 싱글은 로컬이 항상 0번. 기본값에 기대지 않고 명시한다 — MultiplayerTurnRunner가 씬 오브젝트라
        // 싱글 전투에서도 Awake가 돌고, 스테일 러너가 있으면 LocalOwnerIndex를 1로 써 버린다.
        TurnState.LocalOwnerIndex = 0;

        // 시드는 필드 초기화보다 **먼저** — 셔플이 MatchRandom을 소비한다.
        // (구: TurnRunner.PlayIntroAndStart에서 시드 → 셔플이 시드 밖 UnityEngine.Random으로 새어나갔다.)
        MatchSeeding.SeedForNewMatch();

        if (TutorialConfig.IsActive)
        {
            // 튜토리얼: 양 덱 고정 주입(무셔플=저작 순서가 곧 등장 순서·6장 이하 허용). 적덱 GetRandomDeck 우회.
            // 덱 게이트(ShowDeckGate)를 켜도 여기는 갈리지 않는다 — 튜토리얼 전투 덱은 언제나 시나리오가 정한다.
            this.playerField.Initialize(TutorialConfig.PlayerDeck, 0, ShufflePolicy.None);
            this.enemyField.Initialize(TutorialConfig.EnemyDeck, 1, ShufflePolicy.None);
        }
        else
        {
            this.playerField.Initialize(DeckConfig.PlayerDeck, 0, ShufflePolicy.Match);
            // 상대 덱은 게이트보다 앞선 ConfirmEnemyDeck이 확정해 뒀다(로비가 넘긴 값이면 그대로 유지된다).
            // 여기서 다시 뽑지 않는 게 핵심 — 뽑으면 게이트 화면에서 본 상대와 실제 상대가 갈린다.
            var t_enemyDeck = DeckConfig.EnemyDeck ?? new System.Collections.Generic.List<CardData>();
            this.enemyField.Initialize(t_enemyDeck, 1, ShufflePolicy.Match);
        }


        // 시너지: 양 덱 확정 후 각 필드에 1회 적용 (전투 중 재계산 없음)
        // 오프닝 배치는 Placed만 발화하고 시너지 Entered는 미발화 — 등장 트리거(돌보미/흐름)는 런타임 등장(FillEmptySlots/Swap/PlaceDirect)에서만.
        // 튜토리얼 시너지 미도입 구간은 적용 스킵(스탯=기본값, 배지 숨김). 일반 전투 또는 SynergyEnabled(3편~)이면 적용.
        if (!TutorialConfig.IsActive || TutorialConfig.SynergyEnabled)
        {
            this.playerField.ApplyDeckSynergy();
            this.enemyField.ApplyDeckSynergy();
        }

        // 확정승 튜토리얼: 적 체력을 낮춰(공격력=체력) 플레이어가 무조건 이기게. 시너지 적용 뒤 최종 반영.
        if (TutorialConfig.IsActive && TutorialConfig.EnemyMaxHpOverride > 0)
            this.enemyField.OverrideAllHp(TutorialConfig.EnemyMaxHpOverride);

        // 튜토리얼: 스크립트 기준선 최초 스냅샷. 이후 슬롯 지정 스텝은 "그때 그 카드"인지 여기 기준으로 대조된다.
        if (TutorialConfig.IsActive)
            TutorialConfig.SyncBoardBaseline(this.playerField, this.enemyField);
    }

    void InitializeViews()
    {
        this.playerFieldView.InitializeAnimators();
        this.enemyFieldView.InitializeAnimators();
        this.playerFieldView.Refresh();

        if (!DeckConfig.IsMultiplayer)
            this.enemyFieldView.Refresh();
    }
    
    public void OnSettingButton()
    {
        UIPoolManager.Instance?.AddOrUpdateUI<SettingsPanel>();
    }
}
