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

    /// <summary>요구 장수만 짧게. 티어가 여럿이면 "2장/4장"으로 잇는다 —
    /// 시너지 이름 옆에 붙일 자리(카드 상세 칩)는 한 줄이라 설명·라벨을 담지 못한다.
    /// 티어가 없으면 빈 문자열(호출측이 이름만 쓰면 된다).</summary>
    public static string Requirement(SynergyData _synergy)
    {
        SynergyTier[] t_tiers = _synergy != null ? _synergy.tiers : null;
        if (t_tiers == null || t_tiers.Length == 0) return string.Empty;

        var t_sb = new StringBuilder();
        foreach (SynergyTier t_tier in t_tiers)
        {
            if (t_tier == null) continue;
            if (t_sb.Length > 0) t_sb.Append('/');
            t_sb.Append(TierRequirement(t_tier));
        }

        return t_sb.ToString();
    }

    /// <summary>효과 설명만(요구치 제외). 요구치를 이름 옆으로 올린 화면이 쓴다.</summary>
    public static string Effect(SynergyData _synergy)
    {
        if (_synergy == null) return string.Empty;
        return string.IsNullOrEmpty(_synergy.effectDescription) ? string.Empty : _synergy.effectDescription;
    }

    /// <summary>티어 한 단계의 요구 장수("3장"). 티어를 줄로 세우는 화면이 쓴다.</summary>
    public static string TierRequirement(SynergyTier _tier)
        => _tier == null ? string.Empty : _tier.requiredCount + "장";

    /// <summary>티어 한 단계가 무엇을 주는지 한 줄("추가 생명력 +1"). 요약이 비면 별칭(label)으로 폴백하고,
    /// 그것마저 비거나 시너지 이름과 같으면 빈 문자열 — 이름이 이미 위에 있어 "2장 — 덩치"가 같은 말을 두 번 한다.</summary>
    public static string TierEffect(SynergyData _synergy, SynergyTier _tier)
    {
        if (_tier == null) return string.Empty;
        if (!string.IsNullOrEmpty(_tier.effectSummary)) return _tier.effectSummary;
        if (string.IsNullOrEmpty(_tier.label)) return string.Empty;

        return _tier.label == Name(_synergy) ? string.Empty : _tier.label;
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
                t_sb.Append(TierRequirement(t_tier));

                string t_effect = TierEffect(_synergy, t_tier);
                if (t_effect.Length > 0) t_sb.Append(" — ").Append(t_effect);
            }
        }

        return t_sb.Length == 0 ? "설명 없음" : t_sb.ToString();
    }
}
