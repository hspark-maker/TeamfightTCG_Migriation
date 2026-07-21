using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "TeemoPassive", menuName = "Card Battle/Passives/Teemo")]
public class TeemoPassive : CardPassive
{
    [SerializeField] int    bonusHpGrant = 1;
    [SerializeField] string effectLabel;

    public override async UniTask OnSwapOut(CardInstance _self, CardInstance _incoming)
    {
        _incoming.bonusHp += this.bonusHpGrant;
        Notify(_self, this.effectLabel);
        await Glow(_incoming);
    }
}
