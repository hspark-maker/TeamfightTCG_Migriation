using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

public class PlayerTurn : TurnBase
{
    CardInstance forcedAttacker;
    bool turnDone;
    bool scriptedStepAttack;   // 진행 중인 공격이 슬롯 지정 스텝대로인가(기준선 재동기 대상). 자유공격은 false

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
                // 무효 스텝 폐기로 스크립트가 끊긴 경우(ScriptDerailed)도 마찬가지 — 안내 없는 턴 잠김 방지.
                if (TutorialConfig.FreePlayAfterScript || TutorialConfig.ScriptDerailed)
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
        TurnState.AllowedGesture = InputGesture.Any;   // 턴 종료 — 조작 제한 해제
        CardView.ForcedDimAlpha  = 0.3f;   // 튜토리얼 암전 강도 원복
        CardView.RestoreAllFades();
        CardView.OnAttack      -= HandleCardViewAttack;
        EndGuidedFreeSelect();   // 턴이 끝나면 구독을 반드시 끊는다(턴 객체는 매 턴 새로 만들어진다)
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

        // 실행 불가 스텝은 안내를 띄우기 '전에' 통째로 버린다(문구·오버레이도 안 뜨게).
        // 여기부터 공격 실행 전까지 보드는 변하지 않는다(입력 차단 상태) → 이 한 번의 검사로 충분.
        DiscardUnplayableSteps();

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
                t_overlay?.ShowInspect(t_step0.guideMessage, t_step0.bannerAnchor);
                CardView.ForcedDimAlpha = 0.1f;
                CardView.FadeAll(CardView.ForcedDimAlpha);
                if (InSlotRange(t_step0.targetSlot))
                {
                    CardView t_inspectView = this.ctx.enemyFieldView.GetSlotView(t_step0.targetSlot);
                    if (t_inspectView != null) CardView.FadeCards(1f, t_inspectView);
                }
                TurnState.AllowedGesture = InputGesture.LongPressOnly;   // 정보 확인 레슨 — 드래그·탭 차단
                TurnState.InputAllowed = true;
                if (t_overlay != null) await t_overlay.WaitForInspectAsync(t_ct);
                TurnState.InputAllowed = false;
                TurnState.AllowedGesture = InputGesture.Any;
                CardView.RestoreAllFades();
            }
            else if (t_overlay != null && !string.IsNullOrWhiteSpace(t_step0.guideMessage))
            {
                CardView t_focus = ResolveFocusCard(t_step0);

                // 아군 카드를 포커스한 설명은 **그 카드를 탭하는 것 자체가 진행**이다.
                // 화면 아무 데나 탭해서 넘기는 게이트를 겹쳐 두면 "탭하여 계속"이 떠서
                // 정작 눌러야 할 카드가 가려진다(부자연스러운 2단 조작).
                if (t_focus != null && t_step0.cardFocusSide == TutorialScenarioData.CardFocusSide.Player)
                {
                    t_overlay.ShowCardFocus(t_step0.guideMessage, t_step0.bannerAnchor, _waitTap: false, t_focus);
                    ApplyHandOverride(t_step0);
                    await WaitForFocusCardArmedAsync(t_focus, t_ct);
                }
                else if (t_focus != null)
                {
                    // 적 카드 포커스: 탭해도 무장되지 않으므로(적 카드는 공격 대상) 화면 탭으로 진행.
                    t_overlay.ShowCardFocus(t_step0.guideMessage, t_step0.bannerAnchor, _waitTap: true, t_focus);
                    await t_overlay.WaitForTapAsync(t_ct);
                }
                else
                {
                    t_overlay.ShowMessage(t_step0.guideMessage, true, t_step0.bannerAnchor);   // 탭 게이트 = BG(dim) 항상 켬
                    await t_overlay.WaitForTapAsync(t_ct);
                }
                t_overlay.ClearFieldFocus();
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

        // 공격 전 설명 탭 게이트(입력은 아직 차단 상태). 메시지 없으면 게이트 스킵 —
        // 빈 텍스트 dim 가이드 화면이 떴다 사라지는 문제 방지(적 턴과 대칭).
        if (t_step.waitForTap && !string.IsNullOrWhiteSpace(t_step.guideMessage) && t_overlay != null)
        {
            t_overlay.ShowMessage(t_step.guideMessage, true, t_step.bannerAnchor);   // 탭 게이트 = BG(dim) 항상 켬
            await t_overlay.WaitForTapAsync(t_ct);
        }

        ShowTutorialStep(t_step);          // 배너+하이라이트+포인터, 마스크 off(카드 드래그 허용)
        TurnState.InputAllowed = true;     // 공격 스텝 준비 완료 → 입력 재허용
        return true;
    }

    /// <summary>튜토리얼 처형 재공격의 대상. 큐 맨 앞이 <b>이 공격자를 지정한 공격 스텝</b>이고
    /// 그 타깃 슬롯에 카드가 살아 있을 때만 그 카드를 돌려주고 스텝을 소비한다 — 자동 발사가
    /// 스크립트 기준선과 어긋나지 않게. 조건이 하나라도 안 맞으면 null(무작위 대상으로 폴백).
    ///
    /// 안내 배너는 띄우지 않는다. 처형 재공격은 플레이어가 조작하는 스텝이 아니다.</summary>
    CardInstance TutorialScriptedExecutionTarget(CardInstance _attacker)
    {
        if (!TutorialConfig.IsActive || _attacker == null) return null;
        if (!TutorialConfig.TryPeekPlayerStep(out var t_step)) return null;
        if (t_step.kind != TutorialScenarioData.StepKind.Attack) return null;
        if (IsFreeStep(t_step)) return null;
        if (t_step.attackerSlot != _attacker.slotIndex) return null;
        if (!InSlotRange(t_step.targetSlot)) return null;

        CardInstance t_target = this.ctx.enemyField.GetSlot(t_step.targetSlot);
        if (t_target == null) return null;

        TutorialConfig.ConsumePlayerStep();
        return t_target;
    }

    /// <summary>실행 불가한 "선행 안내 + 공격 스텝" 묶음을 통째로 폐기(플레이어 큐).
    /// 판정·폐기 규칙은 <see cref="TutorialStepGate"/> 단독 — 적 턴과 기준이 갈리지 않게.</summary>
    void DiscardUnplayableSteps()
        => TutorialStepGate.DiscardUnplayable(TutorialStepGate.Side.Player,
                                              this.ctx.playerField, this.ctx.enemyField);

    /// <summary>튜토리얼: 공격 스텝을 오버레이에 안내(문구+공격자/타깃 하이라이트+드래그 포인터).
    /// 추가로 스크립트 공격자를 <see cref="TurnState.ForcedAttacker"/>로 지정 → (1)다른 카드 입력 차단
    /// (OnMouseDown 게이트) (2)나머지 로컬 카드를 검게 암전(RestoreAllFades). "그 카드 말고 다 검게".</summary>
    void ShowTutorialStep(TutorialScenarioData.ScriptedAttack _step)
    {
        EndGuidedFreeSelect();   // 이전 스텝의 포커스/구독 정리(아래 자유 분기에서 필요하면 다시 건다)

        // 자유공격: 강제 지정 없음 → 전 카드 조작 허용, 암전/하이라이트/포인터 없이 안내 문구만.
        if (IsFreeStep(_step))
        {
            TurnState.ForcedAttacker = null;
            TurnState.ForcedTarget   = null;
            TurnState.AllowedGesture = InputGesture.Any;   // 자유공격 = 조작 제한 없음
            CardView.ForcedDimAlpha  = 0.3f;   // 기본값 원복
            CardView.RestoreAllFades();        // forced 없음 → 전부 밝게

            if (_step.guidedFreeSelect) BeginGuidedFreeSelect(_step);
            else TutorialOverlayUI.Instance?.ShowAttack(_step.guideMessage, null, null, false, _step.bannerAnchor);
            return;
        }

        CardView t_atkView = InSlotRange(_step.attackerSlot) ? this.ctx.playerFieldView.GetSlotView(_step.attackerSlot) : null;
        CardView t_defView = InSlotRange(_step.targetSlot)   ? this.ctx.enemyFieldView.GetSlotView(_step.targetSlot)   : null;

        // 선택 게이트+집중 암전: 스크립트 공격자만 조작/밝게, 나머지 로컬 카드는 검게 덮는다.
        CardInstance t_atkCard = InSlotRange(_step.attackerSlot) ? this.ctx.playerField.GetSlot(_step.attackerSlot) : null;
        CardInstance t_defCard = InSlotRange(_step.targetSlot)   ? this.ctx.enemyField.GetSlot(_step.targetSlot)    : null;
        TurnState.ForcedAttacker = t_atkCard;
        TurnState.ForcedTarget   = t_defCard;   // 지정 타깃 외 적 카드 암전(집중 유도)
        TurnState.AllowedGesture = _step.allowedGesture;   // 이 스텝이 가르치는 조작만 허용(나머지 무반응)
        CardView.ForcedDimAlpha  = 0.1f;   // 튜토리얼: 거의 검게(일반 전투 0.3보다 진하게)
        CardView.RestoreAllFades();        // 공격자/타깃 기준 재적용 → 공격자·타깃만 full, 나머지 암전

        TutorialOverlayUI.Instance?.ShowAttack(_step.guideMessage, t_atkView, t_defView, true, _step.bannerAnchor);

        // 카드 낱장 포커스가 지정됐으면 배경까지 덮는다(ShowAttack은 카드 암전만 한다).
        // 탭 대기는 없다 — 이 스텝은 공격 입력으로 진행한다.
        CardView t_focus = ResolveFocusCard(_step);
        if (t_focus != null)
            TutorialOverlayUI.Instance?.ShowCardFocus(_step.guideMessage, _step.bannerAnchor, _waitTap: false, t_focus);

        ApplyHandOverride(_step);
    }

    // ── 필드 포커스 자유 선택 ────────────────────────────────────────────────
    // 1단계: 아군 필드만 남기고 딤 → 아무 아군이나 탭.
    // 2단계: 그 순간 적 필드만 남기고 딤 + 문구 교체 → 아무 적이나 탭해서 공격.
    // 무장을 풀면 1단계로 되돌아간다. **강제는 없다** — 슬롯을 지정하지 않으므로 어떤 조합이든 통과한다.

    TutorialScenarioData.ScriptedAttack guidedStep;
    bool guidedActive;

    /// <summary>포커스한 아군 카드가 탭으로 무장될 때까지 대기 = 이 설명 스텝의 진행 신호.
    ///
    /// 대기 동안 그 카드만 조작 가능하게 연다(ForcedAttacker) — 다른 아군을 눌러 엉뚱한 카드가
    /// 무장되면 안내와 화면이 어긋난다. 적 카드는 눌려도 <see cref="HandleCardViewAttack"/>의
    /// "설명 스텝 중 공격 차단"에 걸리므로 여기서 따로 막지 않는다.
    ///
    /// 무장은 남긴 채 끝난다 — 곧바로 이어지는 공격 스텝이 그 상태에서 자연스럽게 이어진다.</summary>
    async UniTask WaitForFocusCardArmedAsync(CardView _card, CancellationToken _ct)
    {
        CardInstance t_prevForced = TurnState.ForcedAttacker;
        TurnState.ForcedAttacker = _card.BoundCard;
        TurnState.InputAllowed   = true;

        try
        {
            if (CardView.SelectedAttacker == _card) return;   // 이미 무장 — 이벤트가 안 온다

            bool t_armed = false;
            void OnArmed(CardView _view) { if (_view == _card) t_armed = true; }

            CardView.OnAttackerArmed += OnArmed;
            try { await UniTask.WaitUntil(() => t_armed, cancellationToken: _ct); }
            finally { CardView.OnAttackerArmed -= OnArmed; }
        }
        finally
        {
            // 다음 스텝 준비 동안 입력 재차단(Inspect 경로와 같은 규약). ForcedAttacker는 원상복구.
            TurnState.InputAllowed   = false;
            TurnState.ForcedAttacker = t_prevForced;
        }
    }

    /// <summary>진영+슬롯 → 슬롯 뷰. None이거나 범위 밖이면 null.</summary>
    CardView ResolveSlotView(TutorialScenarioData.CardFocusSide _side, int _slot)
    {
        if (_side == TutorialScenarioData.CardFocusSide.None) return null;
        if (!InSlotRange(_slot)) return null;

        BattleFieldView t_view = _side == TutorialScenarioData.CardFocusSide.Enemy
            ? this.ctx.enemyFieldView : this.ctx.playerFieldView;
        return t_view != null ? t_view.GetSlotView(_slot) : null;
    }

    /// <summary>스텝이 지정한 카드 낱장 포커스 대상. None이거나 슬롯이 비었으면 null(포커스 없음).</summary>
    CardView ResolveFocusCard(TutorialScenarioData.ScriptedAttack _step)
        => ResolveSlotView(_step.cardFocusSide, _step.cardFocusSlot);

    /// <summary>가이드 핸드를 스텝 지정 슬롯으로 **덮어쓴다**. 지정이 없으면 자동 배치를 그대로 둔다.
    /// 표시 API(ShowAttack/ShowCardFocus/ShowFieldFocus)가 자체 배치를 끝낸 <b>뒤</b>에 불러야 한다 —
    /// 먼저 부르면 그 안의 자동 배치에 덮인다.</summary>
    void ApplyHandOverride(TutorialScenarioData.ScriptedAttack _step)
    {
        CardView t_hand = ResolveSlotView(_step.handSide, _step.handSlot);
        if (t_hand != null) TutorialOverlayUI.Instance?.ShowHandOn(t_hand);
    }

    void BeginGuidedFreeSelect(TutorialScenarioData.ScriptedAttack _step)
    {
        this.guidedStep = _step;
        if (!this.guidedActive)
        {
            this.guidedActive = true;
            CardView.OnAttackerArmed += HandleGuidedArm;
        }
        // **현재 무장 상태에서 시작**한다. 앞 스텝(아군 카드 포커스 설명)이 무장을 남긴 채 끝나므로
        // 무조건 1단계로 열면 화면과 실제가 어긋난다 — 그 상태에서 카드를 누르면 무장이 "풀려서"
        // 1단계 그대로 보이고, 한 번 더 눌러야 2단계로 간다(탭 3번 요구).
        ShowGuidedPhase(CardView.SelectedAttacker);
    }

    void EndGuidedFreeSelect()
    {
        if (!this.guidedActive) return;
        this.guidedActive = false;
        CardView.OnAttackerArmed -= HandleGuidedArm;

        // 1단계를 한 장으로 좁힌 스텝은 ForcedAttacker를 걸어 뒀다 → 반드시 풀어준다.
        // 안 풀면 다음 스텝/자유 플레이에서 그 카드 말고는 아무것도 안 눌린다.
        if (ResolveGuidedAttacker() != null)
        {
            TurnState.ForcedAttacker = null;
            CardView.RestoreAllFades();
        }
        TutorialOverlayUI.Instance?.ClearFieldFocus();
    }

    // 무장(아군 선택) 상태 변화 → 포커스 진영 전환. _armed=null이면 다시 아군을 고를 차례.
    void HandleGuidedArm(CardView _armed) => ShowGuidedPhase(_armed);

    void ShowGuidedPhase(CardView _armed)
    {
        var t_overlay = TutorialOverlayUI.Instance;
        if (t_overlay == null) return;

        bool t_pickEnemy = _armed != null;
        BattleFieldView t_view = t_pickEnemy ? this.ctx.enemyFieldView : this.ctx.playerFieldView;
        if (t_view == null) return;

        // 2단계 문구가 비어 있으면 1단계 문구를 유지한다(저작이 한 줄로 끝내고 싶은 경우).
        string t_msg = t_pickEnemy && !string.IsNullOrWhiteSpace(this.guidedStep.targetGuideMessage)
            ? this.guidedStep.targetGuideMessage
            : this.guidedStep.guideMessage;

        TutorialScenarioData.BannerAnchor t_anchor = t_pickEnemy
            ? this.guidedStep.targetBannerAnchor
            : this.guidedStep.bannerAnchor;

        // 1단계에 한해 **지정 아군 한 장**으로 좁힐 수 있다(cardFocusSide=Player + cardFocusSlot).
        // "이 카드로 공격해봐 → 상대는 아무나" 같은 스텝용. 지정이 없으면 종전대로 아군 필드 전체.
        // 2단계(적 고르기)는 항상 필드 전체다 — 대상까지 찍을 거면 자유 스텝이 아니라 슬롯 지정 스텝을 쓴다.
        CardView t_only = t_pickEnemy ? null : ResolveGuidedAttacker();

        if (t_only != null)
        {
            // 그 카드만 조작 가능하게(다른 아군 탭 무시) + 나머지 암전. 무장 뒤에도 유지해
            // 2단계에서 공격자가 바뀌지 않게 한다.
            TurnState.ForcedAttacker = t_only.BoundCard;
            CardView.RestoreAllFades();
            t_overlay.ShowCardFocus(t_msg, t_anchor, _waitTap: false, t_only);
        }
        else
        {
            t_overlay.ShowFieldFocus(t_view.ScreenBounds(GuidedFocusPadding), t_msg, t_anchor);
        }

        // 지정 핸드는 그 진영 단계에서만 적용 — 아군 고르는 중에 적 슬롯을 가리키면 안 된다.
        var t_wantSide = t_pickEnemy ? TutorialScenarioData.CardFocusSide.Enemy
                                     : TutorialScenarioData.CardFocusSide.Player;
        if (this.guidedStep.handSide == t_wantSide) ApplyHandOverride(this.guidedStep);
    }

    /// <summary>자유 선택 1단계를 좁힐 지정 아군. cardFocusSide가 Player일 때만 의미가 있다
    /// (Enemy 지정은 카드 낱장 설명용이라 여기선 무시 — 아군 고르는 단계에 적을 열 수 없다).
    /// 그 슬롯이 비었으면 null → 필드 전체로 자연 폴백한다.</summary>
    CardView ResolveGuidedAttacker()
    {
        if (this.guidedStep.cardFocusSide != TutorialScenarioData.CardFocusSide.Player) return null;
        CardView t_view = ResolveSlotView(this.guidedStep.cardFocusSide, this.guidedStep.cardFocusSlot);
        return t_view != null && t_view.BoundCard != null ? t_view : null;
    }

    const float GuidedFocusPadding = 24f;   // 구멍 여유(px) — 카드 테두리가 딤에 물리지 않게

    /// <summary>튜토리얼 자유 꼬리: 스크립트 소진 후 일반 전투처럼 자유 공격 턴을 연다.
    /// 강제 지정·암전·안내 배너 없이 아무 로컬 카드로 아무 적 카드를 공격 가능. 스텝 소비 없음.</summary>
    void EnterFreePlay()
    {
        this.forcedAttacker      = null;
        TurnState.ForcedAttacker = null;
        TurnState.ForcedTarget   = null;
        TurnState.AllowedGesture = InputGesture.Any;   // 자유 꼬리 = 조작 제한 해제
        CardView.ForcedDimAlpha  = 0.3f;   // 튜토리얼 암전 강도 원복(일반 전투 기본)
        CardView.RestoreAllFades();        // forced 없음 → 전부 밝게
        EndGuidedFreeSelect();
        TutorialOverlayUI.Instance?.Clear();   // 안내 배너/힌트 제거
        TurnState.InputAllowed   = true;
    }

    static bool InSlotRange(int _slot) => TutorialStepGate.InSlotRange(_slot);

    /// <summary>자유공격 스텝: 공격자·타깃 슬롯 둘 다 -1 → 강제 지정 없이 아무 카드로 아무 적을 공격 가능.</summary>
    static bool IsFreeStep(TutorialScenarioData.ScriptedAttack _step) => TutorialStepGate.IsFreeStep(_step);

    void HandleCardViewAttack(CardView _attacker, CardView _target)
    {
        CardInstance t_attCard = _attacker.BoundCard;
        CardInstance t_defCard = _target.BoundCard;

        if (t_attCard == null || t_attCard.ownerIndex != TurnState.LocalOwnerIndex) return;
        if (t_defCard == null || t_defCard.ownerIndex == TurnState.LocalOwnerIndex) return;
        if (this.forcedAttacker != null && t_attCard != this.forcedAttacker) return;

        // 규칙 백스톱: 도발/지정 타깃 필터의 집행을 뷰(CardView.HandleEnemyTap)에만 맡기면
        // 뷰를 우회한 입력이 규칙을 깬다. 판정은 BattleRules 단독 — 위반이면 조용히 무시
        // (거절 연출·안내는 뷰가 이미 담당). 스텝 소비 전에 검사해야 스크립트가 어긋나지 않는다.
        if (!this.ctx.enemyField.CanAttack(t_attCard, t_defCard))
        {
            // 뷰는 이미 무장을 풀고 공격 연출용으로 VFX만 다시 켠 상태다. 여기서 거절하면
            // 그 VFX를 끌 주체(AttackSequence)가 안 돌아 공격자에 이펙트가 고착된다.
            return;
        }

        // 튜토리얼: 스크립트 스텝(공격자 슬롯·타깃 슬롯)과 일치하는 공격만 허용. 불일치 = 입력 무시.
        this.scriptedStepAttack = false;
        if (TutorialConfig.IsActive)
        {
            if (!TutorialConfig.TryPeekPlayerStep(out var t_step))
            {
                // 스텝 소진: 자유 전환이면 소비 없이 통과(아무 카드로 아무 적 공격), 아니면 무시.
                // 무효 스텝 폐기로 스크립트가 끊긴 경우도 자유 통과 — Execute가 이미 자유 플레이를 열었다.
                if (!TutorialConfig.FreePlayAfterScript && !TutorialConfig.ScriptDerailed) return;
            }
            else
            {
                if (t_step.kind != TutorialScenarioData.StepKind.Attack) return; // 설명 스텝 중 공격 차단(탭으로만 진행)
                // 자유공격이 아니면 스크립트 슬롯과 일치하는 공격만 허용(불일치=입력 무시).
                if (!IsFreeStep(t_step))
                {
                    if (t_attCard.slotIndex != t_step.attackerSlot) return;
                    if (t_defCard.slotIndex != t_step.targetSlot) return;
                    this.scriptedStepAttack = true;   // 스크립트대로 진행 → 결과 보드를 기준선으로 재동기
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

        // 지정 타깃(튜토리얼) > 도발 > 전체 + slot 오름차순. 수동 공격(CardView 필터)과 같은 규칙 함수다 —
        // 자동공격만 ForcedTarget을 모르면 스크립트가 지정한 적이 아닌 카드를 친다.
        var t_targets = this.ctx.enemyField.GetValidTargets(t_attacker);
        CardInstance t_target = t_targets.Count > 0 ? t_targets[0] : null;

        // 방어적: 유효 공격자/타깃 없으면 입력 유지한 채 반환(hang 방지, 다음 tick 재시도).
        if (t_attacker == null || !t_attacker.IsAlive || t_target == null) return;

        TurnState.InputAllowed = false;        // 유효 공격 확정 후에만 입력 차단
        DeckPileUI.CloseAny();                 // 덱을 열어둔 채 시간이 다 됐으면 닫는다(연출이 패널에 가리지 않게)
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
        EndGuidedFreeSelect();   // 공격이 나갔으면 이번 선택 안내는 끝 — 연출 중 무장 통지에 반응하지 않게
        if (TutorialConfig.IsActive) TutorialOverlayUI.Instance?.Clear();

        CardView t_attackerView = this.ctx.playerFieldView.GetSlotView(_attacker.slotIndex);
        CardView t_defenderView = this.ctx.enemyFieldView.GetSlotView(_defender.slotIndex);

        var (t_preSelectedSplash, t_splashView) = AttackFlow.PreSelectSplash(
            _attacker, _defender, this.ctx.enemyField, this.ctx.enemyFieldView);

        AttackResult t_result = default;
        Action t_onEffect = () => t_result = AttackProcessor.Execute(
            _attacker, _defender, this.ctx.playerField, this.ctx.enemyField, t_preSelectedSplash);

        var (t_preKw, t_atKw) = AttackFlow.Keywords(_attacker);

        await AttackFlow.RunBeforeAttack(_attacker, _defender, this.ctx.playerField, this.ctx.enemyField,
                                         t_preSelectedSplash);   // 낙인 선피해(Execute 전 원자)

        await AttackSequence.Play(t_attackerView, t_defenderView, t_splashView,
            t_onEffect, t_preKw, t_atKw,
            () => AttackFlow.RunAfterAttack(_attacker, _defender, this.ctx.playerField, this.ctx.enemyField, t_result));

        // 교활 퇴장은 보충 **전**에 — 슬롯 뷰가 아직 물러나는 카드를 그리고 있는 동안만 가능하다.
        await AttackFlow.PlayCunningSwap(this.ctx.playerFieldView, t_attackerView, t_result);

        await this.ctx.FillAndAnimate();

        // 튜토리얼: 슬롯 지정 스텝대로 끝난 공격의 결과 보드 = 스크립트가 기대하는 보드 → 기준선 재동기.
        // 자유공격/자유플레이 결과는 재동기하지 않는다(뒤 스텝이 어긋남을 감지해야 하므로).
        if (TutorialConfig.IsActive && this.scriptedStepAttack)
            TutorialConfig.SyncBoardBaseline(this.ctx.playerField, this.ctx.enemyField);

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

            // 처형 재공격 = 대상 자동 발사. 입력을 열지 않고 같은 공격 경로로 다시 들어간다.
            // 튜토리얼도 같은 경로다 — 예전엔 여기서 빠져나가 "처형인데 대상을 또 고르는" 흐름이 남아 있었다.
            // 대상만 갈린다: 스크립트가 이 공격자의 다음 타깃을 지정해 뒀으면 그걸, 아니면 무작위.
            if (BattleUxFlags.ExecutionRandomTarget)
            {
                CardInstance t_nextTarget = TutorialScriptedExecutionTarget(_attacker)
                                         ?? ExecutionRule.PickRandomTarget(_attacker, this.ctx.enemyField);
                if (t_nextTarget != null)
                {
                    // 연속 공격이 한 동작으로 뭉쳐 보이지 않게 상대 연속 공격과 같은 간격을 둔다.
                    await UniTask.Delay((int)(GameTiming.Battle.OpponentExtraAttackDelay * 1000));
                    ExecuteAttack(_attacker, t_nextTarget);
                    return;
                }

                // 칠 대상이 없으면 턴을 닫는다 — 여기서 입력을 열면 아무것도 못 고르고 잠긴다.
                this.forcedAttacker      = null;
                TurnState.ForcedAttacker = null;
                CardView.RestoreAllFades();
                this.turnDone            = true;
                return;
            }

            // 튜토리얼: 같은 턴 다음 스텝(처형 연속 공격) 준비 — 선행 Message 소진 후 공격 안내.
            // 처형 공격자 슬롯 일치 검증(_attacker 전달). 남은/유효 스텝 없으면 hang 방지로 턴 종료.
            if (TutorialConfig.IsActive)
            {
                if (!await PrepareTutorialStepsAsync(_attacker))
                {
                    // 자유 전환: 처형 재공격은 규칙상 그 카드로만 계속 → forcedAttacker(_attacker) 유지,
                    // 암전/하이라이트도 위에서 잡은 그대로 두고 입력만 재개(안내 없음).
                    // 단 큐가 비었을 때만 — 스텝이 남아 있는데(공격자 슬롯 불일치로 준비 실패) 입력을 열면
                    // HandleCardViewAttack의 슬롯 게이트가 모든 공격을 거절해 턴이 잠긴다(턴 종료로 자연 복구).
                    if ((TutorialConfig.FreePlayAfterScript || TutorialConfig.ScriptDerailed)
                        && !TutorialConfig.TryPeekPlayerStep(out _))
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
