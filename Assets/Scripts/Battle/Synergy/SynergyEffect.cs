using UnityEngine;

/// <summary>
/// 시너지 효과 1건. 활성 조건 = "덱에 이 시너지 카드가 requiredCount 이상"(덱 확정 시 1회 판정, 전투 중 불변).
/// 효과=에셋으로 추가하는 개방-폐쇄 구조 — 서브클래스/에셋만 늘리고 엔진층은 수정하지 않는다.
///
/// **훅은 <see cref="BattleEffect"/>에 선언돼 있다** — 여기엔 아무것도 추가하지 않는다.
/// CardPassive와 훅 목록·시그니처가 완전히 동일하고, 다른 건 활성 조건뿐이다.
/// 어느 타이밍이 있는지·계약이 뭔지는 <see cref="BattleTimings"/>를 봐라.
///
/// 시너지 효과가 쓰는 것:
/// - `_ctx.synergy` — 이 발화를 일으킨 시너지. SynergyApplier.BelongsTo 자기판정 +
///   SynergyTriggers.Fire 배너 태그에 쓴다. (passive 발화면 null이라 시너지 효과엔 항상 값이 있다.)
/// - 디스패처가 BelongsTo 필터를 걸어주는 훅과 그렇지 않은 훅이 섞여 있다 —
///   SynergyTriggers 쪽 주석을 확인하고, 필터가 없으면 효과가 스스로 판정할 것.
/// </summary>
public abstract class SynergyEffect : BattleEffect
{
    /// <summary>시트(SynergyEffectDef.parameters)의 키=값 한 쌍을 이 효과에 꽂는다.
    /// 키는 이 클래스의 필드명과 1:1이고, **모르는 키는 false** — 로더가 그 자리에서 던진다.
    /// 리플렉션을 쓰지 않는 이유는 허용 키 목록의 진실원을 클래스 안에 두기 위해서다.</summary>
    public virtual bool TrySetParam(string _key, string _value) => false;

    protected static int ParseInt(string _value)
        => int.TryParse(_value, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int t_value)
           ? t_value
           : throw new System.FormatException($"정수가 아니다: '{_value}'");

    /// <summary>시트는 bool을 0/1로 적는다. true/false 표기도 받아준다.</summary>
    protected static bool ParseBool(string _value)
        => _value == "1" || string.Equals(_value, "true", System.StringComparison.OrdinalIgnoreCase);

    protected static CardKeyword ParseKeywords(string _value)
    {
        var t_flags = CardKeyword.None;
        if (string.IsNullOrWhiteSpace(_value)) return t_flags;
        foreach (string t_token in _value.Split(new[] { '|', '/' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            string t_name = t_token.Trim();
            if (t_name.Length == 0) continue;
            if (!System.Enum.TryParse(t_name, out CardKeyword t_keyword))
                throw new System.FormatException($"알 수 없는 키워드: '{t_name}'");
            t_flags |= t_keyword;
        }
        return t_flags;
    }
}
