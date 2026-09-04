using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TeamfightTCG.BattleCore;
using UnityEngine;

/// <summary>
/// 내 턴 (멀티). PlayerTurn과 같은 흐름이나:
/// - 공격 결정 직후 RPC 브로드캐스트
/// - 내 field FillEmptySlots 후 스폰 브로드캐스트
/// - WaitForOpponentReady 이후 상대 스폰 반영 + fill anim
/// </summary>
public class MultiplayerPlayerTurn : TurnBase, IAiTakeoverContinuable
{
    CardInstance forcedAttacker;
    bool turnDone;
    bool attackRunning;

    public MultiplayerPlayerTurn(TurnContext _ctx) : base(_ctx) { }

    public override void OnEnter()
    {
        if (this.ctx.turnLabel != null) this.ctx.turnLabel.text = "내 턴";
        TurnState.LocalOwnerIndex = MultiplayerTurnRunner.Instance?.MyOwnerIndex ?? 0;
        TurnState.InputAllowed = true;
        CardView.OnAttack    += HandleCardViewAttack;
    }

    public override async UniTask Execute()
    {
        this.turnDone = false;
        // 생각시간 감시 기동. ct는 턴 수명(씬 파괴)에 묶고, turnDone 세팅 시 자연 종료.
        var t_ct = this.ctx.playerFieldView.GetCancellationTokenOnDestroy();
        TurnThinkTimer.Watch(GameTiming.Battle.TurnThinkTime, () => this.turnDone, ForceTimeoutAttack, t_ct).Forget();
        await UniTask.WaitUntil(() => this.turnDone);
    }

    public override void OnExit()
    {
        TurnState.InputAllowed   = false;
        TurnState.ForcedAttacker = null;
        TurnState.ForcedTarget   = null;
        TurnState.AllowedGesture = InputGesture.Any;
        CardView.ForcedDimAlpha  = 0.3f;
        CardView.RestoreAllFades();
        CardView.OnAttack      -= HandleCardViewAttack;
        this.ctx.ClearAllHighlights();
        this.forcedAttacker = null;
        this.attackRunning = false;
    }

    /// <summary>내 입력을 기다리던 중 상대가 이탈했다면 현재 턴은 그대로 플레이하게 한다.</summary>
    public void ContinueAfterAiTakeover()
    {
        if (this.turnDone || this.attackRunning) return;
        TurnState.InputAllowed = true;
    }

    void HandleCardViewAttack(CardView _attacker, CardView _target)
    {
        CardInstance t_attCard = _attacker.BoundCard;
        CardInstance t_defCard = _target.BoundCard;

        int t_myIndex = MultiplayerTurnRunner.Instance?.MyOwnerIndex ?? 0;
        if (t_attCard == null || t_attCard.ownerIndex != t_myIndex) return;
        if (t_defCard == null || t_defCard.ownerIndex == t_myIndex) return;
        if (this.forcedAttacker != null && t_attCard != this.forcedAttacker) return;

        // 규칙 백스톱: 도발 필터의 집행을 뷰(CardView.HandleEnemyTap)에만 맡기면 뷰를 우회한 입력이
        // 규칙을 깬다(멀티는 그 공격이 그대로 상대에게 브로드캐스트되므로 더 위험). 판정은 BattleRules 단독.
        if (!this.ctx.enemyField.CanAttack(t_attCard, t_defCard))
        {
            // 뷰는 이미 무장을 풀고 공격 연출용으로 VFX만 다시 켠 상태다. 여기서 거절하면
            // 그 VFX를 끌 주체(AttackSequence)가 안 돌아 공격자에 이펙트가 고착된다.
            return;
        }

        ExecuteAttackAsync(t_attCard, t_defCard).Forget();
    }

