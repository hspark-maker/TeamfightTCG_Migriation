using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

public class PlayerTurn : TurnBase
{
    CardInstance forcedAttacker;
    bool turnDone;

    public PlayerTurn(TurnContext _ctx) : base(_ctx) { }

    public override void OnEnter()
    {
        if (this.ctx.turnLabel != null) this.ctx.turnLabel.text = "내 턴";
        TurnState.InputAllowed = true;
        CardView.OnAttack    += HandleCardViewAttack;
    }

    public override async UniTask Execute()
    {
        this.turnDone = false;
        // 튜토리얼: 선행 설명 스텝(탭 게이트) 소진 후 공격 스텝을 안내. 소진 상태면 hang 방지로 턴 스킵.
        if (TutorialConfig.IsActive)
        {
            if (!await PrepareTutorialStepsAsync())
            {
                // 스크립트 소진: 자유 전환 시나리오면 턴 스킵 대신 자유 공격 턴을 연다(안내 없음).
                if (TutorialConfig.FreePlayAfterScript)
                {
                    EnterFreePlay();   // 아래 WaitUntil(turnDone)로 진행(일반 전투처럼 입력 대기)
                }
                else
                {
                    Debug.LogWarning("[Tutorial] 플레이어 스텝 소진 → 턴 스킵(hang 방지)");
                    this.turnDone = true;
                    return;
                }
            }
        }
        // 튜토리얼: 생각시간 타이머 미기동(자동공격이 스크립트 스텝을 깨므로). 스텝 소진까지 대기.
        if (!TutorialConfig.IsActive)
        {
            // 생각시간 감시 기동. ct는 턴 수명(씬 파괴)에 묶고, turnDone 세팅 시 자연 종료.
            var t_ct = this.ctx.playerFieldView.GetCancellationTokenOnDestroy();
            TurnThinkTimer.Watch(GameTiming.Battle.TurnThinkTime, () => this.turnDone, ForceTimeoutAttack, t_ct).Forget();
        }
        await UniTask.WaitUntil(() => this.turnDone);
    }

    public override void OnExit()
    {
        TurnState.InputAllowed   = false;
        TurnState.ForcedAttacker = null;
        TurnState.ForcedTarget   = null;
        CardView.ForcedDimAlpha  = 0.3f;   // 튜토리얼 암전 강도 원복
        CardView.RestoreAllFades();
        CardView.OnAttack      -= HandleCardViewAttack;
        this.ctx.ClearAllHighlights();
        if (TutorialConfig.IsActive) TutorialOverlayUI.Instance?.Clear();
        this.forcedAttacker = null;
    }

