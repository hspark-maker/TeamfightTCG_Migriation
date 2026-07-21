using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "AatroxPassive", menuName = "Card Battle/Passives/Aatrox")]
public class AatroxPassive : CardPassive
{
    [SerializeField] string peerlessLabel;
    [SerializeField] string executionLabel;

    public override async UniTask OnAfterAttack(CardInstance _self, CardInstance _target, BattleField _ownField)
    {
        _self.attackCount++;
        switch (_self.attackCount)
        {
            case 1: _self.runtimeKeywords |= CardKeyword.Peerless;  Notify(_self, this.peerlessLabel);  break;
            case 2: _self.runtimeKeywords |= CardKeyword.Execution; Notify(_self, this.executionLabel); break;
            default: return;
        }
        await Glow(_self);
    }
}
