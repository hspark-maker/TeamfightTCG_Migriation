using System;
using System.Collections.Generic;
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
    TurnRuleContext ruleCtx;
    TurnViewContext viewCtx;
    BattleLoop battleLoop;
    BattleOutcome battleOutcome;
    bool aiTakeoverPending;
    bool aiTakeoverFillPending;
    bool forcedEnd;      // 항복/디버그로 결과를 강제 확정했는가. 턴 루프를 다음 경계에서 끊는다.
    bool resultFinalized;// 결과 표시 경로에 진입했는가. 여운·팝업이 두 번 돌지 않게 하는 게이트.
    CurrencyGain lastReward; // CaptureResult에서 확정한 지급분. F-20 팝업 표시용(표시만, 재지급 없음).
    long lastRankDelta;  // CaptureResult에서 확정한 랭크 포인트 증감(클램프 반영). 팝업 표시용(표시만).

    // 보상을 만든 생존 카드 스냅샷. 여운이 도는 동안 필드가 정리돼도 흔들리지 않게 값으로 잡아 둔다.
    List<int> lastSurvivorCards;

    // 이번 판에 잃은 카드 스냅샷. 보상에는 관여하지 않고 결과 화면의 분모("몇 장 중")만 만든다.
    List<int> lastFallenCards;

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
    /// 멀티는 <see cref="EMatchEndReason.Surrender"/> 메시지로 상대에게 승리를 알린 뒤 러너를 내린다 —
    /// 명령 로그를 도입하면서 이탈-부전승 경로에 기대던 방식을 버렸다. 양쪽이 <b>같은 Surrender 명령</b>을
    /// 기록해야 로그가 일치하는데, 이탈 경로로 넘어가면 항복자만 명령 1개를 더 갖게 되기 때문이다.</summary>
    public void Surrender()
    {
        if (this.resultFinalized) return;
        int t_actorOwner = ResolveLocalOwnerForCommand();
        BattleCommandLog.RecordSurrender(t_actorOwner);
        if (DeckConfig.IsMultiplayer)
        {
            NetworkGameController.Instance?.SendSurrender(t_actorOwner);
            DisconnectAfterSurrender().Forget();
        }
        ForceEnd(false, EMatchEndReason.Surrender);
    }

    /// <summary>명령 로그에 실을 로컬 ownerIndex. 멀티 초기화가 덜 끝나 <c>MyOwnerIndex</c>가 -1인
    /// 상태를 그대로 흘리면 양쪽 로그가 갈리므로 여기서 한 번 걸러낸다.</summary>
    int ResolveLocalOwnerForCommand()
    {
        int t_owner = MultiplayerTurnRunner.Instance?.MyOwnerIndex ?? -1;
        if (t_owner < 0) t_owner = TurnState.LocalOwnerIndex;
        return t_owner;
    }

    public void HandleRemoteSurrender(int _actorOwner)
    {
        if (this.resultFinalized) return;
        BattleCommandLog.RecordSurrender(_actorOwner);
        ForceEnd(true, EMatchEndReason.Surrender);
    }

    /// <summary>항복 메시지가 신뢰 전송으로 빠져나갈 시간을 준 뒤 러너를 내린다.
    /// 한 프레임으로는 부족하다 — 플러시 전에 끊기면 상대가 Surrender 대신 이탈 경로를 타고
    /// <see cref="HandleRemoteSurrender"/>를 부르지 않아 명령 로그가 한 개 어긋난다
    /// (그러면 서버가 command_log_mismatch로 양쪽 지급을 막는다).</summary>
    async UniTaskVoid DisconnectAfterSurrender()
    {
        try
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(NetTimeouts.SurrenderFlushSec),
                ignoreTimeScale: true,
                cancellationToken: this.GetCancellationTokenOnDestroy());
        }
        catch (OperationCanceledException) { /* 씬이 먼저 내려갔다 — 러너는 씬 정리가 내린다 */ return; }

        NetworkSession.Instance?.Disconnect().Forget();
    }

#if UNITY_EDITOR
    /// <summary>디버그 강제 승리. 에디터 전용 — 빌드에는 이 심볼 자체가 없다.
    /// 멀티에서는 이쪽 화면만 끝나고 상대는 계속 진행한다(디버그 용도라 동기화하지 않는다).</summary>
    public void DebugForceWin() => ForceEnd(true, EMatchEndReason.DebugForceWin);
