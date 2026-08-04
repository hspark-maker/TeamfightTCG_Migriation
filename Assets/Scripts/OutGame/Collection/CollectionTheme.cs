using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 도감 테마 하나의 런타임 뷰(테마 def에서 파생, 런타임 불변).
/// 소유·완성 상태는 담지 않는다 — 실시간 조회(CollectionThemes.OwnedCountOf/IsComplete)가 정본.
/// 생성은 CollectionThemes 전용(internal 생성자). 모든 노출은 get-only.
/// </summary>
public sealed class CollectionTheme
{
    // 테마 안정 키(authoring themeId 파생). 표시명·순서가 바뀌어도 불변.
    public string Key { get; }

    // 헤더 표시명.
    public string DisplayName { get; }

    // 헤더 좌측 아이콘(선택). 미지정이면 null.
    public Sprite Icon { get; }

    // 테마 목록상 인덱스(0-base). 표시·정렬용 부가정보일 뿐 식별 키가 아니다.
    public int Index { get; }

    // 테마에 속한 카드들(순서 = 슬롯 번호). authoring 누락 슬롯은 null을 포함할 수 있다(드리프트).
    public IReadOnlyList<CardData> Cards { get; }

    // 테마 카드들의 안정 키(Cards와 인덱스 정합). 미해결/미authoring 슬롯은 null일 수 있다.
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
