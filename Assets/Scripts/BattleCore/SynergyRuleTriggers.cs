// 활성 시너지 효과의 규칙 훅만 동기 실행한다. UI, CardCatalog, UniTask를 참조하지 않는다.
// Active 및 Tier.effects 순회 순서는 결정론 계약이다.
public static class SynergyRuleTriggers
{
    public static void Placed(SpawnCtx _ctx)
    {
        SynergyState t_state = _ctx.field?.Synergy;
        if (t_state == null || _ctx.self == null) return;
        foreach (ActiveSynergy t_active in t_state.Active)
        {
            if (!CanRun(t_active) || !SynergyApplier.BelongsTo(_ctx.self, t_active.Runtime)) continue;
            foreach (SynergyEffect t_effect in t_active.Tier.effects)
                t_effect?.OnPlaced(_ctx.WithSynergy(t_active.Runtime));
        }
    }

    public static void TurnBegan(TurnCtx _ctx)
    {
        SynergyState t_state = _ctx.field?.Synergy;
        if (t_state == null || _ctx.self == null) return;
        foreach (ActiveSynergy t_active in t_state.Active)
        {
            if (!CanRun(t_active) || !SynergyApplier.BelongsTo(_ctx.self, t_active.Runtime)) continue;
            foreach (SynergyEffect t_effect in t_active.Tier.effects)
                t_effect?.OnTurnBegan(_ctx.WithSynergy(t_active.Runtime));
        }
    }

    public static void TurnEnded(TurnCtx _ctx)
    {
        SynergyState t_state = _ctx.field?.Synergy;
        if (t_state == null || _ctx.self == null) return;
        foreach (ActiveSynergy t_active in t_state.Active)
        {
            if (!CanRun(t_active) || !SynergyApplier.BelongsTo(_ctx.self, t_active.Runtime)) continue;
            foreach (SynergyEffect t_effect in t_active.Tier.effects)
                t_effect?.OnTurnEnded(_ctx.WithSynergy(t_active.Runtime));
        }
    }

    public static void BeforeAttack(BeforeAttackCtx _ctx,
                                    System.Action<SynergyEffect, BeforeAttackCtx> _beforeInvoke = null)
    {
        SynergyState t_state = _ctx.ownField?.Synergy;
        if (t_state == null || _ctx.self == null || !_ctx.self.IsAlive || _ctx.defender == null) return;
        foreach (ActiveSynergy t_active in t_state.Active)
        {
            if (!CanRun(t_active) || !SynergyApplier.BelongsTo(_ctx.self, t_active.Runtime)) continue;
            foreach (SynergyEffect t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                BeforeAttackCtx t_effectCtx = _ctx.WithSynergy(t_active.Runtime);
                _beforeInvoke?.Invoke(t_effect, t_effectCtx); // ★ presentation 캡처가 규칙보다 먼저다.
                t_effect.OnBeforeAttack(t_effectCtx);
            }
        }
    }

    public static void Attacked(AttackedCtx _ctx)
    {
        SynergyState t_state = _ctx.ownField?.Synergy;
        if (t_state == null || _ctx.self == null || _ctx.attacker == null) return;
        foreach (ActiveSynergy t_active in t_state.Active)
        {
            if (!CanRun(t_active) || !SynergyApplier.BelongsTo(_ctx.self, t_active.Runtime)) continue;
            foreach (SynergyEffect t_effect in t_active.Tier.effects)
                t_effect?.OnAttacked(_ctx.WithSynergy(t_active.Runtime));
        }
    }

    public static void DamageDealt(DamageDealtCtx _ctx)
    {
        SynergyState t_state = _ctx.field?.Synergy;
        if (t_state == null || _ctx.self == null) return;
        foreach (ActiveSynergy t_active in t_state.Active)
        {
            if (!CanRun(t_active) || !SynergyApplier.BelongsTo(_ctx.self, t_active.Runtime)) continue;
            foreach (SynergyEffect t_effect in t_active.Tier.effects)
                t_effect?.OnDamageDealt(_ctx.WithSynergy(t_active.Runtime));
        }
    }

