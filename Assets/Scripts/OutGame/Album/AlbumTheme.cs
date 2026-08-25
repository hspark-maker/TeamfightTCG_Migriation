using System.Collections.Generic;
using UnityEngine;

// 앨범 테마 하나의 런타임 뷰(저작 def에서 파생, 런타임 불변)
public sealed class AlbumTheme : AlbumSection
{
    public string DisplayName { get; }

    // 셀에 붙는 한 줄 소개. 미저작이면 빈 문자열
    public string Description { get; }

    // 준비 중 테마 — 갤러리가 흑백+자물쇠로 그리고, 완성 판정·진행도 모수에서도 빠진다
    public bool IsLocked { get; }

    public Sprite Icon { get; }

    // 셀 스킨 — null이면 셀 프리팹에 저작된 스프라이트를 그대로 둔다
    public Sprite Frame { get; }

    public Sprite NamePlate { get; }

    // 테마 전용 셀 프리팹 — null이면 갤러리 기본 셀. 타입이 GameObject인 건 저작 축이 UI 축을 참조하지 않기 위해서다
    public GameObject CellPrefab { get; }

    public IReadOnlyList<AlbumPage> Pages { get; }

    // 테마 전체 카드 평탄화(null 슬롯 제외) — 페이지의 Cards와 달리 빈 칸이 없다
    public IReadOnlyList<CardData> Cards { get; }

    internal AlbumTheme(
        string _key,
        string _displayName,
        string _description,
        bool _locked,
        Sprite _icon,
        Sprite _frame,
        Sprite _namePlate,
        GameObject _cellPrefab,
        IReadOnlyList<AlbumRewardDef> _rewards,
        IReadOnlyList<AlbumPage> _pages,
        IReadOnlyList<CardData> _cards,
        IReadOnlyList<int> _cardIds,
        bool _hasStableKey)
        : base(_key, _rewards, _cardIds, _hasStableKey ? "t:" + _key : null)
    {
        DisplayName = _displayName;
        Description = _description ?? string.Empty;
        IsLocked = _locked;
        Icon = _icon;
        Frame = _frame;
        NamePlate = _namePlate;
        CellPrefab = _cellPrefab;
        Pages = _pages;
        Cards = _cards;
    }
}
