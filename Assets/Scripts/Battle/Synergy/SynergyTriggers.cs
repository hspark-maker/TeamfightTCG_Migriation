using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

public interface IBeforeAttackPresentation
{
    // ★ 규칙 적용 전에 호출된다. 피해 전 체력처럼 표시용 선행 스냅샷만 캡처한다.
    Func<UniTask> CaptureBeforeAttackPresentation(BeforeAttackCtx _ctx);
}

public interface IAfterAttackPresentation
{
    // ★ 규칙 적용 전에 호출된다. 효과 인스턴스에는 캡처 상태를 저장하지 않는다.
    Func<UniTask> CaptureAfterAttackPresentation(AfterAttackCtx _ctx);
}

// 외부 전투 코드가 보는 facade. 규칙은 SynergyRuleTriggers, Unity 표시는 이 타입이 소유한다.
public static class SynergyTriggers
{
    public static bool Fire(CardInstance _self, SynergyRuntime _synergy, BattleField _field = null)
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
            BattleField t_field = _field;
            BattlePresentationQueue.Run(() => Present(t_self, t_presentation, t_field));
            return false;
        }

        return Present(_self, t_presentation, _field);
    }

    static bool Present(CardInstance _self, SynergyData _synergy, BattleField _field)
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
        var t_presentations = new List<Func<UniTask>>();
        SynergyRuleTriggers.BeforeAttack(_ctx, (t_effect, t_effectCtx) =>
        {
            if (!(t_effect is IBeforeAttackPresentation t_presenter)) return;
            Func<UniTask> t_presentation = t_presenter.CaptureBeforeAttackPresentation(t_effectCtx);
            if (t_presentation != null) t_presentations.Add(t_presentation);
        });

        foreach (Func<UniTask> t_presentation in t_presentations)
            await t_presentation();
    }

    public static void Attacked(AttackedCtx _ctx) => SynergyRuleTriggers.Attacked(_ctx);
    public static void DamageDealt(DamageDealtCtx _ctx) => SynergyRuleTriggers.DamageDealt(_ctx);
    public static void Lethal(DeathCtx _ctx) => SynergyRuleTriggers.Lethal(_ctx);
    public static void Removed(DeathCtx _ctx) => SynergyRuleTriggers.Removed(_ctx);
    public static void SwappedOut(SwapOutCtx _ctx) => SynergyRuleTriggers.SwappedOut(_ctx);
    public static void Entered(SpawnCtx _ctx) => SynergyRuleTriggers.Entered(_ctx);

    public static async UniTask AfterAttack(AfterAttackCtx _ctx)
    {
        var t_presentations = new List<Func<UniTask>>();
        SynergyRuleTriggers.AfterAttack(_ctx, (t_effect, t_effectCtx) =>
        {
            if (!(t_effect is IAfterAttackPresentation t_presenter)) return;
            Func<UniTask> t_presentation = t_presenter.CaptureAfterAttackPresentation(t_effectCtx);
            if (t_presentation != null) t_presentations.Add(t_presentation);
        });

        foreach (Func<UniTask> t_presentation in t_presentations)
            await t_presentation();
    }

    public static void BoardChanged(BoardCtx _ctx) => SynergyRuleTriggers.BoardChanged(_ctx);
}
