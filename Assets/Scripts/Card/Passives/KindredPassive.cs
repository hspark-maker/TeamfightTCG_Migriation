using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "KindredPassive", menuName = "Card Battle/Passives/Kindred")]
public class KindredPassive : CardPassive
{
    [SerializeField] string effectLabel;

    public override async UniTask OnAfterAttack(AfterAttackCtx _ctx)
    {
        if (!_ctx.target.IsAlive) return;
        _ctx.target.runtimeKeywords |= CardKeyword.Mark;
        Notify(_ctx.self, this.effectLabel);
        await Glow(_ctx.self);
    }
}
