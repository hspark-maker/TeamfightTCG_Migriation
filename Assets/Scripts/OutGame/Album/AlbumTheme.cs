using System.Collections.Generic;
using UnityEngine;

// 앨범 테마 하나의 런타임 뷰(저작 def에서 파생, 런타임 불변)
public sealed class AlbumTheme
{
    public string Key { get; }

    public string DisplayName { get; }

    public Sprite Icon { get; }

    // 셀 스킨 — null이면 셀 프리팹에 저작된 스프라이트를 그대로 둔다
    public Sprite Frame { get; }

    public Sprite NamePlate { get; }

    // 테마 전용 셀 프리팹 — null이면 갤러리 기본 셀. 타입이 GameObject인 건 저작 축이 UI 축을 참조하지 않기 위해서다
    public GameObject CellPrefab { get; }

    // 테마 목록상 인덱스(표시용, 식별 키 아님)
    public int Index { get; }

    public IReadOnlyList<AlbumRewardDef> Rewards { get; }

    public IReadOnlyList<AlbumPage> Pages { get; }

    // 테마 전체 카드 평탄화(null 슬롯 제외)
    public IReadOnlyList<CardData> Cards { get; }

    // Cards와 인덱스 정합(null 슬롯 제외)
    public IReadOnlyList<int> CardIds { get; }

    public bool HasStableKey { get; }

    // 수령 낙인 키 — 여기 외에 문자열 조립 금지. 불안정 키면 null(보상 영구 Locked)
    public string RewardKey { get; }

    internal AlbumTheme(
        string _key,
        string _displayName,
        Sprite _icon,
        Sprite _frame,
        Sprite _namePlate,
        GameObject _cellPrefab,
        int _index,
        IReadOnlyList<AlbumRewardDef> _rewards,
        IReadOnlyList<AlbumPage> _pages,
        IReadOnlyList<CardData> _cards,
        IReadOnlyList<int> _cardIds,
        bool _hasStableKey)
    {
        Key = _key;
        DisplayName = _displayName;
        Icon = _icon;
        Frame = _frame;
        NamePlate = _namePlate;
        CellPrefab = _cellPrefab;
        Index = _index;
        Rewards = _rewards;
        Pages = _pages;
        Cards = _cards;
        CardIds = _cardIds;
        HasStableKey = _hasStableKey;
        RewardKey = _hasStableKey ? "t:" + _key : null;
    }
}
