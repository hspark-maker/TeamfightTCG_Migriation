using System.Collections.Generic;

// 도감 행 하나의 런타임 뷰(행 def에서 파생, 런타임 불변)
public sealed class CatalogRow
{
    // 행 안정 키 = 행의 첫 non-null 카드 키(위치 기반 아님)
    public string Key { get; }

    // 행 목록상 인덱스(표시용, 식별 키 아님)
    public int Index { get; }

    // 행에 속한 카드들(미authoring 슬롯은 null 가능)
    public IReadOnlyList<CardData> Cards { get; }

    // 행 카드들의 안정 키(Cards와 인덱스 정합)
    public IReadOnlyList<string> CardKeys { get; }

    // 완성 행의 생산 사이클 시간(초, 전역 기본값 해석 완료)
    public float ProductionCycleSeconds { get; }

    // 수확 시 지급할 재화 종류
    public ECurrencyType RewardType { get; }

    // 수확 전 누적 상한
    public long Cap { get; }

    internal CatalogRow(
        string _key,
        int _index,
        IReadOnlyList<CardData> _cards,
        IReadOnlyList<string> _cardKeys,
        float _productionCycleSeconds,
        ECurrencyType _rewardType,
        long _cap)
    {
        Key = _key;
        Index = _index;
        Cards = _cards;
        CardKeys = _cardKeys;
        ProductionCycleSeconds = _productionCycleSeconds;
        RewardType = _rewardType;
        Cap = _cap;
    }
}
