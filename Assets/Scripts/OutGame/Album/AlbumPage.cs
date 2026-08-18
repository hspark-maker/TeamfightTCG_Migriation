using System.Collections.Generic;

// 앨범 페이지 하나의 런타임 뷰(저작 def에서 파생, 런타임 불변)
public sealed class AlbumPage : AlbumSection
{
    // 테마 내 페이지 인덱스(표시용, 식별 키 아님)
    public int Index { get; }

    // 칸 순서 그대로(null 슬롯 포함 — UI가 빈 칸을 그린다)
    public IReadOnlyList<CardData> Cards { get; }

    internal AlbumPage(
        string _key,
        int _index,
        IReadOnlyList<AlbumRewardDef> _rewards,
        string _themeKey,
        bool _hasStableKey,
        IReadOnlyList<CardData> _cards,
        IReadOnlyList<int> _cardIds)
        : base(_key, _rewards, _cardIds, _hasStableKey ? "p:" + _themeKey + "/" + _key : null)
    {
        Index = _index;
        Cards = _cards;
    }
}
