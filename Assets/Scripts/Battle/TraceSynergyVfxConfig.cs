using UnityEngine;

/// <summary>추적 시너지 연출. 엠블럼(베이스) + **표식**(공격당한 적 자리에 남는 낙점).
///
/// 엠블럼 줄은 자기 슬롯에서만 뜨는데 추적이 보여줘야 하는 건 "저 적이 찍혔다"다 —
/// 자리가 다르므로 여기 전용 슬롯을 둔다(포식자 impact가 PredatorSynergyVfxConfig에 있는 것과 같은 이유).
///
/// 배선 지점만 여기다. 스폰·정렬·반납 규약은 BattleVfx, 발동 지점은 <see cref="TraceSynergyEffect"/>.
/// 미배선(prefab 없음)이면 조용히 생략된다 — 표식 부여 규칙은 그대로 돈다.</summary>
[CreateAssetMenu(fileName = "TraceSynergyVfx", menuName = "Card Battle/Synergy Vfx/Trace")]
public class TraceSynergyVfxConfig : SynergyVfxConfig
{
    [Header("표식 (표식이 붙는 순간 피격자 자리에서 터진다)")]
    // id는 쓰지 않는다 — BattleVfxId는 여러 곳이 공유하는 공용 축이고, 이건 추적 전용이다.
    public VfxEntry mark;
}
