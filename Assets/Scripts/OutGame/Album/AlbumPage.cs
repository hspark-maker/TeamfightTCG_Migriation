using System.Collections.Generic;

// 앨범 페이지 하나의 런타임 뷰(저작 def에서 파생, 런타임 불변)
public sealed class AlbumPage
{
    public string Key { get; }

    // 테마 내 페이지 인덱스(표시용, 식별 키 아님)
    public int Index { get; }

    public AlbumRewardDef Reward { get; }

    public string ThemeKey { get; }

    // 칸 순서 그대로(null 슬롯 포함 — UI가 빈 칸을 그린다)
    public IReadOnlyList<CardData> Cards { get; }

    // 완성 판정 모수(null 슬롯 제외)
    public IReadOnlyList<int> CardIds { get; }

    // 테마 키까지 안정해야 참 — 페이지 낙인 키에 테마 키가 들어간다
    public bool HasStableKey { get; }

    // 수령 낙인 키 — 여기 외에 문자열 조립 금지. 불안정 키면 null(보상 영구 Locked)
    public string RewardKey { get; }

    internal AlbumPage(
        string _key,
        int _index,
        AlbumRewardDef _reward,
        string _themeKey,
        bool _hasStableKey,
        IReadOnlyList<CardData> _cards,
        IReadOnlyList<int> _cardIds)
    {
        Key = _key;
        Index = _index;
        Reward = _reward;
        ThemeKey = _themeKey;
        Cards = _cards;
        CardIds = _cardIds;
        HasStableKey = _hasStableKey;
        RewardKey = _hasStableKey ? "p:" + _themeKey + "/" + _key : null;
    }
}
