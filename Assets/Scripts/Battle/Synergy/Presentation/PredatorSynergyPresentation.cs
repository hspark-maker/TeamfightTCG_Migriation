using Cysharp.Threading.Tasks;

/// <summary>포식자 규칙이 캡처한 흡혈 대본을 Unity 뷰와 VFX로 재생한다.</summary>
public static class PredatorSynergyPresentation
{
    public static UniTask Play(PredatorDrainPlan _plan)
    {
        if (_plan == null) return UniTask.CompletedTask;

        SynergyTriggers.Fire(_plan.self, _plan.synergy, _plan.ownField);
        if (!CardCatalog.TryGetSynergyData(_plan.synergy, out SynergyData t_presentation))
            return UniTask.CompletedTask;

        return PredatorVfx.PlayDrain(CardView.GetView(_plan.target), CardView.GetView(_plan.self),
            t_presentation.vfx as PredatorSynergyVfxConfig);
    }
}
