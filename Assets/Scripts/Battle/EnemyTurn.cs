using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TeamfightTCG.BattleCore;
using UnityEngine;

public class EnemyTurn : TurnBase
{
    public EnemyTurn(TurnContext _ctx) : base(_ctx) { }

    /// <summary>자유공격 스텝: 공격자·타깃 슬롯 둘 다 -1 → AI가 대상 선택.</summary>
    static bool IsFreeStep(TutorialScenarioData.ScriptedAttack _step) => TutorialStepGate.IsFreeStep(_step);

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
            // 배너 자리도 스텝이 정한다. t_step은 아래 블록 스코프라 여기로 꺼내 둔다(기본 = Top).
            var t_bannerAnchor = TutorialScenarioData.BannerAnchor.Top;
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
                t_tutorialMsg  = t_step.guideMessage;
                t_bannerAnchor = t_step.bannerAnchor;
                if (IsFreeStep(t_step))
                {
                    // 자유공격: 슬롯 무지정 → AI가 결정론으로 공격자·타깃 선택.
                    // 공격자 규칙은 EnemyAi, 처형 재공격 대상 규칙은 ExecutionRule(PickTargetFor 참조).
                    t_atk = t_forcedAttacker != null ? t_forcedAttacker : EnemyAi.PickAttacker(t_attackers);
                    t_def = PickTargetFor(t_atk, _executionChain: t_forcedAttacker != null);
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
                    TutorialOverlayUI.Instance.ShowMessage(t_step.guideMessage, true, t_step.bannerAnchor);   // 탭 게이트 = BG(dim) 항상 켬
                    await TutorialOverlayUI.Instance.WaitForTapAsync(GetCt());
                }

                TutorialConfig.ConsumeEnemyStep();   // 스텝 확정 소비(공격 진행)
            }
            else
            {
                // 일반 AI 공격. 공격자 선택(가중치 룰렛)의 진실원은 EnemyAi, 타깃은 PickTargetFor가 정한다 —
                // 튜토리얼 자유공격 스텝도 같은 함수를 부른다. 룰렛이 쓰는 랜덤은 MatchRandom뿐이고,
                // 시드는 GameInitializer가 전투 시작 전에 건다.
                t_atk = t_forcedAttacker != null
                    ? t_forcedAttacker
                    : EnemyAi.PickAttacker(t_attackers);
                t_def = PickTargetFor(t_atk, _executionChain: t_forcedAttacker != null);
            }

            // 타깃은 공격자 확정 후 다시 뽑는다(GetValidTargets(t_atk) = 지정 타깃·도발이 이 공격자 기준).
            // 위 t_targets는 "칠 대상이 아예 없는가"를 보는 루프 탈출 게이트 전용이라 여기 쓰지 않는다.
            if (t_atk == null || t_def == null) return;
            if (!t_atk.IsAlive) return;

            CardView t_attackerView = this.ctx.enemyFieldView.GetSlotView(t_atk.slotIndex);
            CardView t_defenderView = this.ctx.playerFieldView.GetSlotView(t_def.slotIndex);

            // 튜토리얼: 적 공격 순차 안내(문구+하이라이트, dim off) 후 읽기 딜레이. 연출 전용.
            // 메시지 없으면 오버레이 자체를 띄우지 않는다 — 빈 가이드가 순간 깜빡였다 사라지는 문제 방지.
            if (TutorialConfig.IsActive && TutorialOverlayUI.Instance != null
                && !string.IsNullOrWhiteSpace(t_tutorialMsg))
            {
                TutorialOverlayUI.Instance.ShowAttack(t_tutorialMsg, t_attackerView, t_defenderView, false,
                                                      t_bannerAnchor);
                await UniTask.Delay((int)(GameTiming.Battle.EnemyTurnStartDelay * 1000));

                // 안내 읽기 딜레이 후, 실제 공격 연출 동안 힌트(배너·하이라이트) 전부 숨김. 다음 스텝에서 재표시.
                TutorialOverlayUI.Instance.Clear();
            }

            var (t_preSelectedSplash, t_splashView) = AttackFlow.PreSelectSplash(
                t_atk, t_def, this.ctx.playerField, this.ctx.playerFieldView);

            var (t_preKw, t_atKw) = AttackFlow.Keywords(t_atk);

            await AttackFlow.RunBeforeAttack(t_atk, t_def, this.ctx.enemyField, this.ctx.playerField,
                                             t_preSelectedSplash);   // 낙인 선피해(Execute 전 원자)

            AttackResult t_result;
            using (BattleEventStream.CaptureScope t_events = BattleEventStream.BeginCapture())
            {
                t_result = AttackProcessor.Execute(
                    t_atk, t_def, this.ctx.enemyField, this.ctx.playerField, t_preSelectedSplash);
                t_result.events = t_events.ToArray();
            }

            await AttackSequence.Play(t_attackerView, t_defenderView, t_splashView,
                t_result.events, t_preKw, t_atKw,
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

    /// <summary>이 공격자가 칠 대상. <b>처형 재공격이면 대상 선택의 단일 진실원은 <see cref="ExecutionRule"/>다</b> —
    /// 도발을 무시하고 살아 있는 적 전부에서 뽑는 그 규칙을 AI도 그대로 따른다(사람 쪽 PlayerTurn과 동형).
    /// 첫 공격만 <see cref="EnemyAi"/>의 최저 체력 우선 규칙을 쓴다.
    /// <paramref name="_executionChain"/>이 false거나 <see cref="BattleUxFlags.ExecutionRandomTarget"/>가
    /// 꺼져 있으면(=대상을 직접 고르던 구 경로) AI는 고를 주체가 없으므로 EnemyAi로 폴백한다.</summary>
    CardInstance PickTargetFor(CardInstance _attacker, bool _executionChain)
        => _executionChain && BattleUxFlags.ExecutionRandomTarget
            ? ExecutionRule.PickRandomTarget(_attacker, this.ctx.playerField)
            : EnemyAi.PickTarget(this.ctx.playerField.GetValidTargets(_attacker));

    public override void OnExit()
    {
        if (TutorialConfig.IsActive) TutorialOverlayUI.Instance?.Clear();
    }

    /// <summary>실행 불가한 "선행 안내 + 공격 스텝" 묶음을 통째로 폐기(적 큐).
    /// 판정·폐기 규칙은 <see cref="TutorialStepGate"/> 단독 — 플레이어 턴과 기준이 갈리지 않게.</summary>
    void DiscardUnplayableEnemySteps()
        => TutorialStepGate.DiscardUnplayable(TutorialStepGate.Side.Enemy,
                                              this.ctx.enemyField, this.ctx.playerField);

    /// <summary>적 공격 스텝이 지금 실행 가능한가(범위·생존·기준선 일치). 규칙은 <see cref="TutorialStepGate"/>.</summary>
    bool IsEnemyStepPlayable(TutorialScenarioData.ScriptedAttack _step)
        => TutorialStepGate.IsPlayable(TutorialStepGate.Side.Enemy, _step,
                                       this.ctx.enemyField, this.ctx.playerField);

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
                t_overlay.ShowMessage(t_msg.guideMessage, true, t_msg.bannerAnchor);   // 탭 게이트 = BG(dim) 항상 켬
                await t_overlay.WaitForTapAsync(GetCt());
            }
            TutorialConfig.ConsumeEnemyStep();
        }
        return TutorialConfig.TryPeekEnemyStep(out _);   // 남은 공격 스텝 존재?
    }

    CancellationToken GetCt() => this.ctx.playerFieldView.GetCancellationTokenOnDestroy();
}
