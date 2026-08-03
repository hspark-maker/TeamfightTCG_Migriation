using UnityEngine;

/// <summary>무리 시너지 연출. 엠블럼(베이스) + 선피해 투사체.
/// 투사체 프리팹이 비어 있으면 볼리는 통째로 생략된다 — 피해는 이미 적용된 뒤라 연출은 선택이다.</summary>
[CreateAssetMenu(fileName = "SwarmSynergyVfx", menuName = "Card Battle/Synergy Vfx/Swarm")]
public class SwarmSynergyVfxConfig : SynergyVfxConfig
{
    [Header("선피해 투사체")]
    // 스폰/반납/정렬 규약은 BattleVfx 소유 — 여기엔 "무엇을 어떤 자세로" 스폰하는지(VfxEntry)만 둔다.
    // id는 쓰지 않는다(BattleVfxId는 여러 곳이 공유하는 공용 연출 축이다).
    public VfxEntry projectile;

    // 베지어 제어점을 직선에서 밀어내는 거리(0이면 직선). 인덱스 패리티로 부호가 갈려 부채꼴이 된다.
    // 예전엔 힐러 커브값(BattleVfxLibrary.healCurveHeight)을 빌려 썼는데, 그건 남의 연출 값이라
    // 힐러를 조정하면 무리 궤적이 같이 흔들렸다.
    public float curveHeight = 0.55f;
}