    /// <summary>
    /// 생각시간 초과 시 자동으로 합법 공격 1개를 수동 공격과 동일 경로로 실행.
    /// RNG 절대 미사용 — attacker/target 모두 slot 오름차순 첫 생존/유효로 결정론 선택.
    /// (공유 RNG 스트림 어긋나면 하드 divergence). turnDone은 여기서 세우지 않는다
    /// (ExecuteAttackAsync가 SendAttack 브로드캐스트 + 정상 resolve하며 세팅 = 단일 경로).
    /// </summary>
    void ForceTimeoutAttack()
    {
        if (!TurnState.InputAllowed) return;   // 이미 액션 시작됨 → 무시(원자성)

        // 선택을 먼저 — 유효 공격이 확정될 때만 입력을 차단한다.
        // (먼저 InputAllowed=false로 끄고 나서 유효공격이 없어 return하면 turnDone도 안 서고
        //  입력도 죽어 양측 hang. 순서를 뒤집어 그 위험 제거.)
        // Execution 재무장 창이면 ForcedAttacker 준수 필수(HandleCardViewAttack이 불일치 거절).
        CardInstance t_attacker = TurnState.ForcedAttacker;
        if (t_attacker == null)
        {
            var t_attackers = this.ctx.playerField.GetActiveCards();   // slot 오름차순
            if (t_attackers.Count > 0) t_attacker = t_attackers[0];
        }

        var t_targets = this.ctx.enemyField.GetValidTargets(t_attacker);   // 도발 우선 + slot 오름차순
        CardInstance t_target = t_targets.Count > 0 ? t_targets[0] : null;

        // 방어적: 유효 공격자/타깃 없으면 입력 유지한 채 반환(hang 방지, 다음 tick 재시도).
        if (t_attacker == null || !t_attacker.IsAlive || t_target == null) return;

        TurnState.InputAllowed = false;        // 유효 공격 확정 후에만 입력 차단
        DeckPileUI.CloseAny();                 // 덱을 열어둔 채 시간이 다 됐으면 닫는다(연출이 패널에 가리지 않게)
        CardView.RestoreAllFades();            // 드래그 잔상 정리
        ExecuteAttackAsync(t_attacker, t_target).Forget();   // 수동 공격과 100% 동일 경로
    }

