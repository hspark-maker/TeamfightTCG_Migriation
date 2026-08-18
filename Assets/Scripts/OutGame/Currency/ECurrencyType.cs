// 재화 종류 식별자 — 새 재화는 Count 앞에 추가(순서·기존 값 변경 금지, 세이브 하위호환)
public enum ECurrencyType
{
    Gold,
    Diamond,
    Energy,
    Shard,  // 카드 조각 — 카드팩 중복 환급으로 들어와 카드 강화 재료로 나간다

    Count,
}
