using UnityEngine;

/// <summary>흐름 시너지 연출. 엠블럼(베이스) + 그 진영 필드 위로 지나가는 바람.
/// 바람 프리팹이 비어 있으면 바람만 생략된다(엠블럼은 그대로 뜬다).</summary>
[CreateAssetMenu(fileName = "FlowSynergyVfx", menuName = "Card Battle/Synergy Vfx/Flow")]
public class FlowSynergyVfxConfig : SynergyVfxConfig
{
    [Header("발동 바람")]
    // 수명은 항목 lifetime이 쥔다(1회성 스폰). 스폰/정렬 규약은 BattleVfx 소유.
    public VfxEntry wind;
}
