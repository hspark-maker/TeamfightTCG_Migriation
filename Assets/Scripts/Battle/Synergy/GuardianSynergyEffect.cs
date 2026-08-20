using Cysharp.Threading.Tasks;
using UnityEngine;

// 수호자 2티어: 배치될 때마다 자신에게 보호막 1개를 부여한다.
// 보호막은 bool이라 중첩되지 않고 갱신되며, 피해 규칙과 소모는 CardInstance가 소유한다.
[CreateAssetMenu(fileName = "GuardianSynergyEffect", menuName = "Card Battle/Synergy Effect/Guardian")]
public class GuardianSynergyEffect : SynergyEffect
{
    public override UniTask OnPlaced(SpawnCtx _ctx) => Grant(_ctx);
    public override UniTask OnEntered(SpawnCtx _ctx) => Grant(_ctx);

    UniTask Grant(SpawnCtx _ctx)
    {
        if (_ctx.self == null || !SynergyApplier.BelongsTo(_ctx.self, _ctx.synergy))
            return UniTask.CompletedTask;

        _ctx.self.GrantShield();
        SynergyTriggers.Fire(_ctx.self, _ctx.synergy, _ctx.field);
        return UniTask.CompletedTask;
    }
}