    /// <summary>
    /// 튜토리얼 턴 준비: 큐 앞의 Message 스텝을 탭 게이트로 소진한 뒤, 다음 공격 스텝을 안내한다.
    /// 반환 false = 남은 스텝 없음(턴 스킵/종료 필요). 표시·탭 대기 전용 — 스텝 소비는 TutorialConfig.
    /// </summary>
    async UniTask<bool> PrepareTutorialStepsAsync(CardInstance _forced = null)
    {
        var t_ct      = this.ctx.playerFieldView.GetCancellationTokenOnDestroy();
        var t_overlay = TutorialOverlayUI.Instance;

        TurnState.InputAllowed = false;   // 게이트/드레인 중 드래그 공격 차단(공격 스텝 준비 완료 시 재허용)

        // 선행 Message/Inspect 스텝 소진. Message = 탭 대기, Inspect = 적 카드 롱프레스 대기.
        while (TutorialConfig.TryPeekPlayerStep(out var t_step0)
               && (t_step0.kind == TutorialScenarioData.StepKind.Message
                   || t_step0.kind == TutorialScenarioData.StepKind.Inspect))
        {
            if (t_step0.kind == TutorialScenarioData.StepKind.Inspect)
            {
                // Inspect: 배너만 띄우고(마스크 off) 입력 허용 → OnMouseDown/롱프레스 동작.
                // 롱프레스 통지(WaitForInspectAsync) 대기 후, 다음 스텝 준비 위해 입력 재차단.
                // "상대 정보 확인" 집중: 확인 대상(적 targetSlot) 1장만 밝게, 나머지 전부 암전. 대기 후 원복.
                t_overlay?.ShowInspect(t_step0.guideMessage);
                CardView.ForcedDimAlpha = 0.1f;
                CardView.FadeAll(CardView.ForcedDimAlpha);
                if (InSlotRange(t_step0.targetSlot))
                {
                    CardView t_inspectView = this.ctx.enemyFieldView.GetSlotView(t_step0.targetSlot);
                    if (t_inspectView != null) CardView.FadeCards(1f, t_inspectView);
                }
                TurnState.InputAllowed = true;
                if (t_overlay != null) await t_overlay.WaitForInspectAsync(t_ct);
                TurnState.InputAllowed = false;
                CardView.RestoreAllFades();
            }
            else if (t_overlay != null)
            {
                t_overlay.ShowMessage(t_step0.guideMessage, true);   // 탭 게이트 = BG(dim) 항상 켬
                await t_overlay.WaitForTapAsync(t_ct);
            }
            TutorialConfig.ConsumePlayerStep();
        }

        // 다음 = 공격 스텝(없으면 스킵).
        if (!TutorialConfig.TryPeekPlayerStep(out var t_step)) return false;

        // 재무장(처형) 중이면 스텝 공격자 슬롯이 처형 공격자와 일치해야 진행 가능(불일치=이번 턴 불가).
        // 스텝은 소비하지 않는다 — 다음 정규 턴에서 forced 없이 재시도되어 자연 복구.
        if (_forced != null && !IsFreeStep(t_step) && t_step.attackerSlot != _forced.slotIndex)
        {
            Debug.LogWarning($"[Tutorial] 재무장 스텝 attackerSlot({t_step.attackerSlot}) != 처형 공격자 슬롯({_forced.slotIndex}) → 턴 종료");
            return false;
        }

        // 공격 스텝 슬롯 유효성(범위밖/빈슬롯/죽은카드 = 영구 소프트락 방지 → 스텝 폐기·턴 스킵).
        // EnemyTurn과 대칭. 도발 필터는 의도적으로 미적용(스크립트가 비-도발 타깃 저작 허용).
        if (!IsAttackStepPlayable(t_step))
        {
            Debug.LogWarning($"[Tutorial] 플레이어 공격 스텝 무효(atk={t_step.attackerSlot}, def={t_step.targetSlot}) → 스텝 폐기·턴 스킵");
            TutorialConfig.ConsumePlayerStep();
            return false;
        }

        // 공격 전 설명 탭 게이트(입력은 아직 차단 상태).
        if (t_step.waitForTap && t_overlay != null)
        {
            t_overlay.ShowMessage(t_step.guideMessage, true);   // 탭 게이트 = BG(dim) 항상 켬
            await t_overlay.WaitForTapAsync(t_ct);
        }

        ShowTutorialStep(t_step);          // 배너+하이라이트+포인터, 마스크 off(카드 드래그 허용)
        TurnState.InputAllowed = true;     // 공격 스텝 준비 완료 → 입력 재허용
        return true;
    }

    /// <summary>튜토리얼 공격 스텝이 지금 실행 가능한가(범위·생존). 도발 필터는 의도적 미적용.</summary>
    bool IsAttackStepPlayable(TutorialScenarioData.ScriptedAttack _step)
    {
        // 자유공격: 생존 아군·적이 각각 1장 이상이면 실행 가능(슬롯 무관).
        if (IsFreeStep(_step))
            return this.ctx.playerField.GetActiveCards().Count > 0
                && this.ctx.enemyField.GetActiveCards().Count > 0;
        if (!InSlotRange(_step.attackerSlot) || !InSlotRange(_step.targetSlot)) return false;
        CardInstance t_atk = this.ctx.playerField.GetSlot(_step.attackerSlot);
        CardInstance t_def = this.ctx.enemyField.GetSlot(_step.targetSlot);
        return t_atk != null && t_atk.IsAlive && t_def != null && t_def.IsAlive;
    }

    /// <summary>튜토리얼: 공격 스텝을 오버레이에 안내(문구+공격자/타깃 하이라이트+드래그 포인터).
    /// 추가로 스크립트 공격자를 <see cref="TurnState.ForcedAttacker"/>로 지정 → (1)다른 카드 입력 차단
    /// (OnMouseDown 게이트) (2)나머지 로컬 카드를 검게 암전(RestoreAllFades). "그 카드 말고 다 검게".</summary>
    void ShowTutorialStep(TutorialScenarioData.ScriptedAttack _step)
    {
        // 자유공격: 강제 지정 없음 → 전 카드 조작 허용, 암전/하이라이트/포인터 없이 안내 문구만.
        if (IsFreeStep(_step))
        {
            TurnState.ForcedAttacker = null;
            TurnState.ForcedTarget   = null;
            CardView.ForcedDimAlpha  = 0.3f;   // 기본값 원복
            CardView.RestoreAllFades();        // forced 없음 → 전부 밝게
            TutorialOverlayUI.Instance?.ShowAttack(_step.guideMessage, null, null, false);
            return;
        }

        CardView t_atkView = InSlotRange(_step.attackerSlot) ? this.ctx.playerFieldView.GetSlotView(_step.attackerSlot) : null;
        CardView t_defView = InSlotRange(_step.targetSlot)   ? this.ctx.enemyFieldView.GetSlotView(_step.targetSlot)   : null;

        // 선택 게이트+집중 암전: 스크립트 공격자만 조작/밝게, 나머지 로컬 카드는 검게 덮는다.
        CardInstance t_atkCard = InSlotRange(_step.attackerSlot) ? this.ctx.playerField.GetSlot(_step.attackerSlot) : null;
        CardInstance t_defCard = InSlotRange(_step.targetSlot)   ? this.ctx.enemyField.GetSlot(_step.targetSlot)    : null;
        TurnState.ForcedAttacker = t_atkCard;
        TurnState.ForcedTarget   = t_defCard;   // 지정 타깃 외 적 카드 암전(집중 유도)
        CardView.ForcedDimAlpha  = 0.1f;   // 튜토리얼: 거의 검게(일반 전투 0.3보다 진하게)
        CardView.RestoreAllFades();        // 공격자/타깃 기준 재적용 → 공격자·타깃만 full, 나머지 암전

        TutorialOverlayUI.Instance?.ShowAttack(_step.guideMessage, t_atkView, t_defView, true);
    }

