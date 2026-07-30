using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

public class EnemyTurn : TurnBase
{
    public EnemyTurn(TurnContext _ctx) : base(_ctx) { }

    static bool InSlotRange(int _slot) => _slot >= 0 && _slot < BattleField.SLOT_COUNT;

    /// <summary>자유공격 스텝: 공격자·타깃 슬롯 둘 다 -1 → AI가 대상 선택.</summary>
    static bool IsFreeStep(TutorialScenarioData.ScriptedAttack _step)
        => _step.attackerSlot < 0 && _step.targetSlot < 0;

    public override void OnEnter()
    {
        if (this.ctx.turnLabel != null) this.ctx.turnLabel.text = "상대 턴";
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
            string t_tutorialMsg = null;
            bool   t_scriptedSlots = false;   // 슬롯 지정 스텝대로 진행했는가(기준선 재동기 대상)

            // 실행 불가 스텝(공격자·타깃이 죽었거나 그 자리를 다른 카드가 채움)은 안내 묶음째 먼저 폐기.
            // 전부 폐기되면 아래 t_scripted가 false → 일반 AI 폴백(턴이 비지 않는다).
            if (TutorialConfig.IsActive) DiscardUnplayableEnemySteps();

            // 튜토리얼: 적 스크립트에 Attack 스텝이 있으면 스크립트대로, 없으면(=enemyScript 미저작/소진)
            // 일반 AI로 폴백해 상대도 공격한다. (선행 Message 스텝은 폴백 여부와 무관하게 먼저 소진.)
            bool t_scripted = TutorialConfig.IsActive && await DrainEnemyMessagesAsync();
            if (t_scripted)
            {
                TutorialConfig.TryPeekEnemyStep(out var t_step);   // Drain 성공 = Attack 스텝 존재 보장
                t_tutorialMsg = t_step.guideMessage;
                if (IsFreeStep(t_step))
                {
                    // 자유공격: 슬롯 무지정 → AI가 결정론(MatchRandom)으로 공격자·타깃 선택.
                    t_atk = t_forcedAttacker != null ? t_forcedAttacker : t_attackers[MatchRandom.Range(t_attackers.Count)];
                    t_def = t_targets[MatchRandom.Range(t_targets.Count)];
                }
                else
                {
                    // 위 폐기 루프를 통과한 스텝이지만 방어 유지 — 범위 밖 저작값이 GetSlot에 그대로 들어가면 크래시.
                    if (!IsEnemyStepPlayable(t_step))
                    {
                        Debug.LogWarning($"[Tutorial] 적 스텝 무효(atk={t_step.attackerSlot}, def={t_step.targetSlot}) → 스텝 폐기·턴 종료");
                        TutorialConfig.DiscardEnemyStep();
                        return;
                    }
                    t_atk = this.ctx.enemyField.GetSlot(t_step.attackerSlot);
                    t_def = this.ctx.playerField.GetSlot(t_step.targetSlot);
                    t_scriptedSlots = true;
                }

                // 공격 전 설명 탭 게이트. 메시지 없으면 게이트 자체를 건너뛴다 —
                // 빈 텍스트로 dim 가이드 화면이 떴다 사라지는 문제 방지(탭 대기도 무의미).
                if (t_step.waitForTap && !string.IsNullOrWhiteSpace(t_step.guideMessage)
                    && TutorialOverlayUI.Instance != null)
                {
                    TutorialOverlayUI.Instance.ShowMessage(t_step.guideMessage, true);   // 탭 게이트 = BG(dim) 항상 켬
                    await TutorialOverlayUI.Instance.WaitForTapAsync(GetCt());
                }

                TutorialConfig.ConsumeEnemyStep();   // 스텝 확정 소비(공격 진행)
            }
            else
            {
                // 일반 AI 랜덤 공격. 튜토리얼 폴백은 재현성 위해 고정 시드(MatchRandom) 사용.
                if (t_forcedAttacker != null)          t_atk = t_forcedAttacker;
                else if (TutorialConfig.IsActive)      t_atk = t_attackers[MatchRandom.Range(t_attackers.Count)];
                else                                   t_atk = t_attackers[UnityEngine.Random.Range(0, t_attackers.Count)];

                t_def = TutorialConfig.IsActive
                    ? t_targets[MatchRandom.Range(t_targets.Count)]
                    : t_targets[UnityEngine.Random.Range(0, t_targets.Count)];
            }

            if (!t_atk.IsAlive) return;

            CardView t_attackerView = this.ctx.enemyFieldView.GetSlotView(t_atk.slotIndex);
            CardView t_defenderView = this.ctx.playerFieldView.GetSlotView(t_def.slotIndex);

            // 튜토리얼: 적 공격 순차 안내(문구+하이라이트, dim off) 후 읽기 딜레이. 연출 전용.
            // 메시지 없으면 오버레이 자체를 띄우지 않는다 — 빈 가이드가 순간 깜빡였다 사라지는 문제 방지.
            if (TutorialConfig.IsActive && TutorialOverlayUI.Instance != null
                && !string.IsNullOrWhiteSpace(t_tutorialMsg))
            {
                TutorialOverlayUI.Instance.ShowAttack(t_tutorialMsg, t_attackerView, t_defenderView, false);
                await UniTask.Delay((int)(GameTiming.Battle.EnemyTurnStartDelay * 1000));

                // 안내 읽기 딜레이 후, 실제 공격 연출 동안 힌트(배너·하이라이트) 전부 숨김. 다음 스텝에서 재표시.
                TutorialOverlayUI.Instance.Clear();
            }

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

            // 교활 퇴장은 보충 **전**에 — 슬롯 뷰가 아직 물러나는 카드를 그리고 있는 동안만 가능하다.
            await AttackFlow.PlayCunningSwap(this.ctx.enemyFieldView, t_attackerView, t_result);

            await this.ctx.FillAndAnimate();

            // 튜토리얼: 슬롯 지정 스텝대로 끝난 공격의 결과 보드를 기준선으로 재동기(PlayerTurn과 대칭).
            if (TutorialConfig.IsActive && t_scriptedSlots)
                TutorialConfig.SyncBoardBaseline(this.ctx.playerField, this.ctx.enemyField);

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

    public override void OnExit()
    {
        if (TutorialConfig.IsActive) TutorialOverlayUI.Instance?.Clear();
    }

    /// <summary>
    /// 큐 앞의 "선행 안내 + 공격 스텝 1개"를 한 묶음으로 보고, 실행 불가한 묶음을 통째로 조용히 폐기한다.
    /// 공격 스텝이 더 없는 꼬리(안내만 남음)는 손대지 않는다. PlayerTurn.DiscardUnplayableSteps와 대칭.
    /// </summary>
    void DiscardUnplayableEnemySteps()
    {
        while (true)
        {
            int t_ahead = 0;
            while (TutorialConfig.TryPeekEnemyStep(t_ahead, out var t_lead)
                   && t_lead.kind != TutorialScenarioData.StepKind.Attack)
                t_ahead++;

            if (!TutorialConfig.TryPeekEnemyStep(t_ahead, out var t_attack)) return;   // 남은 공격 스텝 없음
            if (IsEnemyStepPlayable(t_attack)) return;                                 // 유효 묶음 도달

            Debug.LogWarning($"[Tutorial] 적 공격 스텝 무효(atk={t_attack.attackerSlot}, def={t_attack.targetSlot})" +
                             $" → 선행 안내 포함 {t_ahead + 1}개 스킵");
            for (int i = 0; i <= t_ahead; i++) TutorialConfig.DiscardEnemyStep();
        }
    }

    /// <summary>적 공격 스텝이 지금 실행 가능한가(범위·생존·기준선 일치). PlayerTurn과 대칭.
    /// 기준선 대조 = 죽은 카드 자리를 채운 다른 카드가 스크립트 공격자/타깃이 되는 것을 막는다.</summary>
    bool IsEnemyStepPlayable(TutorialScenarioData.ScriptedAttack _step)
    {
        // 자유공격: 생존 적·아군이 각각 1장 이상이면 실행 가능(슬롯 무관, 대상은 AI가 선택).
        if (IsFreeStep(_step))
            return this.ctx.enemyField.GetActiveCards().Count > 0
                && this.ctx.playerField.GetActiveCards().Count > 0;
        if (!InSlotRange(_step.attackerSlot) || !InSlotRange(_step.targetSlot)) return false;
        CardInstance t_atk = this.ctx.enemyField.GetSlot(_step.attackerSlot);
        CardInstance t_def = this.ctx.playerField.GetSlot(_step.targetSlot);
        if (t_atk == null || !t_atk.IsAlive || t_def == null || !t_def.IsAlive) return false;
        return TutorialConfig.MatchesEnemyBaseline(_step.attackerSlot, t_atk)
            && TutorialConfig.MatchesPlayerBaseline(_step.targetSlot, t_def);
    }

    /// <summary>
    /// 튜토리얼: 큐 앞의 적 Message 스텝을 탭 게이트로 소진. 반환 true = 남은 공격 스텝 존재.
    /// </summary>
    async UniTask<bool> DrainEnemyMessagesAsync()
    {
        var t_overlay = TutorialOverlayUI.Instance;
        while (TutorialConfig.TryPeekEnemyStep(out var t_msg)
               && t_msg.kind == TutorialScenarioData.StepKind.Message)
        {
            if (t_overlay != null && !string.IsNullOrWhiteSpace(t_msg.guideMessage))
            {
                t_overlay.ShowMessage(t_msg.guideMessage, true);   // 탭 게이트 = BG(dim) 항상 켬
                await t_overlay.WaitForTapAsync(GetCt());
            }
            TutorialConfig.ConsumeEnemyStep();
        }
        return TutorialConfig.TryPeekEnemyStep(out _);   // 남은 공격 스텝 존재?
    }

    CancellationToken GetCt() => this.ctx.playerFieldView.GetCancellationTokenOnDestroy();
}
