using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

[CreateAssetMenu(fileName = "OrnnPassive", menuName = "Card Battle/Passives/Ornn")]
public class OrnnPassive : CardPassive
{
    [SerializeField] int    bonusHpGrant = 2;
    [SerializeField] string effectLabel;

    public override async UniTask OnAfterAttack(AfterAttackCtx _ctx)
    {
        List<CardInstance> t_others = _ctx.ownField.GetActiveCards();
        t_others.RemoveAll(c => c == _ctx.self);
        if (t_others.Count == 0) return;
        CardInstance t_chosen = t_others[MatchRandom.Range(t_others.Count)];
        t_chosen.bonusHp += this.bonusHpGrant;
        Notify(_ctx.self, this.effectLabel);
        await Glow(t_chosen);
    }
}