    async UniTask ExecuteAttackAsync(CardInstance _attacker, CardInstance _defender)
    {
        this.attackRunning = true;
        TurnState.InputAllowed = false;

        bool t_cunningSwap = _attacker.HasKeyword(CardKeyword.Cunning)
                           && this.ctx.playerField.CanSwapWithWaiting(_attacker);

        // 공격 결정 브로드캐스트
        NetworkGameController.Instance?.SendAttack(_attacker.slotIndex, _defender.slotIndex, t_cunningSwap);

        CardView t_attackerView = this.ctx.playerFieldView.GetSlotView(_attacker.slotIndex);
        CardView t_defenderView = this.ctx.enemyFieldView.GetSlotView(_defender.slotIndex);

        var (t_preSelectedSplash, t_splashView) = AttackFlow.PreSelectSplash(
            _attacker, _defender, this.ctx.enemyField, this.ctx.enemyFieldView);

        bool t_derivedCommand = BattleUxFlags.ExecutionRandomTarget && ReferenceEquals(this.forcedAttacker, _attacker);

        await AttackFlow.RunBeforeAttack(_attacker, _defender, this.ctx.playerField, this.ctx.enemyField,
                                         t_preSelectedSplash);   // 낙인 선피해(Execute 전 원자)

        AttackResult t_result;
        using (BattleEventStream.CaptureScope t_events = BattleEventStream.BeginCapture())
        {
            t_result = AttackProcessor.Execute(
                _attacker, _defender, this.ctx.playerField.State, this.ctx.enemyField.State,
                t_preSelectedSplash, t_cunningSwap, t_derivedCommand);
            t_result.events = t_events.ToArray();
        }

        await AttackSequence.Play(t_attackerView, t_defenderView, t_splashView,
            t_result.events,
            () => AttackFlow.RunAfterAttackPhase(t_attackerView, _attacker, _defender, this.ctx.playerField, this.ctx.enemyField, t_result));

        // 교활 퇴장은 보충 **전**에 — 슬롯 뷰가 아직 물러나는 카드를 그리고 있는 동안만 가능하다.
        await AttackFlow.PlayCunningSwap(this.ctx.playerFieldView, t_attackerView, t_result);

        // 내 field만 로컬 채움 + 브로드캐스트
        List<CardInstance> t_playerPlaced = this.ctx.playerField.FillEmptySlots();
        MultiplayerTurnRunner.Instance?.BroadcastMySpawns(t_playerPlaced);

        this.ctx.playerFieldView.Refresh();
        this.ctx.playerDeckUI?.Refresh();
        await this.ctx.playerFieldView.PlayFillAnim(t_playerPlaced);

        // PlayDeathAnim이 alpha/scale을 1로 리셋하므로, 죽은 슬롯만 즉시 숨김
        // 전체 Refresh는 RPC로 미리 배치된 신규 카드까지 노출시키므로 사용 금지
        if (!_defender.IsAlive) t_defenderView.HideSlot();
        if (t_preSelectedSplash != null && !t_preSelectedSplash.IsAlive) t_splashView.HideSlot();

        // 연출 완료 신호 → 상대 완료 + 상대 스폰 RPC 전부 수신까지 대기
        List<CardInstance> t_takeoverPlaced = null;
        if (DeckConfig.AiTakeover)
        {
            t_takeoverPlaced = FillEnemyAfterTakeover();
        }
        else if (NetworkGameController.Instance != null)
        {
            bool t_ready = await NetworkGameController.Instance.WaitForOpponentReady();
            if (!t_ready)
            {
                if (DeckConfig.AiTakeover)
                {
                    // 대기를 깨운 이탈 콜백은 동기 continuation을 실행한다. 이미 계산된
                    // 처형 연속 공격을 버리지 말고, 이 안전 경계에서 상대 필드만 보충한 뒤 계속한다.
                    t_takeoverPlaced = FillEnemyAfterTakeover();
                }
                else
                {
                    this.turnDone = true;
                    TurnRunner.Instance?.AbortMatch(EMatchEndReason.Timeout);
                    return;
                }
            }
        }

        // 상대 스폰 반영 (OnCardSpawnReceived로 이미 enemyField에 배치됨)
        List<CardInstance> t_enemyPlaced = t_takeoverPlaced
                                           ?? MultiplayerTurnRunner.Instance?.FlushEnemySpawns()
                                           ?? new List<CardInstance>();
        this.ctx.enemyFieldView.Refresh();
        this.ctx.enemyDeckUI?.Refresh();
        await this.ctx.enemyFieldView.PlayFillAnim(t_enemyPlaced);

        // divergence 카나리아 스냅샷. **공격 해결 직후가 아니라 여기다.**
        // 상대 CardSpawn은 수신 즉시 enemyField에 반영되므로(PlaceCardDirectly), 보충 전에 뜨면
        // "상대 보충분이 도착했는가"가 회선 속도에 좌우돼 정상 경기에서도 지문이 갈린다.
        // 배리어를 통과하고 양쪽 보충이 모두 끝난 이 지점은 두 클라가 반드시 같은 보드다.
        // 이 지문은 **다음 배리어**에 실려 나간다(순번도 그때 것으로 맞춰진다).
        NetworkGameController.Instance?.StageStateHash(this.ctx.playerField.State, this.ctx.enemyField.State);

        if (t_result.canAttackAgain && this.ctx.enemyField.IsEmpty)
        {
            this.forcedAttacker     = null;
            TurnState.ForcedAttacker = null;
            CardView.RestoreAllFades();
            this.turnDone = true;
            return;
        }

        if (t_result.canAttackAgain)
        {
            this.forcedAttacker     = _attacker;
            TurnState.ForcedAttacker = _attacker;
            CardView.FadeTeam(0.3f, TurnState.LocalOwnerIndex);
            CardView.FadeCards(1f, t_attackerView);

            // 처형 재공격 = 무작위 대상 자동 발사(대상 선택 입력을 열지 않는다).
            // MatchRandom 소비 지점 — 상대 클라도 MultiplayerOpponentTurn에서 같은 자리에서 같은 횟수를 뽑는다.
            if (BattleUxFlags.ExecutionRandomTarget)
            {
                CardInstance t_nextTarget = ExecutionRule.PickRandomTarget(_attacker, this.ctx.enemyField.State);
                if (t_nextTarget != null)
                {
                    await UniTask.Delay((int)(GameTiming.Battle.OpponentExtraAttackDelay * 1000));
                    ExecuteAttackAsync(_attacker, t_nextTarget).Forget();
                    return;
                }

                // 칠 대상이 없으면 턴을 닫는다(입력을 열면 양측이 서로를 기다리며 잠긴다).
                this.forcedAttacker      = null;
                TurnState.ForcedAttacker = null;
                CardView.RestoreAllFades();
                this.turnDone = true;
                return;
            }

            TurnState.InputAllowed = true;
            this.attackRunning = false;
            return;
        }

        this.forcedAttacker     = null;
        TurnState.ForcedAttacker = null;
        CardView.RestoreAllFades();
        this.turnDone = true;
    }

    /// <summary>AI 인수 후 상대 필드 보충. 원래 이 자리는 상대 클라가 채워 CardSpawn RPC로 알려주던 곳이다 —
    /// 상대가 사라지면 그 보충 권한이 이쪽으로 넘어온다. 배치한 카드를 그대로 돌려주는 이유는
    /// 아래 공통 경로가 PlayFillAnim으로 등장 연출을 태우기 때문이다(원격이 채웠을 때와 화면이 같아야 한다).
    /// Refresh는 공통 경로가 바로 뒤에서 하므로 여기서 다시 하지 않는다.</summary>
    List<CardInstance> FillEnemyAfterTakeover() => this.ctx.enemyField.FillEmptySlots();
}
