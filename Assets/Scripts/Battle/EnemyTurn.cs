using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

public class EnemyTurn : TurnBase
{
    public EnemyTurn(TurnContext _ctx) : base(_ctx) { }

    static bool InSlotRange(int _slot) => _slot >= 0 && _slot < BattleField.SLOT_COUNT;

    public override void OnEnter()
    {
        if (this.ctx.turnLabel != null) this.ctx.turnLabel.text = "AI 턴";
    }

    public override async UniTask Execute()
    {
        await UniTask.Delay((int)(GameTiming.Battle.EnemyTurnStartDelay * 1000));

        CardInstance t_forcedAttacker = null;

        while (true)
        {
            List<CardInstance> t_attackers = this.ctx.enemyField.GetActiveCards();
            List<CardInstance> t_targets   = this.ctx.playerField.GetValidTargets();
            if (t_attackers.Count == 0 || t_targets.Count == 0) return;

            CardInstance t_atk;
            CardInstance t_def;
            if (TutorialConfig.IsActive)
            {
                // 튜토리얼: Random 대체. 스텝의 슬롯으로 직접 선택. 스텝 소진 = 턴 종료.
                if (!TutorialConfig.TryNextEnemyStep(out var t_step)) return;
                // 디자이너 입력 슬롯 무검증 전달 방지 — GetSlot은 경계검사가 없어 범위 밖이면 크래시.
                if (!InSlotRange(t_step.attackerSlot) || !InSlotRange(t_step.targetSlot))
                {
                    Debug.LogWarning($"[Tutorial] 적 스텝 슬롯 범위 초과 (atk={t_step.attackerSlot}, def={t_step.targetSlot}) → 턴 종료");
                    return;
                }
                t_atk = this.ctx.enemyField.GetSlot(t_step.attackerSlot);
                t_def = this.ctx.playerField.GetSlot(t_step.targetSlot);
                if (t_atk == null || t_def == null || !t_def.IsAlive) return;
            }
            else
            {
                t_atk = t_forcedAttacker ?? t_attackers[UnityEngine.Random.Range(0, t_attackers.Count)];
                t_def = t_targets[UnityEngine.Random.Range(0, t_targets.Count)];
            }

            if (!t_atk.IsAlive) return;

            CardView t_attackerView = this.ctx.enemyFieldView.GetSlotView(t_atk.slotIndex);
            CardView t_defenderView = this.ctx.playerFieldView.GetSlotView(t_def.slotIndex);

            var (t_preSelectedSplash, t_splashView) = AttackFlow.PreSelectSplash(
                t_atk, t_def, this.ctx.playerField, this.ctx.playerFieldView);

            AttackResult t_result = default;
            Action t_onEffect = () => t_result = AttackProcessor.Execute(
                t_atk, t_def, this.ctx.enemyField, this.ctx.playerField, t_preSelectedSplash);

            var (t_preKw, t_atKw) = AttackFlow.Keywords(t_atk);

            await AttackFlow.RunBeforeAttack(t_atk, t_def, this.ctx.enemyField, this.ctx.playerField);   // 무리 선피해(Execute 전 원자)

            await AttackSequence.Play(t_attackerView, t_defenderView, t_splashView,
                t_atk.data.attackEffect, t_onEffect, t_preKw, t_atKw,
                () => AttackFlow.RunAfterAttack(t_atk, t_def, this.ctx.enemyField, this.ctx.playerField, t_result));

            await this.ctx.FillAndAnimate();

            await AttackFlow.PlayResultFlourish(t_attackerView, t_atk, t_def, t_result);

            if (t_result.canAttackAgain && t_atk.IsAlive)
            {
                CardPassive.Notify(t_atk, CardKeyword.Execution);
                t_forcedAttacker = t_atk;
                await UniTask.Delay((int)(GameTiming.Battle.EnemyExtraAttackDelay * 1000));
            }
            else
            {
                break;
            }
        }
    }
}
