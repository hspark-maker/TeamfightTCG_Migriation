using Cysharp.Threading.Tasks;

// 포식자 시너지. 각 포식자 카드가 공격 후 생존 시 실제로 준 기본 공격 피해의 일정 비율만큼 회복.
// 회복 규칙은 CardInstance.Heal에 위임(단일 진실원). 결정론: RNG 미소비, 순수 산술.
public class PredatorSynergyEffect : SynergyEffect, IAfterAttackPresentation
{
    int lifestealPercent = 50;

    public override bool TrySetParam(string _key, string _value)
    {
        if (_key != nameof(lifestealPercent)) return false;
        this.lifestealPercent = ParseInt(_value);
        return true;
    }

    public override bool TryGetParam(string _key, out int _value)
    {
        _value = this.lifestealPercent;
        return _key == nameof(lifestealPercent);
    }

    /// <summary>흡혈량 계산의 단일 지점 — 전투와 대본이 같은 식을 본다.</summary>
    public static int LifestealOf(int _damageDealt, int _percent)
        => (int)((long)_damageDealt * _percent / 100);

    public override void OnAfterAttack(AfterAttackCtx _ctx)
    {
        int t_heal = LifestealOf(_ctx.damageDealt, this.lifestealPercent);
        if (t_heal <= 0) return;
        _ctx.self.Heal(t_heal);
    }

    public System.Func<UniTask> CaptureAfterAttackPresentation(AfterAttackCtx _ctx)
    {
        int t_heal = LifestealOf(_ctx.damageDealt, this.lifestealPercent);
        if (t_heal <= 0) return null;

        return () =>
        {
            SynergyTriggers.Fire(_ctx.self, _ctx.synergy, _ctx.ownField);
            if (!CardCatalog.TryGetSynergyData(_ctx.synergy, out SynergyData t_presentation))
                return UniTask.CompletedTask;
            return PredatorVfx.PlayDrain(CardView.GetView(_ctx.target), CardView.GetView(_ctx.self),
                                         t_presentation.vfx as PredatorSynergyVfxConfig);
        };
    }
}
