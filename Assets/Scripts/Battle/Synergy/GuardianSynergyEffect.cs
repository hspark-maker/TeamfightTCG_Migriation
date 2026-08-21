using Cysharp.Threading.Tasks;
using UnityEngine;

// 수호자 2티어: 배치될 때마다 자신에게 보호막 1개를 부여한다.
// 보호막은 bool이라 중첩되지 않고 갱신되며, 피해 규칙과 소모는 CardInstance가 소유한다.
[CreateAssetMenu(fileName = "GuardianSynergyEffect", menuName = "Card Battle/Synergy Effect/Guardian")]
public class GuardianSynergyEffect : SynergyEffect
{
    // [Placed] 오프닝 배치는 ApplyDeckSynergy에서 도는데 그건 InitializeViews보다 앞이라 CardView가 없다.
    // 그래서 여기서는 상태(보호막)만 세우고 알림은 걸지 않는다 — 배치 시점 연출의 발화점은
    // 배치 연출이 끝나는 뷰 쪽 하나뿐이다(SynergyTriggers.Placed의 규약과 같다).
    public override UniTask OnPlaced(SpawnCtx _ctx) => Grant(_ctx, false);

    // [Entered] 런타임 등장은 뷰가 이미 있다 — 다른 시너지와 같이 배너+배지 pop을 건다.
    public override UniTask OnEntered(SpawnCtx _ctx) => Grant(_ctx, true);

    UniTask Grant(SpawnCtx _ctx, bool _notify)
    {
        if (_ctx.self == null || !SynergyApplier.BelongsTo(_ctx.self, _ctx.synergy))
            return UniTask.CompletedTask;

        _ctx.self.GrantShield();
        if (_notify) SynergyTriggers.Fire(_ctx.self, _ctx.synergy, _ctx.field);
        return UniTask.CompletedTask;
    }
}
