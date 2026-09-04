/// <summary>시트의 <c>effectType</c> 문자열을 실제 효과 인스턴스로 바꾼다.
///
/// **동작의 진실원은 여기(=클래스 존재)이고 시트는 값만 갖는다.** 시트에 새 타입 이름을 적어도
/// 대응 클래스가 빌드에 없으면 만들 수 없다 — 그래서 수치는 서버로 나가고 동작은 앱 업데이트가 필요하다.
/// 알 수 없는 타입은 null을 돌려주고 호출부가 스펙 오류로 처리한다.
///
/// 인스턴스는 카탈로그 로드 때 티어당 1개만 만들어 그 티어 소속 카드 전원이 공유한다 —
/// 효과 자신은 무상태이고 변이는 전부 CardInstance/BattleField 쪽에 쓰기 때문에 공유가 안전하다.
/// (ScriptableObject 상속을 떼기 전에도, 에셋으로 저작하던 시절에도 같은 공유 구조였다.)
/// </summary>
public static class SynergyEffectFactory
{
    public static SynergyEffect Create(string _effectType) => _effectType switch
    {
        "Stat"      => new StatSynergyEffect(),
        "Flow"      => new FlowSynergyEffect(),
        "Brand"     => new BrandSynergyEffect(),
        "Caretaker" => new CaretakerSynergyEffect(),
        "Predator"  => new PredatorSynergyEffect(),
        "Legacy"    => new LegacySynergyEffect(),
        "Trace"     => new TraceSynergyEffect(),
        _            => null,
    };

    /// <summary>시트 오타를 로드 시점에 드러내기 위한 지원 타입 목록.</summary>
    public static readonly string[] SupportedTypes =
    {
        "Stat", "Flow", "Brand", "Caretaker", "Predator", "Legacy", "Trace",
    };
}