    /// <summary>튜토리얼 자유 꼬리: 스크립트 소진 후 일반 전투처럼 자유 공격 턴을 연다.
    /// 강제 지정·암전·안내 배너 없이 아무 로컬 카드로 아무 적 카드를 공격 가능. 스텝 소비 없음.</summary>
    void EnterFreePlay()
    {
        this.forcedAttacker      = null;
        TurnState.ForcedAttacker = null;
        TurnState.ForcedTarget   = null;
        CardView.ForcedDimAlpha  = 0.3f;   // 튜토리얼 암전 강도 원복(일반 전투 기본)
        CardView.RestoreAllFades();        // forced 없음 → 전부 밝게
        TutorialOverlayUI.Instance?.Clear();   // 안내 배너/힌트 제거
        TurnState.InputAllowed   = true;
    }

    static bool InSlotRange(int _slot) => _slot >= 0 && _slot < BattleField.SLOT_COUNT;

    /// <summary>자유공격 스텝: 공격자·타깃 슬롯 둘 다 -1 → 강제 지정 없이 아무 카드로 아무 적을 공격 가능.</summary>
    static bool IsFreeStep(TutorialScenarioData.ScriptedAttack _step)
        => _step.attackerSlot < 0 && _step.targetSlot < 0;

    void HandleCardViewAttack(CardView _attacker, CardView _target)
    {
        CardInstance t_attCard = _attacker.BoundCard;
        CardInstance t_defCard = _target.BoundCard;

        if (t_attCard == null || t_attCard.ownerIndex != TurnState.LocalOwnerIndex) return;
        if (t_defCard == null || t_defCard.ownerIndex == TurnState.LocalOwnerIndex) return;
        if (this.forcedAttacker != null && t_attCard != this.forcedAttacker) return;

        // 튜토리얼: 스크립트 스텝(공격자 슬롯·타깃 슬롯)과 일치하는 공격만 허용. 불일치 = 입력 무시.
        if (TutorialConfig.IsActive)
        {
            if (!TutorialConfig.TryPeekPlayerStep(out var t_step))
            {
                // 스텝 소진: 자유 전환이면 소비 없이 통과(아무 카드로 아무 적 공격), 아니면 무시.
                if (!TutorialConfig.FreePlayAfterScript) return;
            }
            else
            {
                if (t_step.kind != TutorialScenarioData.StepKind.Attack) return; // 설명 스텝 중 공격 차단(탭으로만 진행)
                // 자유공격이 아니면 스크립트 슬롯과 일치하는 공격만 허용(불일치=입력 무시).
                if (!IsFreeStep(t_step))
                {
                    if (t_attCard.slotIndex != t_step.attackerSlot) return;
                    if (t_defCard.slotIndex != t_step.targetSlot) return;
                }
                TutorialConfig.ConsumePlayerStep();
            }
        }

        ExecuteAttack(t_attCard, t_defCard);
    }

