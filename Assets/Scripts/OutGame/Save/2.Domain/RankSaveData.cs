using System.Collections.Generic;
using Firebase.Firestore;

// 랭크(표시용 티어 진행도) 세이브 값 객체
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public class RankSaveData
{
    // 티어는 이 값에서 파생 — 도달 티어는 저장하지 않는다
    [FirestoreProperty("points")] public long Points { get; set; }

    // 수령 완료한 티어 인덱스(순서 무관 — 도달한 보상은 아무거나 먼저 받을 수 있다)
    [FirestoreProperty("claimedTiers")] public List<int> ClaimedTiers { get; set; } = new List<int>();
}
