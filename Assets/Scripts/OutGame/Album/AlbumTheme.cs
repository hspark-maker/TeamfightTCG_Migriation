using System.Collections.Generic;
using UnityEngine;

// 앨범 테마 하나의 런타임 뷰(저작 def에서 파생, 런타임 불변)
public sealed class AlbumTheme : AlbumSection
{
    public string DisplayName { get; }

    public Sprite Icon { get; }

    // 셀 스킨 — null이면 셀 프리팹에 저작된 스프라이트를 그대로 둔다
    public Sprite Frame { get; }

    public Sprite NamePlate { get; }

    // 테마 전용 셀 프리팹 — null이면 갤러리 기본 셀. 타입이 GameObject인 건 저작 축이 UI 축을 참조하지 않기 위해서다
    public GameObject CellPrefab { get; }

    public IReadOnlyList<AlbumPage> Pages { get; }

    // 테마 전체 카드 ID 평탄화(0 슬롯 제외) — 페이지의 CardIds와 달리 빈 칸이 없다
    internal AlbumTheme(
        string _key,
        string _displayName,
        Sprite _icon,
        Sprite _frame,
        Sprite _namePlate,
        GameObject _cellPrefab,
        IReadOnlyList<AlbumRewardDef> _rewards,
        IReadOnlyList<AlbumPage> _pages,
        IReadOnlyList<int> _cardIds,
        bool _hasStableKey)
        : base(_key, _rewards, _cardIds, _hasStableKey ? "t:" + _key : null)
    {
        DisplayName = _displayName;
        Icon = _icon;
        Frame = _frame;
        NamePlate = _namePlate;
        CellPrefab = _cellPrefab;
        Pages = _pages;
    }
}
