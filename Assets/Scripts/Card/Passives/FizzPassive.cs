using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "FizzPassive", menuName = "Card Battle/Passives/Fizz")]
public class FizzPassive : CardPassive
{
    [SerializeField] string effectLabel;

    // 구 OnSpawn이 배치·등장 양쪽 발화 — 동작 보존
    public override UniTask OnPlaced(SpawnCtx _ctx)  => GrantInvincible(_ctx.self);
    public override UniTask OnEntered(SpawnCtx _ctx) => GrantInvincible(_ctx.self);

    private async UniTask GrantInvincible(CardInstance _self)
    {
        _self.runtimeKeywords |= CardKeyword.Invincible;
        Notify(_self, this.effectLabel);
        await Glow(_self);
    }

    public override UniTask OnTurnBegan(TurnCtx _ctx)
    {
        _ctx.self.runtimeKeywords &= ~CardKeyword.Invincible;
        return UniTask.CompletedTask;
    }
}