    public static void Lethal(DeathCtx _ctx)
    {
        SynergyState t_state = _ctx.field?.Synergy;
        if (t_state == null || _ctx.self == null) return;
        foreach (ActiveSynergy t_active in t_state.Active)
        {
            if (!CanRun(t_active) || !SynergyApplier.BelongsTo(_ctx.self, t_active.Runtime)) continue;
            foreach (SynergyEffect t_effect in t_active.Tier.effects)
                t_effect?.OnLethal(_ctx.WithSynergy(t_active.Runtime));
        }
    }

    public static void Removed(DeathCtx _ctx)
    {
        SynergyState t_state = _ctx.field?.Synergy;
        if (t_state == null || _ctx.self == null) return;
        foreach (ActiveSynergy t_active in t_state.Active)
        {
            if (!CanRun(t_active) || !SynergyApplier.BelongsTo(_ctx.self, t_active.Runtime)) continue;
            foreach (SynergyEffect t_effect in t_active.Tier.effects)
                t_effect?.OnRemoved(_ctx.WithSynergy(t_active.Runtime));
        }
    }

    public static void SwappedOut(SwapOutCtx _ctx)
    {
        SynergyState t_state = _ctx.field?.Synergy;
        if (t_state == null || _ctx.self == null) return;
        foreach (ActiveSynergy t_active in t_state.Active)
        {
            if (!CanRun(t_active) || !SynergyApplier.BelongsTo(_ctx.self, t_active.Runtime)) continue;
            foreach (SynergyEffect t_effect in t_active.Tier.effects)
                t_effect?.OnSwappedOut(_ctx.WithSynergy(t_active.Runtime));
        }
    }

    // Entered와 BoardChanged는 비소속 카드까지 훑어야 하므로 BelongsTo 필터를 걸지 않는다.
    public static void Entered(SpawnCtx _ctx)
    {
        SynergyState t_state = _ctx.field?.Synergy;
        if (t_state == null || _ctx.self == null) return;
        foreach (ActiveSynergy t_active in t_state.Active)
        {
            if (!CanRun(t_active)) continue;
            foreach (SynergyEffect t_effect in t_active.Tier.effects)
                t_effect?.OnEntered(_ctx.WithSynergy(t_active.Runtime));
        }
    }

    public static void AfterAttack(AfterAttackCtx _ctx,
                                   System.Action<SynergyEffect, AfterAttackCtx> _beforeInvoke = null)
    {
        SynergyState t_state = _ctx.ownField?.Synergy;
        if (t_state == null || _ctx.self == null || !_ctx.self.IsAlive) return;
        foreach (ActiveSynergy t_active in t_state.Active)
        {
            if (!CanRun(t_active) || !SynergyApplier.BelongsTo(_ctx.self, t_active.Runtime)) continue;
            foreach (SynergyEffect t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                AfterAttackCtx t_effectCtx = _ctx.WithSynergy(t_active.Runtime);
                _beforeInvoke?.Invoke(t_effect, t_effectCtx); // ★ presentation 캡처가 규칙보다 먼저다.
                t_effect.OnAfterAttack(t_effectCtx);
            }
        }
    }

    public static void BoardChanged(BoardCtx _ctx)
    {
        SynergyState t_state = _ctx.field?.Synergy;
        if (t_state == null) return;
        foreach (ActiveSynergy t_active in t_state.Active)
        {
            if (!CanRun(t_active)) continue;
            foreach (SynergyEffect t_effect in t_active.Tier.effects)
                t_effect?.OnBoardChanged(_ctx.WithSynergy(t_active.Runtime));
        }
    }

    static bool CanRun(ActiveSynergy _active)
        => _active?.Runtime != null && _active.Tier?.effects != null;
}
