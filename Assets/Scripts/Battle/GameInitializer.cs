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
    [SerializeField] MatchDeckShell matchDeckShell;             // 전투 전 덱 확인/편집 게이트(비우면 게이트 없이 기존 동작)

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

        // 덱 확인/편집 게이트 — 통과해야 DeckConfig.PlayerDeck이 확정된다.
        // 아래 필드 초기화가 그 값을 소비하므로 반드시 이보다 앞이어야 한다.
        if (!await RunDeckGate()) return;

        if (DeckConfig.IsMultiplayer)
            await InitializeMultiplayerFields();
        else
            InitializeSinglePlayerFields();

        InitializeViews();

        // 튜토리얼: 순차 안내 오버레이 부트스트랩(연출 전용, 규칙 무접촉).
        if (TutorialConfig.IsActive) TutorialOverlayUI.Ensure(this.tutorialOverlayPrefab);

        if (DeckConfig.IsMultiplayer && MultiplayerTurnRunner.Instance != null)
        {
            // false = 초기화 중 상대 이탈 → 전투 시작 없이 부전승 처리 후 조기 종료
            bool t_synced = await MultiplayerTurnRunner.Instance.SyncInitialDecks();
            if (!t_synced)
            {
                // 인트로 줌을 못 타고 빠지는 경로 — Await가 걸어둔 카메라 잠금을 여기서 풀지 않으면
                // 이후 화면 비율 대응(BattleCameraFit)이 영영 멈춘다.
                BattleCameraFit.ClearExternalControl();
                GetComponent<TurnRunner>()?.HandleOpponentLeftDuringInit();
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

    /// <summary>전투 시작 전 덱 확인/편집 화면. 반환 false = 유저가 전투를 포기했다 →
    /// 로비로 되돌리고 이 씬의 초기화를 더 진행하지 않는다.</summary>
    async UniTask<bool> RunDeckGate()
    {
        if (this.matchDeckShell == null) return true;   // 미배선 = 게이트 없음(기존 경로 그대로)

        // 멀티는 한쪽이 덱 화면에 머무는 동안 상대가 타임아웃 없이 대기한다(이탈로도 잡히지 않는다).
        // 매칭 전에 덱이 이미 확정되는 경로라 게이트를 태울 이유도 없다.
        if (DeckConfig.IsMultiplayer) return true;
        if (TutorialConfig.IsActive)  return true;      // 튜토리얼은 양 덱이 스크립트 고정

        if (await this.matchDeckShell.RunSelectionAsync(this.GetCancellationTokenOnDestroy())) return true;

        // 인트로 줌을 못 타고 빠지는 경로 — battleIntro.Await()가 걸어둔 카메라 잠금을 여기서 풀지 않으면
        // 이후 화면 비율 대응(BattleCameraFit)이 영영 멈춘다(초기화 중 상대 이탈 경로와 같은 이유).
        BattleCameraFit.ClearExternalControl();
        BattleCleanup.LoadScene("LobbyScene");

        return false;
    }

    async UniTask InitializeMultiplayerFields()
    {
        await UniTask.WaitUntil(() => MultiplayerTurnRunner.Instance != null
                                     && (MultiplayerTurnRunner.Instance.MyOwnerIndex >= 0
                                         || MultiplayerTurnRunner.Instance.TrySetOwnerIndexFromRunner()));

        int t_myIndex = MultiplayerTurnRunner.Instance.MyOwnerIndex;
        TurnState.LocalOwnerIndex = t_myIndex;
        // 멀티 셔플은 Local 고정: 시드 합의(SyncInitialDecks의 commit-reveal)가 이 호출보다 뒤라
        // MatchRandom을 쓸 수 없고, 쓸 필요도 없다 — 셔플 결과는 GetShuffledIds로 상대에게 그대로 전송된다.
        this.playerField.Initialize(DeckConfig.PlayerDeck, t_myIndex, ShufflePolicy.Local);
    }

    void InitializeSinglePlayerFields()
    {
        // 시드는 필드 초기화보다 **먼저** — 셔플이 MatchRandom을 소비한다.
        // (구: TurnRunner.PlayIntroAndStart에서 시드 → 셔플이 시드 밖 UnityEngine.Random으로 새어나갔다.)
        if (TutorialConfig.IsActive) MatchRandom.Seed(TutorialConfig.FixedSeed);
        else                         MatchRandom.SeedRandomLocal();

        if (TutorialConfig.IsActive)
        {
            // 튜토리얼: 양 덱 고정 주입(무셔플=저작 순서가 곧 등장 순서·6장 이하 허용). 적덱 GetRandomDeck 우회.
            this.playerField.Initialize(TutorialConfig.PlayerDeck, 0, ShufflePolicy.None);
            this.enemyField.Initialize(TutorialConfig.EnemyDeck, 1, ShufflePolicy.None);
        }
        else
        {
            this.playerField.Initialize(DeckConfig.PlayerDeck, 0, ShufflePolicy.Match);
            // 로비 매칭에서 상대 덱을 미리 확정해 넘겼으면 그 값을 쓰고, 아니면 기존대로 랜덤 폴백(기존 MainMenu 경로 유지).
            var t_enemyDeck = DeckConfig.HasEnemyDeck
                ? DeckConfig.EnemyDeck
                : (this.aiDeckConfig?.GetRandomDeck() ?? new System.Collections.Generic.List<CardData>());
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
