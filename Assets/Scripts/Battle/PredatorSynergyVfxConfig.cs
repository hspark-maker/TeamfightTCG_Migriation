using UnityEngine;

[CreateAssetMenu(fileName = "PredatorSynergyVfx", menuName = "Card Battle/Synergy Vfx/Predator")]
public class PredatorSynergyVfxConfig : SynergyVfxConfig
{
    [Header("포식 표식 (피격자 자리에서 터진다)")]
    // id는 쓰지 않는다 — BattleVfxId는 여러 곳이 공유하는 공용 축이고, 이건 포식자 전용이다.
    public VfxEntry impact;

    [Header("빨림 발광 (줄기가 나가는 순간 공격자 자리)")]
    [Tooltip("줄기 출발과 **같은 순간** 공격자 자리에서 나는 발광. 표식(impact)이 '물었다'라면 이건 '빨아들인다'다. " +
             "미배선이면 생략된다.")]
    public VfxEntry glow;

    [Header("흡수 궤적 (피격자 → 공격자)")]
    [Tooltip("피격자에서 공격자로 날아가는 궤적. 미배선이면 표식만 터지고 이동은 생략된다.")]
    public VfxEntry trail;

    [Tooltip("한 번에 빨려 들어가는 줄기 개수(3~5 권장). 1이면 한 줄기라 '빨린다'가 아니라 '날아간다'로 읽힌다. " +
             "**개수는 저작값이고 난수가 아니다** — 난수면 두 클라의 화면이 갈린다.")]
    [Min(1)] public int trailCount = 4;

    [Tooltip("베지어 제어점을 직선에서 밀어내는 거리(0이면 직선). 회복이 '빨려 온다'로 읽히게 살짝 휘어 준다. " +
             "여러 줄기는 이 값을 좌우 번갈아 쓰고 바깥 줄기일수록 더 벌어져 부채꼴이 된다.")]
    public float curveHeight = 5.5f;
}
