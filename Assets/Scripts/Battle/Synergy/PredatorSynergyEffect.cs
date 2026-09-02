using Cysharp.Threading.Tasks;
using UnityEngine;

// 포식자 시너지. 각 포식자 카드가 공격 후 생존 시 실제로 준 기본 공격 피해의 일정 비율만큼 회복.
// 회복 규칙은 CardInstance.Heal에 위임(단일 진실원). 결정론: RNG 미소비, 순수 산술.
[CreateAssetMenu(fileName = "PredatorSynergyEffect", menuName = "Card Battle/Synergy Effect/Predator")]
public class PredatorSynergyEffect : SynergyEffect
{
    [SerializeField, Range(0, 100)] int lifestealPercent = 50;

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
        => Mathf.FloorToInt(_damageDealt * (_percent / 100f));

    public override UniTask OnAfterAttack(AfterAttackCtx _ctx)
    {
        int t_heal = LifestealOf(_ctx.damageDealt, this.lifestealPercent);
        if (t_heal <= 0) return UniTask.CompletedTask;
        _ctx.self.Heal(t_heal);
        SynergyTriggers.Fire(_ctx.self, _ctx.synergy, _ctx.ownField);   // 회복 발동 시에만 배너+배지 pop(스팸 방지)

        // 흡수 연출. 상태(회복)는 위에서 이미 끝났고 여기서부터는 표시뿐이라 await 해도 안전하다
        // — 두 클라가 같은 지점에서 같은 시간을 기다린다(훅 계약: 첫 await 전에 상태변이 완결).
        return PredatorVfx.PlayDrain(CardView.GetView(_ctx.target), CardView.GetView(_ctx.self),
                                     _ctx.synergy?.vfx as PredatorSynergyVfxConfig);
    }
}
