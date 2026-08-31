using System.Collections.Generic;
using Firebase.Firestore;

// 앨범 보상 수령 낙인 세이브 값 객체 — 진행도는 저장하지 않는다(소유 파생)
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public class AlbumRewardSaveData
{
    // 수령한 보상의 낙인 키("p:테마/페이지" · "t:테마" · "b")
    [FirestoreProperty("claimedKeys")] public List<string> ClaimedKeys { get; set; } = new List<string>();
}
