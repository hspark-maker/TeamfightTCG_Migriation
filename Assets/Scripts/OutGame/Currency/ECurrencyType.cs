// 재화 종류 식별자. CurrencyManager가 배열 인덱스로 사용한다.
// 새 재화는 Count 앞에 추가(순서·기존 값 변경 금지 — 하위호환).
public enum ECurrencyType
{
    Gold,
    Diamond,    // 카드 진화 전용 재화(5·10레벨 문). 랭크 티어명 "다이아몬드"와는 무관.

    Count,  // 종류 개수(항상 마지막) — 배열 크기.
}
