using UnityEngine;

/// <summary>전면으로 안내할 해금된 개념 한 줄. 키워드와 시너지를 같은 모양으로 정규화한다.</summary>
public readonly struct UnlockIntro
{
    public readonly Sprite Icon;

    /// <summary>아이콘 배율. 1이면 스프라이트를 그대로 쓴다.</summary>
    public readonly float IconScale;

    public readonly string Name;

    /// <summary>본문. 키워드는 설명문, 시너지는 효과 설명만(발동 요구치는 티어 줄이 맡는다).</summary>
    public readonly string Body;

    /// <summary>어느 키워드인가. 시너지면 None.</summary>
    public readonly CardKeyword Keyword;

    /// <summary>어느 시너지인가. 키워드 줄이면 null.</summary>
    public readonly SynergyData Synergy;

    /// <summary>덱 편성 규칙(시너지)인가.</summary>
    public bool IsSynergy => this.Keyword == CardKeyword.None;

    UnlockIntro(Sprite _icon, float _iconScale, string _name, string _body, CardKeyword _keyword,
                SynergyData _synergy)
    {
        this.Icon      = _icon;
        this.IconScale = _iconScale;
        this.Name      = _name;
        this.Body      = _body;
        this.Keyword   = _keyword;
        this.Synergy   = _synergy;
    }

    /// <summary>키워드 한 개를 담아 돌려준다. 표에 없거나 표시명이 비면 false.</summary>
    public static bool TryForKeyword(KeywordIconConfig _config, CardKeyword _keyword, out UnlockIntro _intro)
    {
        _intro = default;

        if (_config == null || _keyword == CardKeyword.None) return false;
        if (!_config.TryGetEntry(_keyword, out KeywordIconConfig.Entry t_entry)) return false;
        if (string.IsNullOrEmpty(t_entry.displayName)) return false;

        _intro = new UnlockIntro(t_entry.icon, 1f, t_entry.displayName, t_entry.explain, _keyword, null);
        return true;
    }

    /// <summary>시너지 한 개를 담아 돌려준다.</summary>
    public static bool TryForSynergy(SynergyData _synergy, out UnlockIntro _intro)
    {
        _intro = default;
        if (_synergy == null) return false;

        // 배율 1 — 현행 시너지 아이콘은 여백이 없어 SynergyIconStrip.IconPadCompensation(1.39)을 걸면 부푼다.
        _intro = new UnlockIntro(_synergy.activeIcon, 1f,
                                 SynergyText.Name(_synergy), SynergyText.Effect(_synergy), CardKeyword.None,
                                 _synergy);
        return true;
    }
}
