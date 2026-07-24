using System.Collections.Generic;

/// <summary>
/// 도감 행 하나의 런타임 뷰(행 def에서 파생, 런타임 불변).
/// 소유·완성·생산 진행 상태는 담지 않는다 — 실시간 조회(CatalogRows.IsRowComplete)가 정본.
/// 튜닝(생산속도/보상)은 이미 전역 기본값이 해석 적용된 값이다(소비자는 그대로 쓰면 됨).
/// 생성은 CatalogRows 전용(internal 생성자). 모든 노출은 get-only.
/// </summary>
public sealed class CatalogRow
{
    // 행 안정 키 = 행의 첫 non-null 카드 안정 키(CardCatalog.KeyOf). 행 인덱스 같은 위치 기반 키가 아니다.
    // 배치가 고정이라 이 키가 안정적. 3장 모두 미해결(드리프트)이면 null일 수 있다.
    public string Key { get; }

    // 행 목록상 인덱스(0-base). 표시·정렬용 부가정보일 뿐 식별 키가 아니다.
    public int Index { get; }

    // 행에 속한 카드들(3장 고정). 미authoring/미해결 슬롯은 null을 포함할 수 있다(드리프트).
    public IReadOnlyList<CardData> Cards { get; }

    // 행 카드들의 안정 키(Cards와 인덱스 정합). 미해결/미authoring 슬롯은 null일 수 있다.
    public IReadOnlyList<string> CardKeys { get; }

    // ── 해석된 튜닝 (전역 기본값 적용 완료 — 이 값이 최종값) ──

    // 완성 행의 생산 사이클 시간(초). 행 오버라이드(>0) 또는 전역 기본.
    public float ProductionCycleSeconds { get; }

    // 수확 시 지급할 재화 종류(해석 완료). 수확 로직(C-7)이 CurrencyManager.Earn에 쓸 값.
    public ECurrencyType RewardType { get; }

    // 수확 전 누적 상한(해석 완료, >0 보장 지향). 생산이 이 값에서 멈춘다.
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
