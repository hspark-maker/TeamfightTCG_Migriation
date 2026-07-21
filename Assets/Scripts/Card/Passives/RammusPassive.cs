using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "RammusPassive", menuName = "Card Battle/Passives/Rammus")]
public class RammusPassive : CardPassive
{
    [SerializeField] int    thornDamage = 1;
    [SerializeField] string effectLabel;

    public override async UniTask OnAttackedBy(CardInstance _self, CardInstance _attacker)
    {
        _attacker.TakeDamage(this.thornDamage);
        Notify(_self, this.effectLabel);
        await Glow(_self);
    }
}
