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
    [SerializeField] TurnBannerUI    turnBanner;
    [SerializeField] CoinFlipUI      coinFlip;       // 선/후공 결정 연출(싱글 AI전 전용, 비우면 스킵)
    [SerializeField] GameResultPopup winPopup;
    [SerializeField] GameResultPopup losePopup;

    TurnContext ctx;
    bool disconnectWin;
    bool resultCaptured; // 이번 전투 결과 확정 여부. 최초 승패만 보상 지급하고 이후 덮어쓰기 차단.
    long lastRewardGold; // CaptureResult에서 확정한 지급 골드. F-20 팝업 표시용(표시만, 재지급 없음).
    long lastRankDelta;  // CaptureResult에서 확정한 랭크 포인트 증감(클램프 반영). 팝업 표시용(표시만).

    void OnDestroy()
    {
        if (NetworkSession.Instance != null)
            NetworkSession.Instance.OnPlayerLeftRoom -= HandlePlayerLeft;
    }

#if UNITY_EDITOR
    void Update()
    {
        // 연출 확인용 샘플 보상 — 0이면 코인·수치 롤링이 통째로 생략돼 볼 게 없다.
        // 패배(F2)는 설계상 분출·롤링이 없다 — 값만 박힌 채 뜨는 게 정상이다.
        if (Input.GetKeyDown(KeyCode.F1)) this.winPopup?.Show(1234, 10, _won: true);
        if (Input.GetKeyDown(KeyCode.F2)) this.losePopup?.Show(1234, -5, _won: false);
    }
