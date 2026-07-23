using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "TeemoPassive", menuName = "Card Battle/Passives/Teemo")]
public class TeemoPassive : CardPassive
{
    [SerializeField] int    bonusHpGrant = 1;
    [SerializeField] string effectLabel;

    public override async UniTask OnSwappedOut(SwapOutCtx _ctx)
    {
        _ctx.incoming.bonusHp += this.bonusHpGrant;
        Notify(_ctx.self, this.effectLabel);
        await Glow(_ctx.incoming);
    }
}
