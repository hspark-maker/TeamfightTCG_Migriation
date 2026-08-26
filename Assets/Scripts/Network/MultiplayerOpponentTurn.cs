using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
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

        while (true)
        {
            // 상대 공격 결정 RPC 대기
            MultiplayerTurnRunner t_runner = MultiplayerTurnRunner.Instance;
            bool t_received;
            int t_attackerSlot;
            int t_defenderSlot;
            bool? t_cunningSwap;

            if (DeckConfig.AiTakeover && t_takeoverAttacker != null)
            {
                // 처형 재공격 대상의 단일 진실원은 ExecutionRule이다 — 도발을 무시하고 살아 있는 적 전부에서
                // 뽑는 그 규칙을 AI 인수 후에도 그대로 쓴다(EnemyTurn.PickTargetFor와 동형).
                CardInstance t_target = BattleUxFlags.ExecutionRandomTarget
                    ? ExecutionRule.PickRandomTarget(t_takeoverAttacker, this.ctx.playerField)
                    : EnemyAi.PickTarget(this.ctx.playerField.GetValidTargets(t_takeoverAttacker));
                if (!t_takeoverAttacker.IsAlive || t_target == null) return;

                t_received = true;
                t_attackerSlot = t_takeoverAttacker.slotIndex;
                t_defenderSlot = t_target.slotIndex;
                t_cunningSwap = null;   // 와이어 값 없음 — EnemyTurn과 동형으로 로컬 판정(교활 스왑 유지)
                t_takeoverAttacker = null;
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
                Debug.LogError($"[Net] 공격 미러 불일치 — atkSlot={t_attackerSlot}, defSlot={t_defenderSlot}, " +
                               $"attacker={(t_atk == null ? "null" : t_atk.IsAlive ? "alive" : "dead")}, " +
                               $"defender={(t_def == null ? "null" : "present")}");
                TurnRunner.Instance?.AbortMatch(EMatchEndReason.Desync);
                return;
            }

            CardView t_attackerView = this.ctx.enemyFieldView.GetSlotView(t_atk.slotIndex);
            CardView t_defenderView = this.ctx.playerFieldView.GetSlotView(t_def.slotIndex);

            var (t_preSelectedSplash, t_splashView) = AttackFlow.PreSelectSplash(
                t_atk, t_def, this.ctx.playerField, this.ctx.playerFieldView);

            AttackResult t_result = default;
            Action t_onEffect = () => t_result = AttackProcessor.Execute(
                t_atk, t_def, this.ctx.enemyField, this.ctx.playerField, t_preSelectedSplash, t_cunningSwap);

            var (t_preKw, t_atKw) = AttackFlow.Keywords(t_atk);

            await AttackFlow.RunBeforeAttack(t_atk, t_def, this.ctx.enemyField, this.ctx.playerField,
                                             t_preSelectedSplash);   // 낙인 선피해(Execute 전 원자)

            await AttackSequence.Play(t_attackerView, t_defenderView, t_splashView,
                CardCatalog.AttackEffectOf(t_atk.data), t_onEffect, t_preKw, t_atKw,
                () => AttackFlow.RunAfterAttack(t_atk, t_def, this.ctx.enemyField, this.ctx.playerField, t_result));

            // 교활 퇴장은 보충 **전**에 — 슬롯 뷰가 아직 물러나는 카드를 그리고 있는 동안만 가능하다.
            await AttackFlow.PlayCunningSwap(this.ctx.enemyFieldView, t_attackerView, t_result);

            // 내 field만 로컬 채움 + 브로드캐스트
            List<CardInstance> t_playerPlaced = this.ctx.playerField.FillEmptySlots();
            t_runner?.BroadcastMySpawns(t_playerPlaced);

            this.ctx.playerFieldView.Refresh();
            this.ctx.playerDeckUI?.Refresh();
            await this.ctx.playerFieldView.PlayFillAnim(t_playerPlaced);

            await AttackFlow.PlayResultFlourish(t_attackerView, t_atk, t_def, t_result);

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

            // 내 카드 전멸 → CheckGameOver에 위임 (Execution 데드락 방지)
            if (this.ctx.playerField.IsEmpty) break;

            if (t_result.canAttackAgain && t_atk.IsAlive)
            {
                CardPassive.Notify(t_atk, CardKeyword.Execution);

                if (DeckConfig.AiTakeover)
                {
                    // EnemyTurn과 동일하게 처형 공격자는 유지하고 타깃만 AI 규칙으로 다시 고른다.
                    t_takeoverAttacker = t_atk;
                }

                // **결정론 정렬**: 공격한 쪽(MultiplayerPlayerTurn)은 여기서 처형 대상을 MatchRandom으로 뽑는다.
                // 실제 대상은 곧 도착할 공격 RPC로 받으므로 이 값은 버리지만, 뽑는 행위 자체를 빼면
                // 스트림 소비 횟수가 한 번 어긋나 그 순간부터 양측 랜덤이 영구히 갈린다.
                else if (BattleUxFlags.ExecutionRandomTarget)
                    _ = ExecutionRule.PickRandomTarget(t_atk, this.ctx.playerField);

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
