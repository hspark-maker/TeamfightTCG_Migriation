using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "GwenPassive", menuName = "Card Battle/Passives/Gwen")]
public class GwenPassive : CardPassive
{
    [SerializeField] string effectLabel;

    public override async UniTask OnKill(CardInstance _self, CardInstance _killed)
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
