using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "RammusPassive", menuName = "Card Battle/Passives/Rammus")]
public class RammusPassive : CardPassive
{
    [SerializeField] int    thornDamage = 1;
    [SerializeField] string effectLabel;

    // 동기 void — 가시 반격이 치사 래치보다 먼저 hp에 반영돼야 한다(BattleTimings ★ 참조).
    // 상태변이(TakeDamage)를 먼저 완결하고 연출만 .Forget으로 흘린다.
    public override void OnAttacked(AttackedCtx _ctx)
    {
        _ctx.attacker.TakeDamage(this.thornDamage);
        Notify(_ctx.self, this.effectLabel);
        Glow(_ctx.self).Forget();
    }
}
