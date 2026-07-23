using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "AatroxPassive", menuName = "Card Battle/Passives/Aatrox")]
public class AatroxPassive : CardPassive
{
    [SerializeField] string peerlessLabel;
    [SerializeField] string executionLabel;

    public override async UniTask OnAfterAttack(AfterAttackCtx _ctx)
    {
        _ctx.self.attackCount++;
        switch (_ctx.self.attackCount)
        {
            case 1: _ctx.self.runtimeKeywords |= CardKeyword.Peerless;  Notify(_ctx.self, this.peerlessLabel);  break;
            case 2: _ctx.self.runtimeKeywords |= CardKeyword.Execution; Notify(_ctx.self, this.executionLabel); break;
            default: return;
        }
        await Glow(_ctx.self);
    }
}
