using System;
using System.Collections.Generic;

/// <summary>탭 공격(제스처3)의 **무장 상태 단일 진실원**. 카드 한 장의 표현이 아니라
/// "지금 누가 공격자로 무장돼 있는가 / 그 무장에 딸린 프리뷰·안내가 무엇인가"만 여기 있다.
///
/// 방향은 단방향이다: BattleSelection → CardView/BattleBoardView.
/// CardView는 아래 API로만 상태를 바꾼다(필드 직접 접근 금지).</summary>
public static class BattleSelection
{
    public static event Action<CardView, CardView> OnAttack;

    /// <summary>탭 무장 상태가 **실제로 바뀌었을 때만** 통지(무장=그 카드, 해제=null).
    /// 튜토리얼 가이드가 "아군 골랐다 → 이제 적 고를 차례"로 넘어가는 유일한 신호다.
    /// 다른 카드로 갈아타는 경우엔 null을 거치지 않는다 — 안내 배너가 한 프레임 깜빡이지 않게.</summary>
    public static event Action<CardView> OnAttackerArmed;

    static CardView s_selectedAttacker;   // 탭 공격(제스처3)으로 무장된 공격자. null=미무장.
    static CardView s_notifiedArmed;      // 마지막으로 통지한 무장 상태(중복 통지 억제)
    static List<CardView> s_previewTargets;   // 무장 시 HP 프리뷰를 켠 타겟들(해제 시 끄기용).
    static bool s_tauntNoticeShown;   // 이번 무장에서 도발 차단 안내를 이미 띄웠나(연타 배너 스팸 방지).

    /// <summary>지금 탭으로 무장된 공격자(없으면 null). 튜토리얼이 "이미 무장돼 있는가"를 먼저 확인해
    /// <see cref="OnAttackerArmed"/> 구독만 걸고 영영 기다리는 상황을 피한다.</summary>
    public static CardView SelectedAttacker => s_selectedAttacker;

    /// <summary>이번 조준에서 도발 차단 안내를 이미 띄웠나(읽기 전용 — 세우는 건 <see cref="MarkTauntNoticeShown"/>).</summary>
    public static bool TauntNoticeShown => s_tauntNoticeShown;

    static void NotifyArmed(CardView _armed)
    {
        if (s_notifiedArmed == _armed) return;
        s_notifiedArmed = _armed;
        OnAttackerArmed?.Invoke(_armed);
    }

    /// <summary>공격 발동 통지. 무장 상태와 무관하게 드래그 공격 경로도 이걸로 발화한다
    /// — 외부(PlayerTurn/Multiplayer/테스터)가 공격을 받는 유일한 창구.</summary>
    public static void NotifyAttack(CardView _attacker, CardView _target)
        => OnAttack?.Invoke(_attacker, _target);

    /// <summary>무장 확정. 호출부(CardView.ToggleSelectAttacker)가 강조/페이드/HP 프리뷰를 다 켠 뒤
    /// 마지막에 부른다 — 통지 시점엔 화면이 이미 무장 상태여야 한다.
    /// <paramref name="_previewTargets"/>는 프리뷰를 켜 둔 타겟 목록(해제 시 되돌리기용).</summary>
    public static void Arm(CardView _attacker, List<CardView> _previewTargets)
    {
        s_selectedAttacker = _attacker;
        s_previewTargets   = _previewTargets;
        NotifyArmed(_attacker);
    }

    /// <summary>무장 해제: 강조/확대/무기/페이드 원복. _instant=공격 발동 직전(뒤이어 AttackSequence가 transform 장악).
    /// _notify=false는 곧바로 다른 카드를 무장하는 경로 전용(중간 null 통지 생략).</summary>
    public static void Clear(bool _instant = false, bool _notify = true)
    {
        if (s_selectedAttacker == null) return;
        CardView t_prev = s_selectedAttacker;
        s_selectedAttacker = null;

        if (s_previewTargets != null)
        {
            foreach (var t_cv in s_previewTargets)
                if (t_cv != null) t_cv.HideAttackPreview();
            s_previewTargets = null;
        }

        t_prev.SetHighlight(false);
        t_prev.SetTargetFocus(false, _instant);
        t_prev.FocusWeapon(false);
        BattleBoardView.RestoreAllFades();

        if (_notify) NotifyArmed(null);
    }

    /// <summary>이번 조준(드래그 진입/탭 무장)에서 도발 안내를 다시 1회 허용.</summary>
    public static void ResetTauntNotice() => s_tauntNoticeShown = false;

    /// <summary>도발 안내를 띄웠음을 기록 — 이번 조준 동안 배너가 연타로 반복되지 않게.</summary>
    public static void MarkTauntNoticeShown() => s_tauntNoticeShown = true;

    /// <summary>전투 종료 리셋. 호출 지점은 CardView.Cleanup() 하나뿐(BattleCleanup.Run이 그걸 부른다).
    /// 이벤트 구독까지 전부 끊는다 — 다음 전투가 죽은 구독자에게 통지하지 않게.</summary>
    public static void Cleanup()
    {
        OnAttack        = null;
        OnAttackerArmed = null;
        s_selectedAttacker = null;
        s_notifiedArmed    = null;
        s_previewTargets   = null;
        s_tauntNoticeShown = false;
    }
}
