using System.Collections.Generic;
using Firebase.Firestore;

// 카드 성장(강화 레벨) 세이브 값 객체 — 키는 카드 고유 번호(CardCatalog.IdOf) 문자열
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public class CardGrowthSaveData
{
    // 진행도가 있는 카드만 담는다(강화·간식·한계돌파). 빈 항목은 CardGrowthManager가 저장 때 거른다.
    [FirestoreProperty("entries")] public Dictionary<string, CardGrowthEntry> Entries { get; set; } = new Dictionary<string, CardGrowthEntry>();
}

// 카드 한 장의 성장 진행도
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public class CardGrowthEntry
{
    [FirestoreProperty("level")] public int Level { get; set; }

    // 간식 보유량(카드팩 중복으로만 쌓인다). 카드별 재화라 전역 잔액 맵에 못 넣어 여기 얹었다.
    [FirestoreProperty("snack")] public int Snack { get; set; }

    // 카드별 한계돌파 단계.
    [FirestoreProperty("limitBreak")] public int LimitBreak { get; set; }
}
