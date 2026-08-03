using UnityEngine;
using ScriptedAttack = TutorialScenarioData.ScriptedAttack;

/// <summary>
/// 튜토리얼 공격 스텝 유효성 판정의 단일 진실원.
/// PlayerTurn/EnemyTurn이 각자 복제하던 "폐기 루프 + 실행 가능 판정" 쌍을 진영(<see cref="Side"/>) 하나로
/// 파라미터화했다 — 규칙을 한쪽만 고쳐 플레이어 턴과 적 턴이 서로 다른 기준을 쓰는 사고(컴파일러가 못 잡는다)를
/// 구조로 막는다. 스텝 큐 API(<see cref="TutorialConfig"/>)의 <b>소비자</b>일 뿐 큐를 소유하지 않는다.
/// 랜덤 미사용 — 판정은 보드 상태와 기준선만 본다(결정론 유지).
/// </summary>
public static class TutorialStepGate
{
    /// <summary>스텝 큐 진영 = 그 스크립트로 공격하는 쪽. Player = 플레이어 큐/플레이어 필드가 공격측.</summary>
    public enum Side { Player, Enemy }

    /// <summary>
    /// 큐 앞의 "선행 안내 + 공격 스텝 1개"를 한 묶음으로 보고, 실행 불가한 묶음을 통째로 조용히 폐기한다.
    /// 안내까지 함께 버리는 이유: 죽은 카드를 설명하는 문구가 뜨는 것 자체가 버그다.
    /// 공격 스텝이 더 없는 꼬리(안내만 남음)는 손대지 않는다 — 마무리 문구는 그대로 보여준다.
    /// </summary>
    public static void DiscardUnplayable(Side _side, BattleField _attackerField, BattleField _defenderField)
    {
        while (true)
        {
            int t_ahead = 0;
            while (TryPeekStep(_side, t_ahead, out var t_lead)
                   && t_lead.kind != TutorialScenarioData.StepKind.Attack)
                t_ahead++;

            if (!TryPeekStep(_side, t_ahead, out var t_attack)) return;                  // 남은 공격 스텝 없음
            if (IsPlayable(_side, t_attack, _attackerField, _defenderField)) return;     // 유효 묶음 도달

            Debug.LogWarning($"[Tutorial] {SideLabel(_side)} 공격 스텝 무효(atk={t_attack.attackerSlot}, def={t_attack.targetSlot})" +
                             $" → 선행 안내 포함 {t_ahead + 1}개 스킵");
            for (int i = 0; i <= t_ahead; i++) DiscardStep(_side);
        }
    }

    /// <summary>튜토리얼 공격 스텝이 지금 실행 가능한가(범위·생존·기준선 일치). 도발 필터는 의도적 미적용.
    /// 기준선 대조 = 죽은 카드 자리를 채운 다른 카드가 스크립트 공격자/타깃이 되는 것을 막는다.</summary>
    public static bool IsPlayable(Side _side, ScriptedAttack _step,
                                  BattleField _attackerField, BattleField _defenderField)
    {
        // 자유공격: 생존 공격측·수비측이 각각 1장 이상이면 실행 가능(슬롯 무관 — 대상은 입력/AI가 고른다).
        if (IsFreeStep(_step))
            return _attackerField.GetActiveCards().Count > 0
                && _defenderField.GetActiveCards().Count > 0;
        if (!InSlotRange(_step.attackerSlot) || !InSlotRange(_step.targetSlot)) return false;
        CardInstance t_atk = _attackerField.GetSlot(_step.attackerSlot);
        CardInstance t_def = _defenderField.GetSlot(_step.targetSlot);
        if (t_atk == null || !t_atk.IsAlive || t_def == null || !t_def.IsAlive) return false;
        // 생존만으론 부족하다 — 죽은 카드 자리를 대기 카드가 채우면 슬롯 지정이 엉뚱한 카드에 붙는다.
        // 스크립트가 그 슬롯에서 기대한 카드와 실제 점유 카드가 다르면 실행 불가로 본다.
        return MatchesBaseline(_side, _step.attackerSlot, t_atk)
            && MatchesBaseline(Opposite(_side), _step.targetSlot, t_def);
    }

    /// <summary>슬롯 인덱스가 필드 범위 안인가(-1 = 무지정).</summary>
    public static bool InSlotRange(int _slot) => _slot >= 0 && _slot < BattleField.SLOT_COUNT;

    /// <summary>자유공격 스텝: 공격자·타깃 슬롯 둘 다 -1 → 슬롯 강제 없이 공격측이 대상을 고른다.</summary>
    public static bool IsFreeStep(ScriptedAttack _step)
        => _step.attackerSlot < 0 && _step.targetSlot < 0;

    // ── 진영별 큐/기준선 위임 ────────────────────────────────────────────────
    // 두 턴 구현의 진짜 차이는 이 네 함수뿐이다(큐 조회·폐기, 기준선 대조, 로그 라벨).

    static bool TryPeekStep(Side _side, int _offset, out ScriptedAttack _step)
    {
        if (_side == Side.Player) return TutorialConfig.TryPeekPlayerStep(_offset, out _step);
        return TutorialConfig.TryPeekEnemyStep(_offset, out _step);
    }

    static void DiscardStep(Side _side)
    {
        if (_side == Side.Player) TutorialConfig.DiscardPlayerStep();
        else                      TutorialConfig.DiscardEnemyStep();
    }

    static bool MatchesBaseline(Side _side, int _slot, CardInstance _card)
        => _side == Side.Player
            ? TutorialConfig.MatchesPlayerBaseline(_slot, _card)
            : TutorialConfig.MatchesEnemyBaseline(_slot, _card);

    static Side Opposite(Side _side) => _side == Side.Player ? Side.Enemy : Side.Player;

    static string SideLabel(Side _side) => _side == Side.Player ? "플레이어" : "적";
}
