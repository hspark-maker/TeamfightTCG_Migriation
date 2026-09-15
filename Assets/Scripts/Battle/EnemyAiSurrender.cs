using System;
using System.Collections.Generic;

/// <summary>마지막 카드의 모든 합법 공격이 즉시 패배로 끝날 때만 항복한다. 상태·난수는 읽기만 한다.</summary>
public static class EnemyAiSurrender
{
    public static bool ShouldSurrender(BattleFieldState _ai, BattleFieldState _opponent)
    {
        if (_ai == null || _opponent == null || _ai.WaitingCount != 0 || _ai.ActiveCount != 1)
            return false;
        if (!HasPredictableEffects(_ai) || !HasPredictableEffects(_opponent)) return false;

        CardInstance t_attacker = _ai.GetActiveCards()[0];
        if (t_attacker == null || !t_attacker.IsAlive) return false;
        List<CardInstance> t_targets = BattleRules.ValidTargets(t_attacker, _opponent.GetActiveCards(), null);
        if (t_targets.Count == 0) return false;

        foreach (CardInstance t_target in t_targets)
        {
            if (t_target == null || !t_target.IsAlive) return false;
            if (!t_attacker.HasKeyword(CardKeyword.Peerless))
            {
                if (!LosesAttack(t_attacker, t_target, null, _ai, _opponent)) return false;
                continue;
            }

            // AttackProcessor.PickSplash와 같은 인접 슬롯 후보. RNG를 소비하지 않고 모든 분기를 검사한다.
            bool t_hasSplash = false;
            for (int t_offset = -1; t_offset <= 1; t_offset += 2)
            {
                int t_slot = t_target.slotIndex + t_offset;
                if (t_slot < 0 || t_slot >= BattleFieldState.SlotCount) continue;
                CardInstance t_splash = _opponent.GetSlot(t_slot);
                if (t_splash == null) continue;
                t_hasSplash = true;
                if (!t_splash.IsAlive || !LosesAttack(t_attacker, t_target, t_splash, _ai, _opponent))
                    return false;
            }
            if (!t_hasSplash && !LosesAttack(t_attacker, t_target, null, _ai, _opponent)) return false;
        }
        return true;
    }

    static bool LosesAttack(CardInstance _attacker, CardInstance _target, CardInstance _splash,
        BattleFieldState _ai, BattleFieldState _opponent)
    {
        // 예측기의 동시 전멸 편향을 AI 기준으로 둔다. 무승부라면 상대 owner가 반환되어 항복하지 않는다.
        return BattleOverForecast.WillEnd(_attacker, _target, _ai, _opponent, _splash,
            _ai.OwnerIndex, out int t_loser) && t_loser == _ai.OwnerIndex;
    }

    static bool HasPredictableEffects(BattleFieldState _field)
    {
        if (_field.Synergy == null) return true;
        foreach (ActiveSynergy t_active in _field.Synergy.Active)
        {
            SynergyEffect[] t_effects = t_active?.Tier?.effects;
            if (t_effects == null) continue;
            foreach (SynergyEffect t_effect in t_effects)
            {
                if (t_effect == null) continue;
                // 턴 시작 전 판정이다. 낙인 선피해·유산·새 효과는 보수적으로 제외한다.
                // 아래 효과는 정적 수치/등장/표시 또는 공격자 생존 후 효과라 마지막 카드의 반격 즉사를 바꾸지 않는다.
                Type t_type = t_effect.GetType();
                if (t_type != typeof(StatSynergyEffect) && t_type != typeof(FlowSynergyEffect)
                    && t_type != typeof(CaretakerSynergyEffect) && t_type != typeof(PredatorSynergyEffect)
                    && t_type != typeof(TraceSynergyEffect)) return false;
            }
        }
        // 힐러는 자신을 회복하지 않으므로 마지막 카드 한 장의 턴 시작에는 체력이 변하지 않는다.
        return true;
    }
}
