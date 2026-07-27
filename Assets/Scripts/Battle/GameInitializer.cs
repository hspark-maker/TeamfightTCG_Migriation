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

    async UniTask InitializeMultiplayerFields()
    {
        await UniTask.WaitUntil(() => MultiplayerTurnRunner.Instance != null
                                     && (MultiplayerTurnRunner.Instance.MyOwnerIndex >= 0
                                         || MultiplayerTurnRunner.Instance.TrySetOwnerIndexFromRunner()));

        int t_myIndex = MultiplayerTurnRunner.Instance.MyOwnerIndex;
        TurnState.LocalOwnerIndex = t_myIndex;
        this.playerField.Initialize(DeckConfig.PlayerDeck, t_myIndex);
    }

    void InitializeSinglePlayerFields()
    {
        if (TutorialConfig.IsActive)
        {
            // 튜토리얼: 양 덱 고정 주입(무셔플·6장 이하 허용). 적덱 GetRandomDeck 우회.
            this.playerField.Initialize(TutorialConfig.PlayerDeck, 0);
            this.enemyField.Initialize(TutorialConfig.EnemyDeck, 1);
        }
        else
        {
            this.playerField.Initialize(DeckConfig.PlayerDeck, 0);
            // 로비 매칭에서 상대 덱을 미리 확정해 넘겼으면 그 값을 쓰고, 아니면 기존대로 랜덤 폴백(기존 MainMenu 경로 유지).
            var t_enemyDeck = DeckConfig.HasEnemyDeck
                ? DeckConfig.EnemyDeck
                : (this.aiDeckConfig?.GetRandomDeck() ?? new System.Collections.Generic.List<CardData>());
            this.enemyField.Initialize(t_enemyDeck, 1);
        }


        // 시너지: 양 덱 확정 후 각 필드에 1회 적용 (전투 중 재계산 없음)
        // 오프닝 배치는 Placed만 발화하고 시너지 Entered는 미발화 — 등장 트리거(돌보미/흐름)는 런타임 등장(FillEmptySlots/Swap/PlaceDirect)에서만.
        // 튜토리얼 시너지 미도입 구간은 적용 스킵(스탯=기본값, 배지 숨김). 일반 전투 또는 SynergyEnabled(3편~)이면 적용.
        if (!TutorialConfig.IsActive || TutorialConfig.SynergyEnabled)
        {
            this.playerField.ApplyDeckSynergy();
            this.enemyField.ApplyDeckSynergy();
        }
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
