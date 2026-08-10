using System;
using System.Threading;
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
    [SerializeField] MulliganOverlayUI mulliganOverlay;   // 멀리건 안내 화면(씬에 꺼진 채로 놓인 프리팹 인스턴스)
    [SerializeField] CoinFlipUI      coinFlip;       // 선/후공 결정 연출(싱글 AI전 전용, 비우면 스킵)
    [SerializeField] GameResultPopup winPopup;
    [SerializeField] GameResultPopup losePopup;

    /// <summary>전투 씬에 하나. 설정 창(항복/디버그 승리)처럼 씬 배선이 없는 UI가 찾아오는 진입점.
    /// null이면 지금 전투 중이 아니다 — UI는 이걸로 전투 전용 버튼 노출을 판정한다.</summary>
    public static TurnRunner Instance { get; private set; }

    TurnContext ctx;
    bool disconnectWin;
    bool forcedEnd;      // 항복/디버그로 결과를 강제 확정했는가. 턴 루프를 다음 경계에서 끊는다.
    bool resultCaptured; // 이번 전투 결과 확정 여부. 최초 승패만 보상 지급하고 이후 덮어쓰기 차단.
    bool resultFinalized;// 결과 표시 경로에 진입했는가. 여운·팝업이 두 번 돌지 않게 하는 게이트.
    CurrencyGain lastReward; // CaptureResult에서 확정한 지급분. F-20 팝업 표시용(표시만, 재지급 없음).
    long lastRankDelta;  // CaptureResult에서 확정한 랭크 포인트 증감(클램프 반영). 팝업 표시용(표시만).

    // 파괴 후 처음 읽으면 Unity가 MissingReferenceException을 던진다 — 씬 전환 중 재개하는 연출이 있으므로 살아 있을 때 잡아 둔다.
    CancellationToken destroyCt;

    void Awake()
    {
        Instance = this;
        this.destroyCt = this.GetCancellationTokenOnDestroy();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (NetworkSession.Instance != null)
            NetworkSession.Instance.OnPlayerLeftRoom -= HandlePlayerLeft;
    }

    /// <summary>항복 = 즉시 패배 확정. 보상·랭크는 정상 패배와 같은 경로(CaptureResult)를 탄다.
    /// 멀티는 러너를 내려 상대에게 <b>기존 이탈-부전승 경로</b>(OnPlayerLeftRoom)로 승리를 준다 —
    /// 항복 전용 와이어 메시지를 새로 만들지 않는다(프로토콜 추가 0, 결과 동일).</summary>
    public void Surrender() => ForceEnd(false);

#if UNITY_EDITOR
    /// <summary>디버그 강제 승리. 에디터 전용 — 빌드에는 이 심볼 자체가 없다.
    /// 멀티에서는 이쪽 화면만 끝나고 상대는 계속 진행한다(디버그 용도라 동기화하지 않는다).</summary>
    public void DebugForceWin() => ForceEnd(true);
#endif

    void ForceEnd(bool _won)
    {
        if (this.resultFinalized) return;   // 이미 승패 확정 — 보상 재지급·팝업 덮어쓰기 방지

        this.forcedEnd = true;
        // 강제 종료에는 여운을 붙이지 않는다 — 항복·디버그 승리는 화면에 강조할 "결정타"가 없다.
        FinalizeResult(_won, _withBeat: false);

        if (!_won && DeckConfig.IsMultiplayer)
            NetworkSession.Instance?.Disconnect().Forget();
    }

    /// <summary>이번 전투 결과를 확정하고 표시한다 — <b>결과가 화면에 나가는 유일한 출구</b>.
    /// 정상 승패·항복·전투 중 이탈·초기화 중 이탈이 전부 여기로 모인다. 두 경로가 경합해도 먼저 온 쪽만 이긴다
    /// (보상은 CaptureResult가, 팝업·여운은 이 게이트가 각각 한 번만 돌게 막는다).
    /// <paramref name="_withBeat"/>면 팝업 앞에 승패 여운(<see cref="BattleResultBeat"/>)을 한 박자 넣는다.</summary>
    void FinalizeResult(bool _won, bool _withBeat)
    {
        if (this.resultFinalized) return;
        this.resultFinalized = true;

        TurnState.InputAllowed = false;    // 결과 팝업 뒤에서 공격이 계속 나가지 않게
        CaptureResult(_won);
        ShowResult(_won, _withBeat).Forget();
    }

    // 여운은 표시 전용이라 결과·보상 확정 뒤에 돈다 — 도중에 씬이 내려가면 취소되고 팝업도 뜨지 않는다.
    async UniTaskVoid ShowResult(bool _won, bool _withBeat)
    {
        if (_withBeat)
            await BattleResultBeat.Play(_won, this.destroyCt);

        GameResultPopup t_popup = _won ? this.winPopup : this.losePopup;
        t_popup?.Show(this.lastReward, this.lastRankDelta, _won);
    }

