// 재화 종류 식별자. CurrencyManager가 배열 인덱스로 사용한다.
// 새 재화는 Count 앞에 추가(순서·기존 값 변경 금지 — 하위호환).
public enum ECurrencyType
{
    Gold,

    Count,  // 종류 개수(항상 마지막) — 배열 크기.
}
