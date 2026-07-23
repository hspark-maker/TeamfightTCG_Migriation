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
    bool resultCaptured; // 이번 전투 결과 확정 여부. 최초 승패만 보상 지급하고 이후 덮어쓰기 차단.
    long lastRewardGold; // CaptureResult에서 확정한 지급 골드. F-20 팝업 표시용(표시만, 재지급 없음).

    void OnDestroy()
    {
        if (NetworkSession.Instance != null)
            NetworkSession.Instance.OnPlayerLeftRoom -= HandlePlayerLeft;
    }

#if UNITY_EDITOR
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) this.winPopup?.Show(0);
        if (Input.GetKeyDown(KeyCode.F2)) this.losePopup?.Show(0);
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

    // 승패 확정 시점에 보상 지급
    void CaptureResult(bool _won)
    {
        // 이미 승패가 확정된 뒤에는 이탈-부전승 등 후속 콜백이 결과를 덮어쓰지 못하게 한다.
        if (this.resultCaptured) return;
        
        this.resultCaptured = true;
        int t_remaining = this.playerField.GetActiveCards().Count + this.playerField.WaitingCount;
        this.lastRewardGold = RewardService.GrantBattleReward(t_remaining);
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
        CaptureResult(true);
        this.winPopup?.Show(this.lastRewardGold);
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
        CaptureResult(true);
        this.winPopup?.Show(this.lastRewardGold);
    }

    bool CheckGameOver()
    {
        if (this.enemyField.IsEmpty)
        {
            CaptureResult(true);
            this.winPopup?.Show(this.lastRewardGold);
            return true;
        }
        if (this.playerField.IsEmpty)
        {
            CaptureResult(false);
            this.losePopup?.Show(this.lastRewardGold);
            return true;
        }
        return false;
    }
}
