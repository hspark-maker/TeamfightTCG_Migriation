using System.Collections.Generic;

/// <summary>
/// 도감 파생 행 하나의 값 스냅샷(배치 순서에서 파생, 런타임 불변).
/// 소유·완성 상태는 담지 않는다 — 실시간 조회(CatalogRows.IsRowComplete)가 정본.
/// 생성은 CatalogRows 전용(internal 생성자). 모든 노출은 get-only.
/// </summary>
public sealed class CatalogRow
{
    // 행 안정 키 = 행 첫 자리 카드의 안정 키(CardCatalog.KeyOf). 행 인덱스 같은 위치 기반 키가 아니다.
    // 배치가 고정이라 이 키가 안정적. 첫 자리가 미해결(드리프트)이면 null일 수 있다.
    public string Key { get; }

    // 배치 순서상 행 인덱스(0-base). 표시·정렬용 부가정보일 뿐 식별 키가 아니다.
    public int Index { get; }

    // 행에 속한 카드들(배치 순서). 카탈로그 미해결 슬롯은 null을 포함할 수 있다(드리프트).
    public IReadOnlyList<CardData> Cards { get; }

    // 행 카드들의 안정 키(Cards와 인덱스 정합). 미해결/미authoring 슬롯은 null일 수 있다.
    public IReadOnlyList<string> CardKeys { get; }

    internal CatalogRow(string _key, int _index, IReadOnlyList<CardData> _cards, IReadOnlyList<string> _cardKeys)
    {
        Key = _key;
        Index = _index;
        Cards = _cards;
        CardKeys = _cardKeys;
    }
}
