using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "MaokaiPassive", menuName = "Card Battle/Passives/Maokai")]
public class MaokaiPassive : CardPassive
{
    [SerializeField] int    bonusHpGrant = 3;
    [SerializeField] string effectLabel;

    public override async UniTask OnDeath(CardInstance _self, BattleField _ownField)
    {
        foreach (CardInstance t_ally in _ownField.GetActiveCards())
            t_ally.bonusHp += this.bonusHpGrant;
        Notify(_self, this.effectLabel);
        // glow 생략 (CardView가 이미 사라짐)
    }
}
