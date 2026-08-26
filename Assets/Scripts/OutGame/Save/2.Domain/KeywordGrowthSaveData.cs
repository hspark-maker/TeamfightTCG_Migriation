using System.Collections.Generic;
using Firebase.Firestore;

// 키워드 강화 세이브 값 객체 — 키는 CardKeyword 값 문자열, 값은 레벨
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public class KeywordGrowthSaveData
{
    [FirestoreProperty("levels")] public Dictionary<string, int> Levels { get; set; } = new Dictionary<string, int>();
}
