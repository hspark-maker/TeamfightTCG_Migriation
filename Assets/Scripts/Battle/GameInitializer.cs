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
        // 씬 전환 영상이 재생 중이면 끝날 때까지 대기 (OnSpawn 소리 차단)
        await UniTask.WaitUntil(() => SceneTransitionVideo.Instance == null
                                   || !SceneTransitionVideo.Instance.IsPlaying);

        if (DeckConfig.IsMultiplayer)
            await InitializeMultiplayerFields();
        else
            InitializeSinglePlayerFields();

        InitializeViews();

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
        this.playerField.Initialize(DeckConfig.PlayerDeck, 0);
        this.enemyField.Initialize(this.aiDeckConfig?.GetRandomDeck() ?? new System.Collections.Generic.List<CardData>(), 1);

        // 시너지: 양 덱 확정 후 각 필드에 1회 적용 (전투 중 재계산 없음)
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
