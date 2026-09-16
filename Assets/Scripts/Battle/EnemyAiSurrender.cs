using System;
using System.Collections.Generic;

/// <summary>마지막 카드의 확정 패배 또는 잔여 전력으로 교환조차 불가능한 소모전을 판정한다. 상태·난수는 읽기만 한다.</summary>
public static class EnemyAiSurrender
{
    public static bool ShouldSurrender(BattleFieldState _ai, BattleFieldState _opponent)
    {
        if (_ai == null || _opponent == null || _ai.WaitingCount != 0 || _ai.ActiveCount == 0)
            return false;
        if (_ai.ActiveCount > 1) return CannotTradeRemainingCards(_ai, _opponent);
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

    static bool CannotTradeRemainingCards(BattleFieldState _ai, BattleFieldState _opponent)
    {
        // 양쪽 대기열이 비어야 상대 교활 교체로 약한 카드가 나오는 역전 경로도 닫힌다.
        if (_ai.ActiveCount > 3 || _opponent.WaitingCount != 0 || _opponent.ActiveCount == 0
            || !HasOnlyStaticEffects(_ai) || !HasOnlyStaticEffects(_opponent)) return false;

        List<CardInstance> t_cards = _ai.GetActiveCards();
        long t_damageBudget = 0;
        foreach (CardInstance t_card in t_cards)
        {
            if (t_card == null || !t_card.IsAlive || t_card.HasKeyword(CardKeyword.Healer)
                || (t_card.HasKeyword(CardKeyword.Immortal) && !t_card.reviveUsed)) return false;

            // 각 카드가 낼 수 있는 피해를 넉넉하게 잡는다. 피해 감소·보호막은 상대에게 유리하므로 무시한다.
            t_damageBudget += Math.Max(0, t_card.AttackDamage());
            if (t_card.HasKeyword(CardKeyword.Peerless)) t_damageBudget += Math.Max(0, t_card.SplashDamage());
            if (t_card.HasVanillaEnhance) t_damageBudget += Math.Max(0, t_card.VanillaEnhanceDamage());
        }

        foreach (CardInstance t_target in _opponent.GetActiveCards())
        {
            // 모든 화력을 한 대상에 몰아도 상대가 남아야 한다. 처형·사망 훅·보충이 시작되지 않는다.
            if (t_target == null || !t_target.IsAlive || t_target.hp <= t_damageBudget) return false;

            // HP 1 감소당 공격력은 최대 1 감소한다(도발은 절반). 실제보다 낮은 반격 하한으로 증명한다.
            long t_minCounter = (long)t_target.AttackDamage() - t_damageBudget;
            if (t_minCounter <= 0) return false;
            foreach (CardInstance t_card in t_cards)
                if (!t_card.TakesCounterFrom(t_target) || !t_card.WouldDieFrom((int)t_minCounter)) return false;
        }

        // 어떤 순서로 공격하거나 피격돼도 각 AI 카드는 첫 교환에 죽는다.
        // 따라서 자기 공격 또는 반격을 두 번 할 수 없고, 위 피해 상한을 넘을 수 없다.
        return true;
    }

    static bool HasOnlyStaticEffects(BattleFieldState _field)
    {
        if (_field.Synergy == null) return true;
        foreach (ActiveSynergy t_active in _field.Synergy.Active)
        {
            SynergyEffect[] t_effects = t_active?.Tier?.effects;
            if (t_effects == null) continue;
            foreach (SynergyEffect t_effect in t_effects)
                // 흐름은 등장 때만 성장한다. 이 경로는 양쪽 보충·교체·부활 없이 상대가 모두 살아남는다.
                if (t_effect != null && t_effect.GetType() != typeof(StatSynergyEffect)
                    && t_effect.GetType() != typeof(FlowSynergyEffect)) return false;
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
