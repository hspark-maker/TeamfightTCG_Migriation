using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

public class EnemyTurn : TurnBase
{
    public EnemyTurn(TurnContext _ctx) : base(_ctx) { }

    public override void OnEnter()
    {
        if (this.ctx.turnLabel != null) this.ctx.turnLabel.text = "AI 턴";
    }

    public override async UniTask Execute()
    {
        await UniTask.Delay(800);

        CardInstance t_forcedAttacker = null;

        while (true)
        {
            List<CardInstance> t_attackers = this.ctx.enemyField.GetActiveCards();
            List<CardInstance> t_targets   = this.ctx.playerField.GetValidTargets();
            if (t_attackers.Count == 0 || t_targets.Count == 0) return;

            CardInstance t_atk = t_forcedAttacker ?? t_attackers[UnityEngine.Random.Range(0, t_attackers.Count)];
            CardInstance t_def = t_targets[UnityEngine.Random.Range(0, t_targets.Count)];

            if (!t_atk.IsAlive) return;

            CardView t_attackerView = this.ctx.enemyFieldView.GetSlotView(t_atk.slotIndex);
            CardView t_defenderView = this.ctx.playerFieldView.GetSlotView(t_def.slotIndex);

            var (t_preSelectedSplash, t_splashView) = AttackFlow.PreSelectSplash(
                t_atk, t_def, this.ctx.playerField, this.ctx.playerFieldView);

            AttackResult t_result = default;
            Action t_onEffect = () => t_result = AttackProcessor.Execute(
                t_atk, t_def, this.ctx.enemyField, this.ctx.playerField, t_preSelectedSplash);

            var (t_preKw, t_atKw) = AttackFlow.Keywords(t_atk);

            await AttackSequence.Play(t_attackerView, t_defenderView, t_splashView,
                t_atk.data.attackEffect, t_onEffect, t_preKw, t_atKw);

            await AttackFlow.RunAfterAttackPassives(t_atk, t_def, this.ctx.enemyField, t_result);

            await this.ctx.FillAndAnimate();

            await AttackFlow.PlayResultFlourish(t_attackerView, t_atk, t_def, t_result);

            if (t_result.canAttackAgain && t_atk.IsAlive)
            {
                CardPassive.Notify(t_atk, CardKeyword.Execution);
                t_forcedAttacker = t_atk;
                await UniTask.Delay(400);
            }
            else
            {
                break;
            }
        }
    }
}
