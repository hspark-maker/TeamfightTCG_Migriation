using System.Collections.Generic;
using Cysharp.Threading.Tasks;

/// <summary>낙인 규칙이 캡처한 대본을 Unity 뷰와 VFX로 재생한다.</summary>
public static class BrandSynergyPresentation
{
    public static async UniTask Play(BrandAttackPlan _plan)
    {
        if (_plan == null) return;

        // 선피해 발동 시에만 배지와 엠블럼을 띄운다. AllMembers 범위 해석에 필드가 필요하다.
        bool t_emblem = SynergyTriggers.Fire(_plan.self, _plan.synergy, _plan.ownField);
        if (!CardCatalog.TryGetSynergyData(_plan.synergy, out SynergyData t_presentation)) return;

        // 엠블럼 → 볼리 → 본 공격 순서를 보존한다.
        float t_wait = t_emblem
            ? SynergyEmblemVfx.DurationOf(t_presentation, SynergyEmblemTiming.Triggered) : 0f;
        if (t_wait > 0f)
            await UniTask.Delay((int)(t_wait * 1000)).SuppressCancellationThrow();

        var t_views = new List<CardView>(_plan.brandCards.Count);
        for (int i = 0; i < _plan.brandCards.Count; i++)
        {
            CardView t_view = CardView.GetView(_plan.brandCards[i]);
            if (t_view != null) t_views.Add(t_view);
        }

        await BrandVolleyVfx.PlayVolley(t_views, CardView.GetView(_plan.defender),
            SplitDamage(_plan.appliedDamage, t_views.Count), _plan.hpBefore, _plan.bonusHpBefore,
            t_presentation.vfx as BrandSynergyVfxConfig);
    }

    /// <summary>실제 적용 피해의 합을 보존하며 앞쪽 발부터 나머지를 배분한다.</summary>
    static int[] SplitDamage(int _total, int _shots)
    {
        var t_out = new int[System.Math.Max(0, _shots)];
        if (t_out.Length == 0) return t_out;

        int t_base = _total / t_out.Length;
        int t_rem = _total % t_out.Length;
        for (int i = 0; i < t_out.Length; i++)
            t_out[i] = t_base + (i < t_rem ? 1 : 0);
        return t_out;
    }
}
