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

        while (true)
        {
            // 상대 공격 결정 RPC 대기
            MultiplayerTurnRunner t_runner = MultiplayerTurnRunner.Instance;
            var (t_attackerSlot, t_defenderSlot, t_cunningSwap) = t_runner != null
                ? await t_runner.WaitForOpponentAttack()
                : (0, 0, false);

            CardInstance t_atk = this.ctx.enemyField.GetSlot(t_attackerSlot);
            CardInstance t_def = this.ctx.playerField.GetSlot(t_defenderSlot);
            if (t_atk == null || !t_atk.IsAlive || t_def == null) return;

            CardView t_attackerView = this.ctx.enemyFieldView.GetSlotView(t_atk.slotIndex);
            CardView t_defenderView = this.ctx.playerFieldView.GetSlotView(t_def.slotIndex);

            var (t_preSelectedSplash, t_splashView) = AttackFlow.PreSelectSplash(
                t_atk, t_def, this.ctx.playerField, this.ctx.playerFieldView);

            AttackResult t_result = default;
            Action t_onEffect = () => t_result = AttackProcessor.Execute(
                t_atk, t_def, this.ctx.enemyField, this.ctx.playerField, t_preSelectedSplash, t_cunningSwap);

            var (t_preKw, t_atKw) = AttackFlow.Keywords(t_atk);

            await AttackFlow.RunBeforeAttack(t_atk, t_def, this.ctx.enemyField, this.ctx.playerField);   // 무리 선피해(Execute 전 원자)

            await AttackSequence.Play(t_attackerView, t_defenderView, t_splashView,
                t_atk.data.attackEffect, t_onEffect, t_preKw, t_atKw,
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
            if (NetworkGameController.Instance != null)
                await NetworkGameController.Instance.WaitForOpponentReady();

            // 상대 스폰 반영
            List<CardInstance> t_enemyPlaced = t_runner?.FlushEnemySpawns() ?? new List<CardInstance>();
            this.ctx.enemyFieldView.Refresh();
            this.ctx.enemyDeckUI?.Refresh();
            await this.ctx.enemyFieldView.PlayFillAnim(t_enemyPlaced);

            // 내 카드 전멸 → CheckGameOver에 위임 (Execution 데드락 방지)
            if (this.ctx.playerField.IsEmpty) break;

            if (t_result.canAttackAgain && t_atk.IsAlive)
            {
                CardPassive.Notify(t_atk, CardKeyword.Execution);
                await UniTask.Delay((int)(GameTiming.Battle.OpponentExtraAttackDelay * 1000));
                // 루프 → 다음 공격 RPC 대기
            }
            else
            {
                break;
            }
        }
    }

    public override void OnExit() { }
}
