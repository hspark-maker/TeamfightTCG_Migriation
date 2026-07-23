using System.Text;

/// <summary>
/// 시너지 설명 문자열 포맷 단일 지점. 덱 편성 툴팁과 카드 정보 팝업이 공용으로 쓴다.
/// 두 곳에 따로 쓰면 포맷이 갈리므로 여기서만 만든다.
/// </summary>
public static class SynergyText
{
    /// <summary>시너지 이름(displayName 비면 에셋명 폴백).</summary>
    public static string Name(SynergyData _synergy)
    {
        if (_synergy == null) return string.Empty;
        return string.IsNullOrEmpty(_synergy.displayName) ? _synergy.name : _synergy.displayName;
    }

    /// <summary>설명 + 티어 목록.
    /// _ownedCount &gt;= 0 이면 보유 수 기준으로 열림(●)/미달(○)을 표시하고,
    /// 음수면 덱 문맥이 없는 것으로 보고 마커 없이 요구치만 나열한다(카드 정보 팝업 용도).</summary>
    public static string Body(SynergyData _synergy, int _ownedCount = -1)
    {
        if (_synergy == null) return string.Empty;

        var t_sb = new StringBuilder();
        if (!string.IsNullOrEmpty(_synergy.effectDescription))
            t_sb.Append(_synergy.effectDescription);

        SynergyTier[] t_tiers = _synergy.tiers;
        if (t_tiers != null && t_tiers.Length > 0)
        {
            if (t_sb.Length > 0) t_sb.Append('\n');
            for (int i = 0; i < t_tiers.Length; i++)
            {
                SynergyTier t_tier = t_tiers[i];
                if (t_tier == null) continue;

                t_sb.Append('\n');
                if (_ownedCount >= 0)
                    t_sb.Append(t_tier.requiredCount <= _ownedCount ? "● " : "○ ");
                t_sb.Append(t_tier.requiredCount).Append('장');
                if (!string.IsNullOrEmpty(t_tier.label))
                    t_sb.Append(" — ").Append(t_tier.label);
            }
        }

        return t_sb.Length == 0 ? "설명 없음" : t_sb.ToString();
    }
}
