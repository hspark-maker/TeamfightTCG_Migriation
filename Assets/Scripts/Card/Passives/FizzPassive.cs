using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "FizzPassive", menuName = "Card Battle/Passives/Fizz")]
public class FizzPassive : CardPassive
{
    [SerializeField] string effectLabel;

    public override async UniTask OnSpawn(CardInstance _self)
    {
        _self.runtimeKeywords |= CardKeyword.Invincible;
        Notify(_self, this.effectLabel);
        await Glow(_self);
    }

    public override UniTask OnTurnStart(CardInstance _self)
    {
        _self.runtimeKeywords &= ~CardKeyword.Invincible;
        return UniTask.CompletedTask;
    }
}