#if UNITY_EDITOR
    void Update()
    {
        // 연출 확인용 샘플 보상 — 0이면 코인·수치 롤링이 통째로 생략돼 볼 게 없다.
        // 패배(F2)는 설계상 분출·롤링이 없다 — 값만 박힌 채 뜨는 게 정상이다.
        if (Input.GetKeyDown(KeyCode.F1)) PreviewResult(true).Forget();
        if (Input.GetKeyDown(KeyCode.F2)) PreviewResult(false).Forget();
    }

    // 여운까지 포함한 미리보기. 결과를 확정하지 않으므로(CaptureResult 미호출) 보상·랭크는 건드리지 않는다 —
    // 전투를 끝까지 돌리지 않고도 여운 타이밍을 튜닝하려면 이 경로가 정상 경로와 같은 연출을 타야 한다.
    async UniTaskVoid PreviewResult(bool _won)
    {
        await BattleResultBeat.Play(_won, this.destroyCt);
        GameResultPopup t_popup = _won ? this.winPopup : this.losePopup;
        t_popup?.Show(new CurrencyGain(ECurrencyType.Gold, 1234), _won ? 10 : -5, _won);
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
        // 정상 경로의 시드 지점은 GameInitializer(덱 셔플이 MatchRandom을 소비하므로 필드 초기화 직전).
        // 여기 남은 건 StartBattle() 단독 호출 같은 우회 진입용 폴백 — 이미 시드됐으면 손대지 않는다.
        // (덮어쓰면 셔플로 이미 전진한 스트림이 리셋돼 시드 하나가 두 시퀀스를 내게 된다.)
        // 멀티는 SyncInitialDecks의 commit-reveal이 시드하므로 여기서 제외.
        MatchSeeding.EnsureSeeded();
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
            mulliganOverlay  = this.mulliganOverlay,
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
            // 연출 대기 중 씬이 내려갔으면 아래는 전부 파괴된 오브젝트를 만진다.
            if (this.destroyCt.IsCancellationRequested) return;
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
        if (this.destroyCt.IsCancellationRequested) return;   // 딜 도중 씬 전환 — 멀리건·턴 루프를 시작하지 않는다
        await MulliganPhase.Run(this.ctx, t_first, this.destroyCt);

        // (4) 턴 루프(선공 배너 재생 완료 → 첫 턴 배너 스킵).
        RunTurns(t_first, _skipFirstBanner: true).Forget();
    }

    // 해당 t_current가 로컬(내) 턴인가. 멀티에서 P2는 t_current=1이 내 턴.
    // 판정은 TurnState.LocalOwnerIndex 하나 — 멀티는 GameInitializer가 MyOwnerIndex로 채우고 싱글은 0이다.
    static bool IsMyTurn(int _current) => TurnState.IsLocalTurn(_current);

    async UniTask RunTurns(int _startCurrent, bool _skipFirstBanner)
    {
        int  t_current    = _startCurrent;
        bool t_skipBanner = _skipFirstBanner;

        while (true)
        {
            // playerField/enemyField는 멀티에서 기기마다 ownerIndex가 다르므로 ownerIndex로 조회한다.
            // 싱글도 playerField.OwnerIndex가 0이라 같은 식이 그대로 맞는다 — 모드 분기 불필요.
            BattleField t_field = this.playerField.OwnerIndex == t_current ? this.playerField : this.enemyField;

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

            if (this.disconnectWin || this.forcedEnd || CheckGameOver()) break;

            // 여기 왔다 = 판이 안 끝났다. 결정타 강조가 돌았었다면 그 판정이 틀린 것이므로 화면을 되돌린다
            // (흐림·클로즈업이 남은 채로 다음 턴이 시작되면 먹통으로 보인다). 안 돌았으면 무동작.
            BattleResultBeat.AbortFinish();

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
        this.lastReward = RewardService.GrantBattleReward(t_remaining);

        // 지급·영속은 위에서 끝났다 — 캐리어에는 로비 획득 연출이 쓸 표시량만 싣는다.
        BattleRewardHandoff.Set(this.lastReward);

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
        TutorialConfig.End();        // 씬 종료 시 튜토리얼 해제(다음 일반 전투로 누수 방지)
        DeckConfig.ResetMode();      // 멀티 플래그도 같은 자리에서 해제 — 두 모드 플래그의 수명 규율을 하나로.
        DeckConfig.ClearEnemyDeck(); // 상대 덱을 확정하지 않는 진입점이 직전 판의 상대를 물려받지 않게(같은 규율).
        TurnCount = 1;
    }

    void HandlePlayerLeft(PlayerRef _p)
    {
        if (!DeckConfig.IsMultiplayer) return;
        this.disconnectWin = true;
        NetworkGameController.Instance?.ForceOpponentReady();
        MultiplayerTurnRunner.Instance?.ForceOpponentAttackResolve();
        // 부전승에는 여운이 없다 — 강조할 결정타가 없고, 이탈은 전투가 진행 중일 때도 들어온다.
        FinalizeResult(true, _withBeat: false);
    }

    /// <summary>
    /// 초기화 단계(StartBattle 이전)에서 상대 이탈이 감지된 경우 GameInitializer가 호출.
    /// RunTurns가 아직 시작 전이므로 기존 이탈 시맨틱(부전승)과 일관되게 승리 팝업만 노출.
    /// StartBattle을 호출하지 않으므로 OnPlayerLeftRoom(HandlePlayerLeft) 구독도 발생하지 않아 이중 처리 없음.
    /// </summary>
    /// <summary>초기화가 **상한 초과**로 실패했을 때. 이탈 부전승과 반드시 구분한다 —
    /// 타임아웃은 내 쪽 문제(스테일 러너·러너 미기동)일 수 있고 상대는 멀쩡히 대기 중일 수 있다.
    /// 여기서 CaptureResult를 부르면 골드·랭크가 실제로 지급되고, 양쪽이 동시에 타임아웃 나면
    /// 둘 다 승리 보상을 받아 랭크가 부풀어 오른다. 결과 없이 로비로 돌려보낸다.</summary>
    public void HandleInitFailed()
    {
        Debug.LogError("[MultiInit] 초기화 상한 초과 — 결과·보상 없이 로비로 복귀한다.");
        BattleCleanup.LoadScene(LobbySceneName);
    }

    const string LobbySceneName = "LobbyScene";

    public void HandleOpponentLeftDuringInit()
    {
        if (!DeckConfig.IsMultiplayer) return;
        this.disconnectWin = true;
        FinalizeResult(true, _withBeat: false);
    }

    bool CheckGameOver()
    {
        // 정상 종료만 여운을 탄다. 여기까지 왔다는 건 이번 턴의 공격·사망 연출과 충원이 모두 끝났다는 뜻이라,
        // 여운은 "정리된 보드를 한 박자 붙잡았다가" 팝업을 여는 연출이 된다.
        if (this.enemyField.IsEmpty)
        {
            FinalizeResult(true, _withBeat: true);
            return true;
        }
        if (this.playerField.IsEmpty)
        {
            FinalizeResult(false, _withBeat: true);
            return true;
        }
        return false;
    }
}
