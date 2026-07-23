using System;
using Cysharp.Threading.Tasks;
using Fusion;
using TMPro;
using UnityEngine;

public class TurnRunner : MonoBehaviour
{
    public static int TurnCount { get; private set; } = 1;

    [SerializeField] BattleField playerField;
    [SerializeField] BattleField enemyField;
    [SerializeField] BattleFieldView playerFieldView;
    [SerializeField] BattleFieldView enemyFieldView;
    [SerializeField] TMP_Text turnLabel;
    [SerializeField] TMP_Text turnCountLabel;
    [SerializeField] DeckPileUI playerDeckUI;
    [SerializeField] DeckPileUI enemyDeckUI;
    [SerializeField] TurnBannerUI    playerTurnBanner;
    [SerializeField] TurnBannerUI    enemyTurnBanner;
    [SerializeField] GameResultPopup winPopup;
    [SerializeField] GameResultPopup losePopup;

    TurnContext ctx;
    bool disconnectWin;

    void OnDestroy()
    {
        if (NetworkSession.Instance != null)
            NetworkSession.Instance.OnPlayerLeftRoom -= HandlePlayerLeft;
    }

#if UNITY_EDITOR
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) this.winPopup?.Show();
        if (Input.GetKeyDown(KeyCode.F2)) this.losePopup?.Show();
    }
#endif

    public void StartBattle()
    {
        this.disconnectWin = false;
        TurnCount = 1;
        SetTurnCountLabel();
        // 멀티는 SyncInitialDecks의 commit-reveal에서 이미 시드됨. 싱글만 로컬 시드.
        if (!DeckConfig.IsMultiplayer)
            MatchRandom.SeedRandomLocal();
        if (DeckConfig.IsMultiplayer && NetworkSession.Instance != null)
            NetworkSession.Instance.OnPlayerLeftRoom += HandlePlayerLeft;
        this.ctx = new TurnContext
        {
            playerField     = this.playerField,
            enemyField      = this.enemyField,
            playerFieldView = this.playerFieldView,
            enemyFieldView  = this.enemyFieldView,
            turnLabel       = this.turnLabel,
            playerDeckUI    = this.playerDeckUI,
            enemyDeckUI     = this.enemyDeckUI,
            playerTurnBanner = this.playerTurnBanner,
            enemyTurnBanner  = this.enemyTurnBanner,
        };
        RunTurns().Forget();
    }

    async UniTask RunTurns()
    {
        int t_current = 0;

        while (true)
        {
            // 멀티: playerField/enemyField는 기기마다 ownerIndex가 다름 → ownerIndex로 조회
            BattleField t_field = DeckConfig.IsMultiplayer
                ? (this.playerField.OwnerIndex == t_current ? this.playerField : this.enemyField)
                : (t_current == 0 ? this.playerField : this.enemyField);

            TurnEvents.RaiseTurnStarted(t_field);
            foreach (var t_c in t_field.GetActiveCards())
            {
                if (t_c.justSpawned) { t_c.justSpawned = false; continue; }
                await (t_c.data.passive?.OnTurnBegan(new TurnCtx(t_c, t_field)) ?? UniTask.CompletedTask);
            }
            this.ctx.RefreshViews();

            // 내 턴인지 기준으로 배너 선택 (멀티에서 P2는 t_current=1이 내 턴)
            bool t_isMyTurn = DeckConfig.IsMultiplayer
                ? t_current == (MultiplayerTurnRunner.Instance?.MyOwnerIndex ?? 0)
                : t_current == 0;
            TurnBannerUI t_banner = t_isMyTurn ? this.ctx.playerTurnBanner : this.ctx.enemyTurnBanner;
            if (t_banner != null)
            {
                SoundManager.Instance?.PlayTurnChange();
                await t_banner.Play();
            }

            TurnBase t_turn;
            if (DeckConfig.IsMultiplayer)
            {
                t_turn = t_isMyTurn
                    ? (TurnBase)new MultiplayerPlayerTurn(this.ctx)
                    : new MultiplayerOpponentTurn(this.ctx);
            }
            else
            {
                t_turn = t_current == 0
                    ? (TurnBase)new PlayerTurn(this.ctx)
                    : (TurnBase)new EnemyTurn(this.ctx);
            }

            t_turn.OnEnter();
            await t_turn.Execute();
            t_turn.OnExit();

            // 유산: 이번 턴 필드의 소속 카드 legacyStack++ (사망 시 아군 회복량). 동기, RNG 미소비.
            foreach (var t_c in t_field.GetActiveCards())
                SynergyTriggers.TurnEnded(new TurnCtx(t_c, t_field));

            if (this.disconnectWin || CheckGameOver()) break;

            if (t_current == 1)
            {
                TurnCount++;
                TurnEvents.RaiseTurnCountChanged(TurnCount);
                SetTurnCountLabel();
            }

            t_current = 1 - t_current;
        }
    }

    void SetTurnCountLabel()
    {
        if (this.turnCountLabel != null)
            this.turnCountLabel.text = $"{TurnCount} 턴";
    }

    public static void Cleanup()
    {
        TurnEvents.Reset();
        MatchRandom.Reset();
        TurnCount = 1;
    }

    void HandlePlayerLeft(PlayerRef _p)
    {
        if (!DeckConfig.IsMultiplayer) return;
        this.disconnectWin = true;
        NetworkGameController.Instance?.ForceOpponentReady();
        MultiplayerTurnRunner.Instance?.ForceOpponentAttackResolve();
        this.winPopup?.Show();
    }

    /// <summary>
    /// 초기화 단계(StartBattle 이전)에서 상대 이탈이 감지된 경우 GameInitializer가 호출.
    /// RunTurns가 아직 시작 전이므로 기존 이탈 시맨틱(부전승)과 일관되게 승리 팝업만 노출.
    /// StartBattle을 호출하지 않으므로 OnPlayerLeftRoom(HandlePlayerLeft) 구독도 발생하지 않아 이중 처리 없음.
    /// </summary>
    public void HandleOpponentLeftDuringInit()
    {
        if (!DeckConfig.IsMultiplayer) return;
        this.disconnectWin = true;
        this.winPopup?.Show();
    }

    bool CheckGameOver()
    {
        if (this.enemyField.IsEmpty)
        {
            this.winPopup?.Show();
            return true;
        }
        if (this.playerField.IsEmpty)
        {
            this.losePopup?.Show();
            return true;
        }
        return false;
    }
}
