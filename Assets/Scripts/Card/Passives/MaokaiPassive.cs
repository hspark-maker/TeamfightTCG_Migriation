using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "MaokaiPassive", menuName = "Card Battle/Passives/Maokai")]
public class MaokaiPassive : CardPassive
{
    [SerializeField] int    bonusHpGrant = 3;
    [SerializeField] string effectLabel;

    public override UniTask OnRemoved(DeathCtx _ctx)
    {
        foreach (CardInstance t_ally in _ctx.field.GetActiveCards())
            t_ally.bonusHp += this.bonusHpGrant;
        Notify(_ctx.self, this.effectLabel);
        // glow 생략 (CardView가 이미 사라짐)
        return UniTask.CompletedTask;
    }
}