#endif

    void ForceEnd(bool _won, EMatchEndReason _reason)
    {
        if (this.resultFinalized) return;   // 이미 승패 확정 — 보상 재지급·팝업 덮어쓰기 방지

        this.forcedEnd = true;
        // 강제 종료에는 여운을 붙이지 않는다 — 항복·디버그 승리는 화면에 강조할 "결정타"가 없다.
        FinalizeResult(_won, _reason);

        if (!_won && DeckConfig.IsMultiplayer && _reason != EMatchEndReason.Surrender)
            NetworkSession.Instance?.Disconnect().Forget();
    }

    /// <summary>이번 전투 결과를 확정하고 표시한다 — <b>결과가 화면에 나가는 유일한 출구</b>.
    /// 정상 승패·항복·전투 중 이탈·초기화 중 이탈이 전부 여기로 모인다. 두 경로가 경합해도 먼저 온 쪽만 이긴다
    /// (보상은 CaptureResult가, 팝업·여운은 이 게이트가 각각 한 번만 돌게 막는다).
    /// <paramref name="_reason"/>이 정상 승패면 팝업 앞에 승패 여운(<see cref="BattleResultBeat"/>)을 한 박자 넣는다.</summary>
    void FinalizeResult(bool _won, EMatchEndReason _reason, bool _delayVoidExit = false)
    {
        if (this.resultFinalized) return;
        this.resultFinalized = true;

        TurnState.InputAllowed = false;    // 결과 팝업 뒤에서 공격이 계속 나가지 않게
        if (_reason.IsVoid())
        {
            // 무효 경기는 씬 전환이 커버 연출을 태우느라 1초 넘게 걸린다. 그동안 전투 씬은 살아 있으므로
            // 턴 루프를 여기서 끊지 않으면 커버 아래에서 공격이 더 나간다(멀티는 그게 그대로 상대에게 간다).
            // 강제 종료와 같은 플래그를 쓴다 — BattleLoop의 탈출 조건은 이 하나만 본다.
            this.forcedEnd = true;
            if (_delayVoidExit) LoadVoidResultNextFrame().Forget();
            else                BattleCleanup.LoadScene(LobbySceneName);
            return;
        }

        if (_reason.GrantsReward())
            CaptureResult(_won, _reason);
        BattleGoldenRecorder.Finish(_won, _reason == EMatchEndReason.Draw);
        ShowResult(_won, _reason.PlaysBeat()).Forget();
    }

    async UniTaskVoid LoadVoidResultNextFrame()
    {
        // ReliableData를 큐에 넣은 프레임에 Runner를 내리면 MatchAbort가 상대에게 도착하기 전에 유실될 수 있다.
        await UniTask.NextFrame();
        BattleCleanup.LoadScene(LobbySceneName);
    }

    /// <summary>로컬에서 감지한 통신 실패를 상대에게 알리고 같은 무효 사유로 종료한다.</summary>
    public void AbortMatch(EMatchEndReason _reason)
    {
        if (!_reason.IsVoid())
        {
            Debug.LogError($"[Net] 무효 경기 사유가 아닌 값으로 AbortMatch를 요청했다: {_reason}");
            return;
        }
        if (this.resultFinalized) return;

        TurnState.InputAllowed = false;
        try
        {
            NetworkGameController.Instance?.SendMatchAbort(_reason);
        }
        catch (Exception t_e)
        {
            // 전송 실패가 수신 콜백의 예외 처리로 다시 들어가 AbortMatch가 재귀하지 않게 여기서 끊는다.
            Debug.LogError($"[Net] MatchAbort 전송 실패 — 로컬 무효 종료는 계속한다: {t_e}");
        }
        FinalizeResult(false, _reason, _delayVoidExit: true);
        ReleaseNetworkWaits();
    }

    /// <summary>상대가 먼저 확정한 무효 경기 사유를 재전송 없이 적용한다.</summary>
    public void HandleMatchAbort(EMatchEndReason _reason)
    {
        if (!_reason.IsVoid())
        {
            Debug.LogError($"[Net] 상대가 잘못된 MatchAbort 사유를 보냈다: {_reason}");
            return;
        }
        if (this.resultFinalized) return;

        TurnState.InputAllowed = false;
        FinalizeResult(false, _reason);
        ReleaseNetworkWaits();
    }

    void ReleaseNetworkWaits()
    {
        NetworkGameController.Instance?.ForceOpponentReady();
        NetworkGameController.Instance?.ForceOpponentMulliganChoice();
        MultiplayerTurnRunner.Instance?.AbortNetworkWaits();
    }

    // 여운은 표시 전용이라 결과·보상 확정 뒤에 돈다 — 도중에 씬이 내려가면 취소되고 팝업도 뜨지 않는다.
    async UniTaskVoid ShowResult(bool _won, bool _withBeat)
    {
        if (_withBeat)
            await BattleResultBeat.Play(_won, this.destroyCt);

        GameResultPopup t_popup = _won ? this.winPopup : this.losePopup;
        t_popup?.Show(this.lastReward, this.lastRankDelta, _won,
            this.lastSurvivorCards, this.lastFallenCards);
    }

