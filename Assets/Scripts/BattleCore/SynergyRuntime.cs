using System;

/// <summary>전투 규칙이 사용하는 시너지 식별자. Unity 표현 에셋과 분리된 값 객체다.</summary>
public sealed class SynergyRuntime : IEquatable<SynergyRuntime>
{
    public const string AssetPrefix = "Data_Synergy_";

    public string SynergyId { get; }

    public SynergyRuntime(string _synergyId)
    {
        string t_id = NormalizeId(_synergyId);
        if (t_id.Length == 0)
            throw new ArgumentException("시너지 ID는 비어 있을 수 없다.", nameof(_synergyId));
        SynergyId = t_id;
    }

    /// <summary>CSV 에셋명, 논리 ID, 한글 별칭을 서버와 같은 영문 논리 ID로 정규화한다.</summary>
    public static string NormalizeId(string _value)
    {
        string t_id = (_value ?? string.Empty).Trim();
        if (t_id.StartsWith(AssetPrefix, StringComparison.Ordinal))
            t_id = t_id.Substring(AssetPrefix.Length);

        return t_id switch
        {
            "덩치" => "Bulk",
            "돌보미" => "Caretaker",
            "포식자" => "Predator",
            "흐름" => "Flow",
            "유산" => "Legacy",
            "비늘" => "Scale",
            "낙인" => "Brand",
            "추적" => "Trace",
            _ => t_id,
        };
    }

    public bool Equals(SynergyRuntime _other)
        => _other != null && string.Equals(SynergyId, _other.SynergyId, StringComparison.Ordinal);

    public override bool Equals(object _obj) => Equals(_obj as SynergyRuntime);
    // 컬렉션 키 전용이다. 프로세스별 문자열 해시는 상태 해시나 와이어 값에 사용하지 않는다.
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(SynergyId);
    public override string ToString() => SynergyId;
}
