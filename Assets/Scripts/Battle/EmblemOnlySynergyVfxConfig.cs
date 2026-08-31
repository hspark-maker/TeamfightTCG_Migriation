/// <summary>고유 연출 없이 엠블럼만 쓰는 시너지용(덩치·비늘처럼 규칙만 있는 것들).
///
/// 비어 있는 게 정상이다 — 모든 값은 베이스(<see cref="SynergyVfxConfig"/>)에 있다.
/// **여기에 슬롯을 늘리지 마라.** 어떤 시너지에 고유 연출이 생기면 그때 전용 자식 타입을 새로 만든다.
/// 그게 이 상속 구조의 전부다(안 쓰는 빈 슬롯이 에셋에 안 생기게 하는 것).</summary>
[UnityEngine.CreateAssetMenu(fileName = "NewSynergyVfx", menuName = "Card Battle/Synergy Vfx/Emblem Only")]
public class EmblemOnlySynergyVfxConfig : SynergyVfxConfig
{
}
