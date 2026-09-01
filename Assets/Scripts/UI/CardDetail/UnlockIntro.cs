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

    // 데모 무대가 대본을 고르고 연출 에셋(vfx)을 꺼내는 데 쓴다. id 문자열만 나르면 무대가 레지스트리에서
    // 이미 손에 있던 것을 다시 푸는 두 번째 해석 지점이 생긴다.
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

        // 요구 장수는 본문에 섞지 않는다 — 티어를 줄로 세우는 화면(UnlockIntroRow)이 Synergy를 보고 직접 그린다.
        //
        // 배율은 1이다. SynergyIconStrip.IconPadCompensation(1.39)이 전제한 "시너지 PNG의 투명 여백"은
        // 실측에서 반증됐다 — 현행 8종이 쓰는 아이콘은 전부 여백이 없어 그 보정을 걸면 39% 부푼다.
        // 상수 자체는 건드리지 않는다: 같은 값을 쓰는 다른 세 화면(KeywordExplain·CardDetailChip·ExplainPopup)은
        // 프리팹 rect가 그 보정을 전제로 저작돼 있어 함께 움직이면 그쪽이 깨진다.
        _intro = new UnlockIntro(_synergy.activeIcon, 1f,
                                 SynergyText.Name(_synergy), SynergyText.Effect(_synergy), CardKeyword.None,
                                 _synergy);
        return true;
    }
}
