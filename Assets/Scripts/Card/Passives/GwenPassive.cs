using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "GwenPassive", menuName = "Card Battle/Passives/Gwen")]
public class GwenPassive : CardPassive
{
    [SerializeField] string effectLabel;

    // 구 OnKill 흡수 — 처치했을 때만 발동(AfterAttack은 공격마다 발화하므로 가드 필수)
    public override async UniTask OnAfterAttack(AfterAttackCtx _ctx)
    {
        if (!_ctx.defenderKilled) return;
        _ctx.self.runtimeKeywords |= CardKeyword.Invincible;
        Notify(_ctx.self, this.effectLabel);
        await Glow(_ctx.self);
    }

    public override UniTask OnTurnBegan(TurnCtx _ctx)
    {
        _ctx.self.runtimeKeywords &= ~CardKeyword.Invincible;
        return UniTask.CompletedTask;
    }
}
