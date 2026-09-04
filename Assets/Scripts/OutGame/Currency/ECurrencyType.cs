// 재화 종류 식별자 — 새 재화는 Count 앞에 추가(순서·기존 값 변경 금지, 세이브 하위호환)
public enum ECurrencyType
{
    Gold,
    Diamond,
    Energy,
    Shard,  // 카드 강화 재화. 중복 카드 전용 재화와는 별개다
    RouletteTicket,  // 룰렛 1회 회전 비용. 서버 지갑 키 개방은 2단계다

    Count,
}
