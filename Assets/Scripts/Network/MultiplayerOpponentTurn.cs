using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TeamfightTCG.BattleCore;
using UnityEngine;

/// <summary>
/// 상대 턴 (멀티). 공격 RPC 대기 → 로컬에서 EnemyTurn과 동일한 연출 실행.
/// 내 field만 로컬 채움, 상대 field는 spawn RPC로 수신.
/// </summary>
public class MultiplayerOpponentTurn : TurnBase
{
    public MultiplayerOpponentTurn(TurnContext _ctx) : base(_ctx) { }

    public override void OnEnter()
    {
        if (this.ctx.turnLabel != null) this.ctx.turnLabel.text = "상대 턴";
    }

    public override async UniTask Execute()
    {
        await UniTask.Delay((int)(GameTiming.Battle.OpponentTurnStartDelay * 1000));
        CardInstance t_takeoverAttacker = null;
        CardInstance t_takeoverTarget = null;
        CardInstance t_executionAttacker = null;

        // 수신 공격의 규칙 검증(도발)을 건너뛰어야 하는 경우를 구분하는 플래그.
        // 처형 재공격은 ExecutionRule이 도발을 **의도적으로 무시하고** 대상을 뽑으므로
        // 여기에 도발 필터를 걸면 정상 플레이가 Desync로 끊긴다.
        bool t_executionFollowUp = false;

        while (true)
        {
            bool t_locallyDecided = false;
            // 이번 바퀴에만 유효하다 — 꺼진 채로 다음 바퀴에 새는 일이 없게 읽자마자 되돌린다.
            bool t_ruleBackstopOff = t_executionFollowUp;
            t_executionFollowUp = false;
            CardInstance t_expectedExecutionAttacker = t_executionAttacker;
            t_executionAttacker = null;
            // 상대 공격 결정 RPC 대기
            MultiplayerTurnRunner t_runner = MultiplayerTurnRunner.Instance;
            bool t_received;
            int t_attackerSlot;
            int t_defenderSlot;
            bool? t_cunningSwap;

            if (DeckConfig.AiTakeover && t_takeoverAttacker != null)
            {
                // 자동 대상은 직전 공격 직후 ExecutionRule로 이미 확정했다. 여기서 다시 뽑으면
                // AI 인수 순간부터 RNG를 두 번 소비하므로 저장한 대상을 그대로 쓴다.
                CardInstance t_target = BattleUxFlags.ExecutionRandomTarget
                    ? t_takeoverTarget
                    : EnemyAi.PickTarget(this.ctx.playerField.GetValidTargets(t_takeoverAttacker));
                if (!t_takeoverAttacker.IsAlive || t_target == null) return;

                t_received = true;
                t_locallyDecided = true;   // 와이어에서 온 값이 아니다 — 규칙 검증은 이미 위 선택 규칙이 했다
                t_attackerSlot = t_takeoverAttacker.slotIndex;
                t_defenderSlot = t_target.slotIndex;
                t_cunningSwap = null;   // 와이어 값 없음 — EnemyTurn과 동형으로 로컬 판정(교활 스왑 유지)
                t_takeoverAttacker = null;
                t_takeoverTarget = null;
            }
            else
            {
                var t_attack = t_runner != null
                    ? await t_runner.WaitForOpponentAttack()
                    : (false, 0, 0, false);
                (t_received, t_attackerSlot, t_defenderSlot, t_cunningSwap) = t_attack;
            }

            if (!t_received)
            {
                if (DeckConfig.AiTakeover)
                {
                    await ExecuteAiTakeoverTurn();
                    return;
                }
                TurnRunner.Instance?.AbortMatch(EMatchEndReason.Timeout);
                return;
            }

            CardInstance t_atk = this.ctx.enemyField.GetSlot(t_attackerSlot);
            CardInstance t_def = this.ctx.playerField.GetSlot(t_defenderSlot);
            if (t_atk == null || !t_atk.IsAlive || t_def == null)
            {
                Debug.LogError($"[Net] Attack mirror mismatch — atkSlot={t_attackerSlot}, defSlot={t_defenderSlot}, " +
                               $"attacker={(t_atk == null ? "null" : t_atk.IsAlive ? "alive" : "dead")}, " +
                               $"defender={(t_def == null ? "null" : "present")}");
                TurnRunner.Instance?.AbortMatch(EMatchEndReason.Desync);
                return;
            }

            if (t_expectedExecutionAttacker != null && !ReferenceEquals(t_atk, t_expectedExecutionAttacker))
            {
                Debug.LogError($"[Net] Execution re-attacker mismatch — expectedSlot={t_expectedExecutionAttacker.slotIndex}, " +
                               $"receivedSlot={t_attackerSlot}");
                TurnRunner.Instance?.AbortMatch(EMatchEndReason.Desync);
                return;
            }

            // 규칙 백스톱(수신 경로). 송신측(MultiplayerPlayerTurn.HandleCardViewAttack)만 BattleRules를
            // 통과시키면 규칙 집행이 비대칭이 된다 — 조작 클라가 도발을 무시한 공격을 보내면
            // 받는 쪽이 그대로 재현해 버린다. 판정은 송신측과 **같은 함수**(BattleField.CanAttack)로 한다.
            if (!t_locallyDecided && !t_ruleBackstopOff
                && !this.ctx.playerField.CanAttack(t_atk, t_def))
            {
                Debug.LogError($"[Net] Received a rule-violating attack — atkSlot={t_attackerSlot}, defSlot={t_defenderSlot}, " +
                               $"validTargets={string.Join(",", this.ctx.playerField.GetValidTargets(t_atk).ConvertAll(c => c.slotIndex))}");
                TurnRunner.Instance?.AbortMatch(EMatchEndReason.Desync);
                return;
            }

            // 수신 공격이라 교활 스왑도 derived 도 와이어 값이 진실원이다(로컬 재계산 금지).
            AttackFlow.AttackOutcome t_outcome = await AttackFlow.RunOneAttack(new AttackFlow.AttackRequest(
                t_atk, t_def, this.ctx.enemyField, this.ctx.playerField,
                this.ctx.enemyFieldView, this.ctx.playerFieldView,
                t_cunningSwap, t_ruleBackstopOff));
            AttackResult t_result = t_outcome.Result;
            CardView t_attackerView = t_outcome.AttackerView;
            CardView t_defenderView = t_outcome.DefenderView;
            CardInstance t_preSelectedSplash = t_outcome.Splash;
            CardView t_splashView = t_outcome.SplashView;

            // 내 field만 로컬 채움 + 브로드캐스트
            List<CardInstance> t_playerPlaced = this.ctx.playerField.FillEmptySlots();
            t_runner?.BroadcastMySpawns(t_playerPlaced);

            this.ctx.playerFieldView.Refresh();
            this.ctx.playerDeckUI?.Refresh();
            await this.ctx.playerFieldView.PlayFillAnim(t_playerPlaced);

            // PlayDeathAnim이 alpha/scale을 1로 리셋하므로, 죽은 슬롯만 즉시 숨김
            // 전체 Refresh는 RPC로 미리 배치된 신규 카드까지 노출시키므로 사용 금지
            if (!t_atk.IsAlive && !t_result.attackerSwapped) t_attackerView.HideSlot();

            // 연출 완료 신호 → 상대(내가 공격한 PlayerTurn) 완료 + 스폰 RPC 전부 수신까지 대기
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
                        // 현재 공격 결과와 처형 연속 공격은 이미 확정됐다. AI 인수로 대기가
                        // 풀렸다면 이 안전 경계에서 보충하고 같은 공격자를 계속 사용한다.
                        t_takeoverPlaced = FillEnemyAfterTakeover();
                    }
                    else
                    {
                        TurnRunner.Instance?.AbortMatch(EMatchEndReason.Timeout);
                        return;
                    }
                }
            }

            // 상대 스폰 반영
            List<CardInstance> t_enemyPlaced = t_takeoverPlaced
                                               ?? t_runner?.FlushEnemySpawns()
                                               ?? new List<CardInstance>();
            this.ctx.enemyFieldView.Refresh();
            this.ctx.enemyDeckUI?.Refresh();
            await this.ctx.enemyFieldView.PlayFillAnim(t_enemyPlaced);

            // divergence 카나리아 스냅샷. MultiplayerPlayerTurn과 **정확히 같은 지점**이어야 한다
            // (배리어 통과 + 양쪽 보충 완료 직후). 인자 순서는 무관하다 — BattleStateHash가 OwnerIndex로 정렬한다.
            NetworkGameController.Instance?.StageStateHash(this.ctx.playerField.State, this.ctx.enemyField.State);

            // 내 카드 전멸 → CheckGameOver에 위임 (Execution 데드락 방지)
            if (this.ctx.playerField.IsEmpty) break;

            if (t_result.canAttackAgain)
            {
                // 다음 바퀴는 처형 재공격이다. 무작위 대상 모드일 때만 백스톱을 끈다 —
                // 그 모드에서만 송신측이 ExecutionRule(도발 무시)로 대상을 뽑기 때문이다.
                // 수동 재선택 모드면 송신측이 HandleCardViewAttack의 규칙 검사를 거치므로 백스톱을 유지한다.
                t_executionFollowUp = BattleUxFlags.ExecutionRandomTarget;
                t_executionAttacker = t_atk;
                if (BattleUxFlags.ExecutionRandomTarget)
                {
                    // 송신측과 같은 순간 같은 횟수로 뽑는다. 원격이면 값은 버리고,
                    // AI 인수 상태면 다음 바퀴가 다시 뽑지 않도록 함께 보관한다.
                    if (!ExecutionRule.TryPickNext(
                            in t_result, t_atk, this.ctx.playerField.State, out CardInstance t_nextTarget))
                        break;
                    if (DeckConfig.AiTakeover)
                    {
                        t_takeoverAttacker = t_atk;
                        t_takeoverTarget = t_nextTarget;
                    }
                }
                else if (!t_atk.IsAlive)
                {
                    break;
                }
                else if (DeckConfig.AiTakeover)
                {
                    // 수동 재선택 모드의 AI 인수는 기존 EnemyAi 대상 규칙을 다음 바퀴에 적용한다.
                    t_takeoverAttacker = t_atk;
                }

                await UniTask.Delay((int)(GameTiming.Battle.OpponentExtraAttackDelay * 1000));
                // 루프 → 다음 공격 RPC 대기
            }
            else
            {
                break;
            }
        }
    }

    async UniTask ExecuteAiTakeoverTurn()
    {
        var t_aiTurn = new EnemyTurn(this.ctx);
        t_aiTurn.OnEnter();
        await t_aiTurn.Execute();
        t_aiTurn.OnExit();
    }

    /// <summary>AI 인수 후 상대 필드 보충. 원래 이 자리는 상대 클라가 채워 CardSpawn RPC로 알려주던 곳이다 —
    /// 상대가 사라지면 그 보충 권한이 이쪽으로 넘어온다. 배치한 카드를 그대로 돌려주는 이유는
    /// 아래 공통 경로가 PlayFillAnim으로 등장 연출을 태우기 때문이다(원격이 채웠을 때와 화면이 같아야 한다).
    /// Refresh는 공통 경로가 바로 뒤에서 하므로 여기서 다시 하지 않는다.</summary>
    List<CardInstance> FillEnemyAfterTakeover() => this.ctx.enemyField.FillEmptySlots();

    public override void OnExit() { }
}
