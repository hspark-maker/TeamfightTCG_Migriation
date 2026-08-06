using System.Collections.Generic;
using UnityEngine;

// 앨범 테마 하나의 런타임 뷰(저작 def에서 파생, 런타임 불변)
public sealed class AlbumTheme
{
    public string Key { get; }

    public string DisplayName { get; }

    public Sprite Icon { get; }

    // 테마 목록상 인덱스(표시용, 식별 키 아님)
    public int Index { get; }

    public AlbumRewardDef Reward { get; }

    public IReadOnlyList<AlbumPage> Pages { get; }

    // 테마 전체 카드 평탄화(null 슬롯 제외)
    public IReadOnlyList<CardData> Cards { get; }

    // Cards와 인덱스 정합(null 슬롯 제외)
    public IReadOnlyList<string> CardKeys { get; }

    public bool HasStableKey { get; }

    // 수령 낙인 키 — 여기 외에 문자열 조립 금지. 불안정 키면 null(보상 영구 Locked)
    public string RewardKey { get; }

    internal AlbumTheme(
        string _key,
        string _displayName,
        Sprite _icon,
        int _index,
        AlbumRewardDef _reward,
        IReadOnlyList<AlbumPage> _pages,
        IReadOnlyList<CardData> _cards,
        IReadOnlyList<string> _cardKeys,
        bool _hasStableKey)
    {
        Key = _key;
        DisplayName = _displayName;
        Icon = _icon;
        Index = _index;
        Reward = _reward;
        Pages = _pages;
        Cards = _cards;
        CardKeys = _cardKeys;
        HasStableKey = _hasStableKey;
        RewardKey = _hasStableKey ? "t:" + _key : null;
    }
}
