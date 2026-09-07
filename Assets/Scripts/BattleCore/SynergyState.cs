using System.Collections.Generic;

// 활성화된 단일 시너지 1건(어떤 SynergyData가 몇 장으로 어느 티어까지 열렸는지).
public class ActiveSynergy
{
    public SynergyRuntime Runtime;
    public int         Count;
    public int         TierIndex;
    public SynergyTier Tier;
}

// 덱 확정 시 1회 산출되는 시너지 스냅샷. 전투 중 재계산하지 않는다(라이브 집계 아님).
public class SynergyState
{
    public IReadOnlyList<ActiveSynergy> Active { get; }

    public SynergyState(IReadOnlyList<ActiveSynergy> active)
    {
        Active = active ?? new List<ActiveSynergy>();
    }

    public static readonly SynergyState Empty = new SynergyState(new List<ActiveSynergy>());
}
