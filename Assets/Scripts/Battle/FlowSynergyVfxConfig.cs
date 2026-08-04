using UnityEngine;

/// <summary>흐름 시너지 연출. 엠블럼(베이스) + 그 진영 필드 위로 지나가는 바람.
/// 바람 프리팹이 비어 있으면 바람만 생략된다(엠블럼은 그대로 뜬다).</summary>
[CreateAssetMenu(fileName = "FlowSynergyVfx", menuName = "Card Battle/Synergy Vfx/Flow")]
public class FlowSynergyVfxConfig : SynergyVfxConfig
{
    [Header("발동 바람")]
    // 수명은 항목 lifetime이 쥔다(1회성 스폰). 스폰/정렬 규약은 BattleVfx 소유.
    public VfxEntry wind;

    [Header("스택에 따른 성장 (표시 전용 — 규칙/데미지에는 영향 없음)")]
    [Tooltip("스택 1일 때의 크기 배율")]
    public float windScaleBase = 1f;
    [Tooltip("스택이 1 오를 때마다 더해지는 배율")]
    public float windScalePerStack = 0.16f;
    [Tooltip("배율 상한. 흐름 스택은 무제한으로 자라므로 여기서 끊지 않으면 화면을 덮는다")]
    public float windScaleMax = 2.2f;

    /// <summary>그 스택에서 바람이 커질 배율. 스택 1이 기본 크기가 되도록 (stack-1)만큼만 더한다.</summary>
    public float WindScaleFor(int _stack)
    {
        int   t_extra = Mathf.Max(0, _stack - 1);
        float t_scale = this.windScaleBase + this.windScalePerStack * t_extra;
        return Mathf.Clamp(t_scale, 0.01f, Mathf.Max(0.01f, this.windScaleMax));
    }
}