    /// <summary>
    /// 생각시간 초과 시 자동으로 합법 공격 1개를 수동 공격과 동일 경로로 실행.
    /// RNG 절대 미사용 — attacker/target 모두 slot 오름차순 첫 생존/유효로 결정론 선택.
    /// turnDone은 여기서 세우지 않는다(ExecuteAttackAsync가 정상 resolve하며 세팅 = 단일 경로).
    /// </summary>
    void ForceTimeoutAttack()
    {
        if (!TurnState.InputAllowed) return;   // 이미 액션 시작됨 → 무시(원자성)

        // 선택을 먼저 — 유효 공격이 확정될 때만 입력을 차단한다.
        // (먼저 InputAllowed=false로 끄고 유효공격이 없어 return하면 turnDone도 안 서고
        //  입력도 죽어 hang. 순서를 뒤집어 그 위험 제거.)
        // Execution 재무장 창이면 ForcedAttacker 준수 필수(HandleCardViewAttack이 불일치 거절).
        CardInstance t_attacker = TurnState.ForcedAttacker;
        if (t_attacker == null)
        {
            var t_attackers = this.ctx.playerField.GetActiveCards();   // slot 오름차순
            if (t_attackers.Count > 0) t_attacker = t_attackers[0];
        }

        var t_targets = this.ctx.enemyField.GetValidTargets();          // 도발 우선 + slot 오름차순
        CardInstance t_target = t_targets.Count > 0 ? t_targets[0] : null;

        // 방어적: 유효 공격자/타깃 없으면 입력 유지한 채 반환(hang 방지, 다음 tick 재시도).
        if (t_attacker == null || !t_attacker.IsAlive || t_target == null) return;

        TurnState.InputAllowed = false;        // 유효 공격 확정 후에만 입력 차단
        CardView.RestoreAllFades();            // 드래그 잔상 정리
        ExecuteAttack(t_attacker, t_target);   // 수동 공격과 100% 동일 경로
    }

    void ExecuteAttack(CardInstance _attacker, CardInstance _defender)
    {
        ExecuteAttackAsync(_attacker, _defender).Forget();
    }

    async UniTask ExecuteAttackAsync(CardInstance _attacker, CardInstance _defender)
    {
        TurnState.InputAllowed = false;

        // 튜토리얼: 공격 연출 동안 안내 힌트(배너·탭힌트·하이라이트·포인터·dim) 전부 숨김. 다음 스텝에서 재표시.
        if (TutorialConfig.IsActive) TutorialOverlayUI.Instance?.Clear();

        CardView t_attackerView = this.ctx.playerFieldView.GetSlotView(_attacker.slotIndex);
        CardView t_defenderView = this.ctx.enemyFieldView.GetSlotView(_defender.slotIndex);

        var (t_preSelectedSplash, t_splashView) = AttackFlow.PreSelectSplash(
            _attacker, _defender, this.ctx.enemyField, this.ctx.enemyFieldView);

        AttackResult t_result = default;
        Action t_onEffect = () => t_result = AttackProcessor.Execute(
            _attacker, _defender, this.ctx.playerField, this.ctx.enemyField, t_preSelectedSplash);

        var (t_preKw, t_atKw) = AttackFlow.Keywords(_attacker);

        await AttackFlow.RunBeforeAttack(_attacker, _defender, this.ctx.playerField, this.ctx.enemyField);   // 무리 선피해(Execute 전 원자)

        await AttackSequence.Play(t_attackerView, t_defenderView, t_splashView,
            _attacker.data.attackEffect, t_onEffect, t_preKw, t_atKw,
            () => AttackFlow.RunAfterAttack(_attacker, _defender, this.ctx.playerField, this.ctx.enemyField, t_result));

        await this.ctx.FillAndAnimate();

        await AttackFlow.PlayResultFlourish(t_attackerView, _attacker, _defender, t_result);

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
            CardPassive.Notify(_attacker, CardKeyword.Execution);
            this.forcedAttacker      = _attacker;
            TurnState.ForcedAttacker  = _attacker;

            CardView.FadeTeam(0.3f, TurnState.LocalOwnerIndex);
            CardView.FadeCards(1f, t_attackerView);

            // 튜토리얼: 같은 턴 다음 스텝(처형 연속 공격) 준비 — 선행 Message 소진 후 공격 안내.
            // 처형 공격자 슬롯 일치 검증(_attacker 전달). 남은/유효 스텝 없으면 hang 방지로 턴 종료.
            if (TutorialConfig.IsActive)
            {
                if (!await PrepareTutorialStepsAsync(_attacker))
                {
                    // 자유 전환: 처형 재공격은 규칙상 그 카드로만 계속 → forcedAttacker(_attacker) 유지,
                    // 암전/하이라이트도 위에서 잡은 그대로 두고 입력만 재개(안내 없음).
                    if (TutorialConfig.FreePlayAfterScript)
                    {
                        TurnState.InputAllowed = true;
                        return;
                    }
                    this.forcedAttacker      = null;
                    TurnState.ForcedAttacker = null;
                    CardView.RestoreAllFades();
                    this.turnDone            = true;
                    return;
                }
            }

            TurnState.InputAllowed    = true;
            return;
        }

        this.forcedAttacker     = null;
        TurnState.ForcedAttacker = null;
        CardView.RestoreAllFades();
        this.turnDone           = true;
    }
}