#endif

    /// <summary>외부 직접 호출용(딜 연출 없음). 정상 경로는 GameInitializer가 <see cref="PlayIntroAndStart"/>로 호출.</summary>
    public void StartBattle() => PlayIntroAndStart(null).Forget();

    /// <summary>턴 정보(배경+레이블) 즉시 숨김. 인트로 확대 전에 GameInitializer가 호출 → 코인 결과 전까지 안 보이게.</summary>
    public void HideTurnInfo()
    {
        GameObject t_go = this.turnBanner != null ? this.turnBanner.gameObject
                        : (this.turnLabel != null ? this.turnLabel.gameObject : null);
        if (t_go != null) t_go.SetActive(false);
    }

    /// <summary>전투 인트로 시퀀스 후 턴 루프 시작.
    /// 순서: (1) 코인 토스로 선/후공 결정 → (2) 선공 턴 전환 연출 → (3) 카드 배치(<paramref name="_dealCards"/>)
    /// → (4) 턴 루프(선공 배너는 이미 재생했으므로 첫 턴 배너 스킵). 코인은 싱글 AI전 전용(멀티 스킵).</summary>
    public async UniTask PlayIntroAndStart(System.Func<UniTask> _dealCards)
    {
        this.disconnectWin = false;
        TurnCount = 1;
        SetTurnCountLabel();
        // 멀티는 SyncInitialDecks의 commit-reveal에서 이미 시드됨. 싱글만 로컬 시드.
        // 튜토리얼: 고정 시드 = 스플래시/랜덤효과까지 실행마다 재현(무셔플만으론 게임로직 RNG가 안 고정).
        if (TutorialConfig.IsActive)
            MatchRandom.Seed(0x7507_0521_1A11_0A15UL);   // 튜토리얼 고정 시드(임의 상수)
        else if (!DeckConfig.IsMultiplayer)
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
            turnBanner       = this.turnBanner,
        };

        // (선/후공 판정 — 시드 확정 후) 멀티는 기존 고정(발산 방지), 싱글은 코인 랜덤.
        bool t_playerFirst = DecideFirstPlayer();
        int  t_first = t_playerFirst ? 0 : 1;   // 멀티는 playerFirst=true → 0(기존 동작)

        // 코인 토스 전에는 턴 정보(배경+레이블) 숨김(선/후공 미정 상태). 배너 GO에 배경 스프라이트+WhosTurn 라벨이 함께 있음.
        GameObject t_turnInfo = this.turnBanner != null ? this.turnBanner.gameObject
                              : (this.turnLabel != null ? this.turnLabel.gameObject : null);
        if (t_turnInfo != null) t_turnInfo.SetActive(false);

        // (1) 코인 토스 — 카드 배치 전. 싱글 AI전 전용.
        if (this.coinFlip != null && !DeckConfig.IsMultiplayer)
        {
            this.coinFlip.gameObject.SetActive(true);
            await this.coinFlip.Play(t_playerFirst);   // 앞면=선공(플레이어 먼저)
            await UniTask.Delay(500);                   // 결과 잠깐 유지
            this.coinFlip.gameObject.SetActive(false);
        }

        // 코인 결과 확정 → 턴 정보 다시 표시(배너 Play 전에 활성화 필요). 텍스트는 각 턴 OnEnter에서 세팅.
        if (t_turnInfo != null) t_turnInfo.SetActive(true);

        // (2) 선공 턴 전환 연출.
        if (this.ctx.turnBanner != null)
        {
            SoundManager.Instance?.PlayTurnChange();
            await this.ctx.turnBanner.Play(IsMyTurn(t_first));
        }

        // (3) 카드 배치.
        if (_dealCards != null) await _dealCards();

        // (3.5) 후공 어드밴티지 멀리건 — 첫 턴 시작 전, 보드가 채워진 뒤. 싱글 전용(멀티는 내부에서 no-op).
        await MulliganPhase.Run(this.ctx, t_first, this.GetCancellationTokenOnDestroy());

        // (4) 턴 루프(선공 배너 재생 완료 → 첫 턴 배너 스킵).
        RunTurns(t_first, _skipFirstBanner: true).Forget();
    }

    // 해당 t_current가 로컬(내) 턴인가. 멀티에서 P2는 t_current=1이 내 턴.
    bool IsMyTurn(int _current) => DeckConfig.IsMultiplayer
        ? _current == (MultiplayerTurnRunner.Instance?.MyOwnerIndex ?? 0)
        : _current == 0;

    async UniTask RunTurns(int _startCurrent, bool _skipFirstBanner)
    {
        int  t_current    = _startCurrent;
        bool t_skipBanner = _skipFirstBanner;

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
                // [TurnBegan] 카드 단위. 패시브 → 시너지 순.
                var t_beganCtx = new TurnCtx(t_c, t_field);
                await (t_c.data.passive?.OnTurnBegan(t_beganCtx) ?? UniTask.CompletedTask);
                await SynergyTriggers.TurnBegan(t_beganCtx);
            }
            this.ctx.RefreshViews();

            // 내 턴인지 기준으로 배너 선택 (멀티에서 P2는 t_current=1이 내 턴)
            bool t_isMyTurn = IsMyTurn(t_current);
            // 선공 배너는 인트로(PlayIntroAndStart)에서 이미 재생 → 첫 턴만 스킵.
            if (!t_skipBanner && this.ctx.turnBanner != null)
            {
                SoundManager.Instance?.PlayTurnChange();
                await this.ctx.turnBanner.Play(t_isMyTurn);
            }
            t_skipBanner = false;

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

            // [TurnEnded] 이번 턴 필드의 라이브 카드마다 패시브 → 시너지 순(유산 legacyStack++ 등).
            // 동기 void, RNG 미소비. CheckGameOver 전에 인라인 완결.
            foreach (var t_c in t_field.GetActiveCards())
            {
                var t_endedCtx = new TurnCtx(t_c, t_field);
                t_c.data.passive?.OnTurnEnded(t_endedCtx);
                SynergyTriggers.TurnEnded(t_endedCtx);
            }

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

    /// <summary>선공(플레이어 먼저)인가. 멀티=기존 고정, 튜토리얼=스크립트 전제(플레이어 선공 고정),
    /// 일반 싱글(AI전)=MatchRandom 코인. MatchRandom은 StartBattle에서 이미 시드됨.</summary>
    bool DecideFirstPlayer()
    {
        if (DeckConfig.IsMultiplayer) return true;   // 멀티(일시중지): 기존 동작 유지
        if (TutorialConfig.IsActive)  return true;   // 튜토리얼: 플레이어 선공 고정(스크립트 순서 전제)
        return MatchRandom.Range(2) == 0;            // 일반 싱글: 코인 랜덤(0=플레이어 선공)
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

        // 표시용 랭크: 전투 결과로 포인트 가감. 보상 영속 뒤라 랭크가 실패해도 골드 안전.
        var t_rank = RankManager.ApplyBattleResult(_won);
        this.lastRankDelta = t_rank.Delta;

        // 티어가 올랐으면 캐리어에 실어 둔다 — 로비 진입 시 보상 패널이 소비해 자동으로 열린다.
        if (t_rank.IsTierUp) RankUpHandoff.Set(t_rank);
    }

    public static void Cleanup()
    {
        TurnEvents.Reset();
        MatchRandom.Reset();
        TutorialConfig.End();   // 씬 종료 시 튜토리얼 해제(다음 일반 전투로 누수 방지)
        TurnCount = 1;
    }

    void HandlePlayerLeft(PlayerRef _p)
    {
        if (!DeckConfig.IsMultiplayer) return;
        this.disconnectWin = true;
        NetworkGameController.Instance?.ForceOpponentReady();
        MultiplayerTurnRunner.Instance?.ForceOpponentAttackResolve();
        CaptureResult(true);
        this.winPopup?.Show(this.lastRewardGold, this.lastRankDelta, _won: true);
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
        this.winPopup?.Show(this.lastRewardGold, this.lastRankDelta, _won: true);
    }

    bool CheckGameOver()
    {
        if (this.enemyField.IsEmpty)
        {
            CaptureResult(true);
            this.winPopup?.Show(this.lastRewardGold, this.lastRankDelta, _won: true);
            return true;
        }
        if (this.playerField.IsEmpty)
        {
            CaptureResult(false);
            this.losePopup?.Show(this.lastRewardGold, this.lastRankDelta, _won: false);
            return true;
        }
        return false;
    }
}
