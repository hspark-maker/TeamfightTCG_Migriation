using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "KindredPassive", menuName = "Card Battle/Passives/Kindred")]
public class KindredPassive : CardPassive
{
    [SerializeField] string effectLabel;

    public override async UniTask OnAfterAttack(CardInstance _self, CardInstance _target, BattleField _ownField)
    {
        if (!_target.IsAlive) return;
        _target.runtimeKeywords |= CardKeyword.Mark;
        Notify(_self, this.effectLabel);
        await Glow(_self);
    }
}
