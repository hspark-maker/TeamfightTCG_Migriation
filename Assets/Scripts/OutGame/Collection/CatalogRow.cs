using System.Collections.Generic;

// 도감 행 하나의 런타임 뷰(행 def에서 파생, 런타임 불변)
public sealed class CatalogRow
{
    // 행 안정 키 = 행의 첫 유효 카드 번호(위치 기반 아님). 0 = 빈 행
    public int Id { get; }

    // 행 목록상 인덱스(표시용, 식별 키 아님)
    public int Index { get; }

    // 행에 속한 카드들(미authoring 슬롯은 null 가능)
    public IReadOnlyList<CardData> Cards { get; }

    // 행 카드들의 고유 번호(Cards와 인덱스 정합)
    public IReadOnlyList<int> CardIds { get; }

    // 완성 행의 생산 사이클 시간(초, 전역 기본값 해석 완료)
    public float ProductionCycleSeconds { get; }

    // 수확 시 지급할 재화 종류
    public ECurrencyType RewardType { get; }

    // 수확 전 누적 상한
    public long Cap { get; }

    internal CatalogRow(
        int _id,
        int _index,
        IReadOnlyList<CardData> _cards,
        IReadOnlyList<int> _cardIds,
        float _productionCycleSeconds,
        ECurrencyType _rewardType,
        long _cap)
    {
        Id = _id;
        Index = _index;
        Cards = _cards;
        CardIds = _cardIds;
        ProductionCycleSeconds = _productionCycleSeconds;
        RewardType = _rewardType;
        Cap = _cap;
    }
}
