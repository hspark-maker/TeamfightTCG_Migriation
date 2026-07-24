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

    async UniTaskVoid Start()
    {
        await StartBattleAsync();
    }

    async UniTask StartBattleAsync()
    {
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
        if (TutorialConfig.IsActive) TutorialOverlayUI.Ensure();

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

        if (this.battleIntro != null)
            await this.battleIntro.Play();

        GetComponent<TurnRunner>()?.StartBattle();
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
            this.enemyField.Initialize(this.aiDeckConfig?.GetRandomDeck() ?? new System.Collections.Generic.List<CardData>(), 1);
        }

        // 시너지: 양 덱 확정 후 각 필드에 1회 적용 (전투 중 재계산 없음)
        // 오프닝 배치는 Placed만 발화하고 시너지 Entered는 미발화 — 등장 트리거(돌보미/흐름)는 런타임 등장(FillEmptySlots/Swap/PlaceDirect)에서만.
        this.playerField.ApplyDeckSynergy();
        this.enemyField.ApplyDeckSynergy();
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
        UIPoolManager.instance.AddOrUpdateUI<SettingsPanel>();
    }
}