#if UNITY_EDITOR
    // 미리보기 생존 장수. static이라 씬을 다시 켜도 마지막으로 보던 장수가 유지된다.
    static int s_previewSurvivors = 3;

    void Update()
    {
        // F3/F4로 생존 장수를 바꾸고 F1/F2로 그 장수로 재생한다.
        // 패배(F2)는 설계상 분출·롤링이 없다 — 값만 박힌 채 뜨는 게 정상이다.
        if (Input.GetKeyDown(KeyCode.F3)) SetPreviewSurvivors(s_previewSurvivors + 1);
        if (Input.GetKeyDown(KeyCode.F4)) SetPreviewSurvivors(s_previewSurvivors - 1);
        if (Input.GetKeyDown(KeyCode.F1)) PreviewResult(true).Forget();
        if (Input.GetKeyDown(KeyCode.F2)) PreviewResult(false).Forget();
    }

    void SetPreviewSurvivors(int _count)
    {
        s_previewSurvivors = Mathf.Clamp(_count, 0, BattleField.SLOT_COUNT * 2);
        Debug.Log($"[결과 미리보기] 생존 {s_previewSurvivors}장 → "
                + $"{RewardService.CalculateReward(true, s_previewSurvivors).Amount} 골드");
    }

    // 여운까지 포함한 미리보기. 결과를 확정하지 않으므로(CaptureResult 미호출) 보상·랭크는 건드리지 않는다 —
    // 전투를 끝까지 돌리지 않고도 여운 타이밍을 튜닝하려면 이 경로가 정상 경로와 같은 연출을 타야 한다.
    // 금액도 하드코딩하지 않는다. CalculateReward는 지급하지 않는 순수 함수라, 재화를 건드리지 않으면서
    // 계단 수(생존 장수)와 총액이 실제 공식대로 맞물리는지까지 함께 검증된다.
    async UniTaskVoid PreviewResult(bool _won)
    {
        var t_reward = RewardService.CalculateReward(_won, s_previewSurvivors);
        List<int> t_cards  = BuildPreviewCards(s_previewSurvivors);
        List<int> t_fallen = BuildPreviewFallen(t_cards);

        await BattleResultBeat.Play(_won, this.destroyCt);
        GameResultPopup t_popup = _won ? this.winPopup : this.losePopup;
        t_popup?.Show(t_reward, _won ? 10 : -5, _won, t_cards, t_fallen);
    }

    // 미리보기 전사 목록: 덱에서 생존 목록에 없는 카드를 담는다. 장수로 자르면 같은 카드가
    // 산 채로도 죽은 채로도 한 줄에 서서, 실제 전투에서는 나올 수 없는 그림으로 연출을 튜닝하게 된다.
    List<int> BuildPreviewFallen(List<int> _survivors)
    {
        var t_cards = new List<int>();
        var t_deck  = DeckConfig.PlayerDeck;
        if (t_deck == null) return t_cards;

        for (int t_i = 0; t_i < t_deck.Count; t_i++)
            if (!_survivors.Contains(t_deck[t_i])) t_cards.Add(t_deck[t_i]);

        return t_cards;
    }

    // 실제 생존 카드 → 플레이어 덱 → 빈 자리 순으로 채운다. 마지막 폴백은 카드 없는 타일 경로까지 함께 검증한다.
    List<int> BuildPreviewCards(int _count)
    {
        var t_cards = new List<int>(_count);

        List<CardInstance> t_active = this.playerField != null ? this.playerField.GetActiveCards() : null;
        var t_deck = DeckConfig.PlayerDeck;

        for (int t_i = 0; t_i < _count; t_i++)
        {
            if (t_active != null && t_i < t_active.Count)
                t_cards.Add(t_active[t_i]?.cardId ?? 0);
            else if (t_deck != null && t_i < t_deck.Count)
                t_cards.Add(t_deck[t_i]);
            else
                t_cards.Add(0);
        }

        return t_cards;
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
        BattleCommandLog.Reset();
        BattleGoldenRecorder.Reset();
        TurnCount = 1;
        SetTurnCountLabel();
        // 정상 경로의 시드 지점은 GameInitializer(덱 셔플이 MatchRandom을 소비하므로 필드 초기화 직전).
        // 여기 남은 건 StartBattle() 단독 호출 같은 우회 진입용 폴백 — 이미 시드됐으면 손대지 않는다.
        // (덮어쓰면 셔플로 이미 전진한 스트림이 리셋돼 시드 하나가 두 시퀀스를 내게 된다.)
        // 멀티는 SyncInitialDecks의 commit-reveal이 시드하므로 여기서 제외.
        MatchSeeding.EnsureSeeded();
        if (DeckConfig.IsMultiplayer && NetworkSession.Instance != null)
            NetworkSession.Instance.OnPlayerLeftRoom += HandlePlayerLeft;
        this.ruleCtx = new TurnRuleContext
        {
            playerField     = this.playerField,
            enemyField      = this.enemyField,
        };
        this.viewCtx = new TurnViewContext
        {
            playerFieldView = this.playerFieldView,
            enemyFieldView  = this.enemyFieldView,
            turnLabel       = this.turnLabel,
            playerDeckUI    = this.playerDeckUI,
            enemyDeckUI     = this.enemyDeckUI,
            turnBanner       = this.turnBanner,
            mulliganOverlay  = this.mulliganOverlay,
        };
        this.ctx = new TurnContext(this.ruleCtx, this.viewCtx);
        // 이미 있으면 그대로 둔다 — 여기서 새로 만들면 확정 게이트(IsCaptured)가 지워진다.
        // 초기화 중 설정창 항복처럼 인트로보다 먼저 결과가 확정되는 경로가 있다(CaptureResult의 lazy 폴백).
        // resultFinalized도 같은 걸 막지만, 두 게이트가 각각 한 번씩 막는 게 원래 구조다.
        if (this.battleOutcome == null) this.battleOutcome = new BattleOutcome(this.ruleCtx);

        // 선/후공은 owner 기준이다. 멀티 양쪽은 같은 합의 시드에서 같은 RNG 1회를 소비한다.
        int t_first = BattleLoop.DecideFirstOwner(TutorialConfig.IsActive);
        BattleGoldenRecorder.Begin(this.playerField, this.enemyField, t_first);

        // 코인 토스 전에는 턴 정보(배경+레이블) 숨김(선/후공 미정 상태). 배너 GO에 배경 스프라이트+WhosTurn 라벨이 함께 있음.
        GameObject t_turnInfo = this.turnBanner != null ? this.turnBanner.gameObject
                              : (this.turnLabel != null ? this.turnLabel.gameObject : null);
        if (t_turnInfo != null) t_turnInfo.SetActive(false);

        // (1) 코인 토스 — owner 기준 결과를 각 기기의 로컬 관점(내 선공 여부)으로 표시한다.
        if (this.coinFlip != null)
        {
            this.coinFlip.gameObject.SetActive(true);
            await this.coinFlip.Play(IsMyTurn(t_first));
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

        // (3.5) 후공 어드밴티지 멀리건 — 첫 턴 시작 전, 보드가 채워진 뒤.
        if (this.destroyCt.IsCancellationRequested) return;   // 딜 도중 씬 전환 — 멀리건·턴 루프를 시작하지 않는다
        await MulliganPhase.Run(this.ctx, t_first, this.destroyCt);

        // 멀리건 RPC 상한이 무효 경기를 확정했거나 씬이 내려간 경우 턴 루프를 새로 시작하지 않는다.
        if (this.destroyCt.IsCancellationRequested || this.resultFinalized || this.forcedEnd) return;

        // (4) 턴 루프(선공 배너 재생 완료 → 첫 턴 배너 스킵).
        RunBattleLoop(t_first, _skipFirstBanner: true).Forget();
    }

    // 해당 t_current가 로컬(내) 턴인가. 멀티에서 P2는 t_current=1이 내 턴.
    // 판정은 TurnState.LocalOwnerIndex 하나 — 멀티는 GameInitializer가 MyOwnerIndex로 채우고 싱글은 0이다.
    static bool IsMyTurn(int _current) => TurnState.IsLocalTurn(_current);

    async UniTask RunBattleLoop(int _startCurrent, bool _skipFirstBanner)
    {
        bool t_skipBanner = _skipFirstBanner;
        this.battleLoop = new BattleLoop(this.ruleCtx, _startCurrent);

        EBattleLoopEnd t_end = await this.battleLoop.Run(
            async t_current =>
            {
                this.viewCtx.RefreshViews();

                bool t_isMyTurn = IsMyTurn(t_current);
                if (!t_skipBanner && this.viewCtx.turnBanner != null)
                {
                    SoundManager.Instance?.PlayTurnChange();
                    await this.viewCtx.turnBanner.Play(t_isMyTurn);
                }
                t_skipBanner = false;

                if (this.aiTakeoverFillPending)
                {
                    this.aiTakeoverFillPending = false;
                    TurnFillResult t_filled = this.ruleCtx.FillSlots();
                    await this.viewCtx.AnimateFilled(t_filled);
                }

                TurnBase t_turn;
                if (DeckConfig.IsMultiplayer && !DeckConfig.AiTakeover)
                {
                    t_turn = t_isMyTurn
                        ? (TurnBase)new MultiplayerPlayerTurn(this.ctx)
                        : new MultiplayerOpponentTurn(this.ctx);
                }
                else
                {
                    t_turn = t_isMyTurn
                        ? (TurnBase)new PlayerTurn(this.ctx)
                        : new EnemyTurn(this.ctx);
                }

                this.battleLoop.ActiveTurn = t_turn as IAiTakeoverContinuable;
                t_turn.OnEnter();
                await t_turn.Execute();
                t_turn.OnExit();
                this.battleLoop.ActiveTurn = null;
            },
            () => this.forcedEnd,
            t_owner =>
            {
                if (DeckConfig.IsMultiplayer) LogDeterminismHash(t_owner);
            },
            BattleResultBeat.AbortFinish,
            t_count =>
            {
                TurnCount = t_count;
                SetTurnCountLabel();
            });

        if (t_end == EBattleLoopEnd.PlayerWon)
            FinalizeResult(true, EMatchEndReason.Normal);
        else if (t_end == EBattleLoopEnd.PlayerLost)
            FinalizeResult(false, EMatchEndReason.Normal);
        else if (t_end == EBattleLoopEnd.Draw)
            FinalizeResult(false, EMatchEndReason.Draw);   // 승자 없음 — 골드만, 랭크는 그대로
    }
    /// <summary>턴 끝 보드 지문을 로그로 남긴다. 계산은 <see cref="BattleStateHash"/> 단독 —
    /// 여기서 접는 방식을 따로 두면 로그 해시와 실제로 교환하는 해시가 갈려 대조가 무의미해진다.</summary>
    void LogDeterminismHash(int _actingOwner)
    {
        ulong t_hash = BattleStateHash.Compute(this.playerField.State, this.enemyField.State);
        BattleGoldenRecorder.RecordCheckpoint(TurnCount, _actingOwner, t_hash);
        Debug.Log($"[Hash] turn={TurnCount} owner={_actingOwner} board=0x{t_hash:X16} draws={MatchRandom.DrawCount}");
    }

    void SetTurnCountLabel()
    {
        if (this.turnCountLabel != null)
            this.turnCountLabel.text = $"{TurnCount} 턴";
    }

    // 승패 확정 시점의 보상·랭크·제출은 BattleOutcome이 한 번만 수행한다.
    void CaptureResult(bool _won, EMatchEndReason _reason)
    {
        if (this.battleOutcome == null)
        {
            this.ruleCtx = this.ruleCtx ?? new TurnRuleContext
            {
                playerField = this.playerField,
                enemyField = this.enemyField,
            };
            this.battleOutcome = new BattleOutcome(this.ruleCtx);
        }

        if (!this.battleOutcome.TryCapture(_won, _reason)) return;

        this.lastReward = this.battleOutcome.Reward;
        this.lastRankDelta = this.battleOutcome.RankDelta;
        this.lastSurvivorCards = this.battleOutcome.SurvivorCards;
        this.lastFallenCards = this.battleOutcome.FallenCards;
    }

    public static void Cleanup()
    {
        TurnEvents.Reset();
        MatchRandom.Reset();
        BattleCommandLog.Reset();    // 명령 로그도 같은 자리에서 수명을 끊는다(제출은 이미 값을 복사해 갔다).
        BattleGoldenRecorder.Reset();
        TutorialConfig.End();        // 씬 종료 시 튜토리얼 해제(다음 일반 전투로 누수 방지)
        AdventureRun.End();         // 모험 정점도 같은 수명 — 남으면 다음 판 AI 레벨·랭크 정산이 정점 규칙으로 굳는다.
        DeckConfig.ResetMode();      // 멀티 플래그도 같은 자리에서 해제 — 두 모드 플래그의 수명 규율을 하나로.
        DeckConfig.ClearEnemyDeck(); // 상대 덱을 확정하지 않는 진입점이 직전 판의 상대를 물려받지 않게(같은 규율).
        MatchOpponentHandoff.Clear();// 매칭 상대 표시도 같은 수명 — 덱만 비우면 다음 판 화면에 직전 상대 이름이 남는다.
        SoloMatchHandoff.Clear();    // 서버 시드도 한 판짜리다 — 남으면 검증을 건너뛴 다음 판이 같은 보드로 선다.
        TurnCount = 1;
    }

    void HandlePlayerLeft(PlayerRef _p)
    {
        if (!DeckConfig.IsMultiplayer) return;
        if (DeckConfig.AiTakeover || this.aiTakeoverPending || this.resultFinalized) return;

        TurnState.InputAllowed = false;
        if (NetTimeouts.OpponentDropGraceSec <= 0f)
        {
            BeginAiTakeover();
            return;
        }

        this.aiTakeoverPending = true;
        BeginAiTakeoverAfterGrace().Forget();
    }

    async UniTaskVoid BeginAiTakeoverAfterGrace()
    {
        await UniTask.Delay(TimeSpan.FromSeconds(NetTimeouts.OpponentDropGraceSec),
                            ignoreTimeScale: true,
                            cancellationToken: this.destroyCt)
                     .SuppressCancellationThrow();

        if (this.destroyCt.IsCancellationRequested || this.resultFinalized || DeckConfig.AiTakeover) return;
        BeginAiTakeover();
    }

    void BeginAiTakeover()
    {
        this.aiTakeoverPending = false;
        DeckConfig.SetAiTakeover(true);
        if (!DeckConfig.AiTakeover) return;

        int t_myOwner = MultiplayerTurnRunner.Instance?.MyOwnerIndex ?? TurnState.LocalOwnerIndex;
        BattleCommandLog.RecordAiTakeover(1 - t_myOwner);
        // 인수 뒤 AI가 두는 수는 목격자가 하나뿐이라 대조에 쓸 수 없다. 여기서 기록을 멈춰
        // 로그가 무의미하게 커지는 것을 막는다(쌓인 로그 자체는 유효하게 남는다).
        BattleCommandLog.Freeze();

        TurnState.InputAllowed = false;
        this.aiTakeoverFillPending = true;

        // 공격 대기 TCS는 continuation을 동기 호출한다. 인수 플래그를 먼저 세운 뒤 깨워야 Timeout으로 재진입하지 않는다.
        MultiplayerTurnRunner.Instance?.PrepareAiTakeover();
        NetworkGameController.Instance?.ForceOpponentReady();
        NetworkGameController.Instance?.ForceOpponentMulliganChoice();
        this.battleLoop?.ActiveTurn?.ContinueAfterAiTakeover();

        Debug.Log("[Net] 상대 이탈 — 기존 보드 상태를 유지한 채 AI가 전투를 인수한다.");
    }

    /// <summary>초기화(StartBattle 이전)가 실패했을 때 GameInitializer가 부르는 출구.
    /// 사유가 상한 초과든 상대 이탈이든 <b>전부 무효 경기</b>다 — 보드가 아직 서지 않아 승패를 매길 판이 없고,
    /// AI가 인수할 상태도 없다. 여기서 CaptureResult를 부르면 골드·랭크가 실제로 지급되고,
    /// 양쪽이 동시에 타임아웃 나면 둘 다 보상을 받아 랭크가 부풀어 오른다. 결과 없이 로비로 돌려보낸다.</summary>
    public void HandleInitFailed(EMatchEndReason _reason)
    {
        Debug.LogError($"[MultiInit] 초기화 실패({_reason}) — 결과·보상 없이 로비로 복귀한다.");
        AbortMatch(_reason);
    }

    const string LobbySceneName = "LobbyScene";

}
