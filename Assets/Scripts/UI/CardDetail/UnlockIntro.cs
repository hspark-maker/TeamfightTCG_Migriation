using UnityEngine;

/// <summary>전면으로 한 번 안내할 "해금된 개념" 한 줄. 키워드와 시너지를 같은 모양으로 정규화한다 —
/// 둘은 출처만 다를 뿐 유저에게는 같은 사건("새 규칙이 열렸다")이라, 화면을 두 장 만들 이유가 없다.
///
/// <see cref="Key"/>는 세이브에 남는 문자열이다(<see cref="OutgameTutorialProgress.MarkUnlockIntroSeen"/>).
/// 그래서 조립을 이 파일 하나에 가둔다 — 두 곳에서 만들면 접두사 한 글자가 갈리는 날 낙인이 조용히 초기화된다.</summary>
public readonly struct UnlockIntro
{
    const string KeywordPrefix = "kw:";

    /// <summary>시너지는 개념 하나로 센다. 처음 배우는 것은 "시너지라는 규칙"이지 개별 시너지가 아니라서다 —
    /// 시너지마다 나누고 싶어지면 그때 <c>"syn:이름"</c>으로 늘리면 되고, 이 구 키는 그냥 안 쓰이게 된다.</summary>
    const string SynergyKey = "syn";

    /// <summary>낙인 키. 비면 "안내할 것이 아니다"라는 뜻이라 <see cref="OutgameTutorialProgress"/>가 본 것으로 친다.</summary>
    public readonly string Key;

    public readonly Sprite Icon;

    /// <summary>아이콘 배율. 시너지 PNG는 투명 여백이 있어 보정 없이 두면 키워드 아이콘 옆에서 혼자 작아 보인다.</summary>
    public readonly float IconScale;

    public readonly string Name;

    /// <summary>본문. 키워드는 <c>explain</c>, 시너지는 효과 + 발동 요구치(<see cref="SynergyText.Body"/>).</summary>
    public readonly string Body;

    /// <summary>어느 키워드인가. <b>시너지면 None</b>이다 — 데모 무대(<see cref="KeywordDemoStage"/>)가
    /// 이 값으로 대본을 고르므로, 키를 역파싱하지 않게 값으로 들고 다닌다.</summary>
    public readonly CardKeyword Keyword;

    UnlockIntro(string _key, Sprite _icon, float _iconScale, string _name, string _body, CardKeyword _keyword)
    {
        this.Key       = _key;
        this.Icon      = _icon;
        this.IconScale = _iconScale;
        this.Name      = _name;
        this.Body      = _body;
        this.Keyword   = _keyword;
    }

    /// <summary>키워드 한 개. 표를 못 찾거나 표시명이 비면 false — 이름 없는 칸을 세우느니 안 세우는 편이 낫다.
    /// 여러 비트가 켜진 마스크는 받지 않는다(칸 하나가 개념 하나여야 낙인 키도 하나다).</summary>
    public static bool TryForKeyword(KeywordIconConfig _config, CardKeyword _keyword, out UnlockIntro _intro)
    {
        _intro = default;

        if (_config == null || _keyword == CardKeyword.None) return false;
        if (!_config.TryGetEntry(_keyword, out KeywordIconConfig.Entry t_entry)) return false;
        if (string.IsNullOrEmpty(t_entry.displayName)) return false;

        _intro = new UnlockIntro(KeywordPrefix + _keyword, t_entry.icon, 1f,
                                 t_entry.displayName, t_entry.explain, _keyword);
        return true;
    }

    /// <summary>시너지 개념. 어느 시너지로 배우든 키는 하나라, 두 번째부터는 이 값을 만들어도 걸러진다.</summary>
    public static bool TryForSynergy(SynergyData _synergy, out UnlockIntro _intro)
    {
        _intro = default;
        if (_synergy == null) return false;

        // 키워드 자리는 None — 시너지는 카드 한 장의 능력이 아니라 덱 편성 규칙이라 데모 대본이 없다.
        _intro = new UnlockIntro(SynergyKey, _synergy.activeIcon, SynergyIconStrip.IconPadCompensation,
                                 SynergyText.Name(_synergy), SynergyText.Body(_synergy), CardKeyword.None);
        return true;
    }
}
