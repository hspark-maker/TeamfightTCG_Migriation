using System.Collections.Generic;
using Cysharp.Threading.Tasks;

// 외부 전투 코드가 보는 facade. 규칙은 SynergyRuleTriggers, Unity 표시는 이 타입이 소유한다.
public static class SynergyTriggers
{
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void InstallPresentationStream()
    {
        SynergyPresentationStream.Published -= OnPresentationPublished;
        SynergyPresentationStream.Published += OnPresentationPublished;
    }

    public static bool Fire(CardInstance _self, SynergyRuntime _synergy, BattleFieldState _field = null)
    {
        if (_self == null || _synergy == null) return false;
        if (!CardCatalog.TryGetSynergyData(_synergy, out SynergyData t_presentation))
        {
            UnityEngine.Debug.LogError($"[SynergyTriggers] Missing presentation data for synergy '{_synergy.SynergyId}'.");
            return false;
        }

        if (BattlePresentationQueue.IsDeferring)
        {
            CardInstance t_self = _self;
            BattleFieldState t_field = _field;
            BattlePresentationQueue.Run(() => Present(t_self, t_presentation, t_field));
            return false;
        }

        return Present(_self, t_presentation, _field);
    }

    static bool Present(CardInstance _self, SynergyData _synergy, BattleFieldState _field)
    {
        if (_self == null || _synergy == null) return false;
        CardView.GetView(_self)?.PopSynergyBadge(_synergy);
        FieldSynergyPanel.Pop(_self.ownerIndex == TurnState.LocalOwnerIndex, _synergy);
        return SynergyEmblemVfx.PlayTriggered(_self, _synergy, _field);
    }

    public static void Placed(SpawnCtx _ctx) => SynergyRuleTriggers.Placed(_ctx);

    public static UniTask TurnBegan(TurnCtx _ctx)
    {
        SynergyRuleTriggers.TurnBegan(_ctx);
        return UniTask.CompletedTask;
    }

    public static void TurnEnded(TurnCtx _ctx) => SynergyRuleTriggers.TurnEnded(_ctx);

    public static async UniTask BeforeAttack(BeforeAttackCtx _ctx)
    {
        var t_presentations = new List<ISynergyPresentationPlan>();
        SynergyRuleTriggers.BeforeAttack(_ctx, (t_effect, t_effectCtx) =>
        {
            if (!(t_effect is IBeforeAttackPlanSource t_source)) return;
            ISynergyPresentationPlan t_plan = t_source.CaptureBeforeAttackPlan(t_effectCtx);
            if (t_plan != null) t_presentations.Add(t_plan);
        });

        foreach (ISynergyPresentationPlan t_plan in t_presentations)
            await PlayPresentation(t_plan);
    }

    public static void Attacked(AttackedCtx _ctx) => SynergyRuleTriggers.Attacked(_ctx);
    public static void DamageDealt(DamageDealtCtx _ctx) => SynergyRuleTriggers.DamageDealt(_ctx);
    public static void Lethal(DeathCtx _ctx) => SynergyRuleTriggers.Lethal(_ctx);
    public static void Removed(DeathCtx _ctx) => SynergyRuleTriggers.Removed(_ctx);
    public static void SwappedOut(SwapOutCtx _ctx) => SynergyRuleTriggers.SwappedOut(_ctx);
    public static void Entered(SpawnCtx _ctx) => SynergyRuleTriggers.Entered(_ctx);

    public static async UniTask AfterAttack(AfterAttackCtx _ctx)
    {
        var t_presentations = new List<ISynergyPresentationPlan>();
        SynergyRuleTriggers.AfterAttack(_ctx, (t_effect, t_effectCtx) =>
        {
            if (!(t_effect is IAfterAttackPlanSource t_source)) return;
            ISynergyPresentationPlan t_plan = t_source.CaptureAfterAttackPlan(t_effectCtx);
            if (t_plan != null) t_presentations.Add(t_plan);
        });

        foreach (ISynergyPresentationPlan t_plan in t_presentations)
            await PlayPresentation(t_plan);
    }

    static UniTask PlayPresentation(ISynergyPresentationPlan _plan)
    {
        if (_plan is BrandAttackPlan t_brand) return BrandSynergyPresentation.Play(t_brand);
        if (_plan is PredatorDrainPlan t_predator) return PredatorSynergyPresentation.Play(t_predator);
        return UniTask.CompletedTask;
    }

