using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "PoppyPassive", menuName = "Card Battle/Passives/Poppy")]
public class PoppyPassive : CardPassive
{
    [SerializeField] string effectLabel;

    public override async UniTask OnDamageDealt(DamageDealtCtx _ctx)
    {
        if (_ctx.isRetaliation) return;
        _ctx.self.bonusHp += Mathf.FloorToInt(_ctx.damage * 0.5f);
        Notify(_ctx.self, this.effectLabel);
        await Glow(_ctx.self);
    }
}
