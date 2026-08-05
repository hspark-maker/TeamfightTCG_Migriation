using System.Collections.Generic;
using UnityEngine;

// 도감 테마 하나의 런타임 뷰(테마 def에서 파생, 런타임 불변)
public sealed class CollectionTheme
{
    // 테마 안정 키(표시명·순서가 바뀌어도 불변)
    public string Key { get; }

    // 헤더 표시명
    public string DisplayName { get; }

    // 헤더 좌측 아이콘(선택)
    public Sprite Icon { get; }

    // 테마 목록상 인덱스(표시용, 식별 키 아님)
    public int Index { get; }

    // 테마에 속한 카드들(순서 = 슬롯 번호)
    public IReadOnlyList<CardData> Cards { get; }

    // 테마 카드들의 안정 키(Cards와 인덱스 정합)
    public IReadOnlyList<string> CardKeys { get; }

    internal CollectionTheme(
        string _key,
        string _displayName,
        Sprite _icon,
        int _index,
        IReadOnlyList<CardData> _cards,
        IReadOnlyList<string> _cardKeys)
    {
        Key = _key;
        DisplayName = _displayName;
        Icon = _icon;
        Index = _index;
        Cards = _cards;
        CardKeys = _cardKeys;
    }
}