    static void OnPresentationPublished(ISynergyPresentationPlan _plan)
    {
        switch (_plan)
        {
            case SynergyFirePlan t_fire:
                PresentFire(t_fire);
                break;
            case FlowAttackPresentationPlan t_flow:
                PresentFlowAttack(t_flow);
                break;
            case CaretakerPresentationPlan t_caretaker:
                PresentCaretaker(t_caretaker);
                break;
            case LegacyTurnPresentationPlan t_legacyTurn:
                PresentLegacyTurn(t_legacyTurn);
                break;
            case LegacyDeathPresentationPlan t_legacyDeath:
                PresentLegacyDeath(t_legacyDeath);
                break;
            case TraceMarkPresentationPlan t_trace:
                PresentTraceMark(t_trace);
                break;
        }
    }

    static void PresentFire(SynergyFirePlan _plan)
    {
        if (_plan == null) return;
        System.Action t_show = () => Fire(_plan.self, _plan.synergy, _plan.field);
        switch (_plan.timing)
        {
            case SynergyPresentationTiming.OnDeath:
                BattlePresentationQueue.RunOnDeath(t_show);
                break;
            case SynergyPresentationTiming.OnKill:
                BattlePresentationQueue.RunOnKill(t_show);
                break;
            default:
                t_show();
                break;
        }
    }

    static void PresentFlowAttack(FlowAttackPresentationPlan _plan)
    {
        if (_plan == null) return;
        Fire(_plan.self, _plan.synergy, _plan.field);
        if (CardCatalog.TryGetSynergyData(_plan.synergy, out SynergyData t_presentation))
            SynergyVfx.PlayFlowWind(_plan.self, _plan.field,
                t_presentation.vfx as FlowSynergyVfxConfig);
    }

    static void PresentCaretaker(CaretakerPresentationPlan _plan)
    {
        if (_plan == null || _plan.self == null) return;

        var t_shots = new List<(CardView view, CardInstance card, int amount)>();
        if (_plan.targets != null)
        {
            foreach (SynergyHealTarget t_target in _plan.targets)
            {
                CardView t_view = CardView.GetView(t_target.card);
                if (t_view != null) t_shots.Add((t_view, t_target.card, t_target.amount));
            }
        }

        CardLandingPresentation.Enqueue(_plan.self, () =>
        {
            Fire(_plan.self, _plan.synergy, _plan.field);
            foreach (var (t_view, t_card, t_amount) in t_shots)
            {
                if (t_view == null || t_view.BoundCard != t_card) continue;
                t_view.PlayHealEffect(t_amount, _consumeDeferred: true);
            }
        });
    }

    static void PresentLegacyTurn(LegacyTurnPresentationPlan _plan)
    {
        if (_plan == null) return;
        Fire(_plan.self, _plan.synergy, _plan.field);
        if (CardCatalog.TryGetSynergyData(_plan.synergy, out SynergyData t_presentation))
            LegacyCrownVfx.Show(_plan.self, t_presentation);
    }

    static void PresentLegacyDeath(LegacyDeathPresentationPlan _plan)
    {
        if (_plan == null || _plan.healed == null || _plan.healed.Count == 0) return;
        BattlePresentationQueue.RunOnDeath(() =>
        {
            Fire(_plan.self, _plan.synergy, _plan.field);
            if (CardCatalog.TryGetSynergyData(_plan.synergy, out SynergyData t_presentation))
                LegacyCrownVfx.PlayHealTrails(
                    _plan.self, _plan.healed, _plan.amount, t_presentation);
        });
    }

    static void PresentTraceMark(TraceMarkPresentationPlan _plan)
    {
        if (_plan == null) return;
        Fire(_plan.self, _plan.synergy, _plan.field);
        CardCatalog.TryGetSynergyData(_plan.synergy, out SynergyData t_presentation);
        if (!(t_presentation?.vfx is TraceSynergyVfxConfig t_vfx) || t_vfx.mark.prefab == null) return;

        CardView t_view = CardView.GetView(_plan.target);
        if (t_view == null) return;
        BattleVfx.Play(t_vfx.mark, t_view.SlotPosition, t_view.VfxSortingLayerId);
    }

    public static void BoardChanged(BoardCtx _ctx) => SynergyRuleTriggers.BoardChanged(_ctx);
}
