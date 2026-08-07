using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "RammusPassive", menuName = "Card Battle/Passives/Rammus")]
public class RammusPassive : CardPassive
{
    [SerializeField] int    thornDamage = 1;
    [SerializeField] string effectLabel;

    // 전투 종료 예측이 반격 합계를 계산할 때 읽어간다. OnAttacked가 쓰는 것과 **같은 필드**다.
    public override int ThornDamage => this.thornDamage;

    // 동기 void — 가시 반격이 치사 래치보다 먼저 hp에 반영돼야 한다(BattleTimings ★ 참조).
    // 상태변이(TakeDamage)를 먼저 완결하고 연출만 .Forget으로 흘린다.
    public override void OnAttacked(AttackedCtx _ctx)
    {
        _ctx.attacker.TakeDamage(this.thornDamage);
        Notify(_ctx.self, this.effectLabel);
        Glow(_ctx.self).Forget();
    }
}
