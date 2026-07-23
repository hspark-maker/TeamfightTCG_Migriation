using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "RammusPassive", menuName = "Card Battle/Passives/Rammus")]
public class RammusPassive : CardPassive
{
    [SerializeField] int    thornDamage = 1;
    [SerializeField] string effectLabel;

    public override async UniTask OnAttacked(AttackedCtx _ctx)
    {
        _ctx.attacker.TakeDamage(this.thornDamage);
        Notify(_ctx.self, this.effectLabel);
        await Glow(_ctx.self);
    }
}
